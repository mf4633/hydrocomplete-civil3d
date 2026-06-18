using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.Geometry;
using HydroComplete.Engine;

namespace HydroComplete.Civil3D.Reading
{
    /// <summary>
    /// Orders gravity pipes upstream-to-downstream within each network and builds
    /// <see cref="NetworkReach"/> inputs for steady HGL calculations.
    /// </summary>
    public static class NetworkTopology
    {
        public sealed class OrderedNetwork
        {
            public string NetworkName { get; set; } = "";
            public List<ReadPipe> OrderedPipes { get; } = new List<ReadPipe>();
            public double MaxUpstreamInvertFt { get; set; }
        }

        /// <summary>
        /// v0.2 ordering: sort pipes by upstream invert descending (highest first).
        /// Does not walk structure connectivity; adequate for simple single-branch
        /// mains but can mis-order loops, branches, or disconnected segments.
        /// Prefer <see cref="BuildOrderedNetworks"/> when structure IDs are present.
        /// </summary>
        public static List<ReadPipe> OrderPipesDownstream(IReadOnlyList<ReadPipe> pipes)
        {
            if (pipes == null) throw new ArgumentNullException(nameof(pipes));
            return pipes
                .OrderByDescending(p => p.UpstreamInvertFt)
                .ThenBy(p => p.PipeName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Walks structure connectivity from headwater structures (no upstream pipe)
        /// to order pipes downstream. Falls back to invert-descending sort when the
        /// graph is ambiguous (loops, multiple outfalls, missing structure links).
        /// </summary>
        public static List<OrderedNetwork> BuildOrderedNetworks(IReadOnlyList<ReadPipe> pipes)
        {
            if (pipes == null) throw new ArgumentNullException(nameof(pipes));

            var networks = new List<OrderedNetwork>();
            foreach (var group in pipes.GroupBy(p => p.NetworkName, StringComparer.OrdinalIgnoreCase))
            {
                var ordered = OrderNetwork(group.Key, group.ToList());
                if (ordered.OrderedPipes.Count > 0)
                    networks.Add(ordered);
            }

            return networks;
        }

        private static OrderedNetwork OrderNetwork(string networkName, List<ReadPipe> pipes)
        {
            var result = new OrderedNetwork { NetworkName = networkName };
            if (pipes.Count == 0) return result;

            var byUpstream = new Dictionary<string, List<ReadPipe>>(StringComparer.OrdinalIgnoreCase);
            var downstreamStructs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (ReadPipe rp in pipes)
            {
                string us = rp.UpstreamStructureId.Handle.ToString();
                string ds = rp.DownstreamStructureId.Handle.ToString();
                downstreamStructs.Add(ds);

                if (!byUpstream.TryGetValue(us, out List<ReadPipe>? list))
                {
                    list = new List<ReadPipe>();
                    byUpstream[us] = list;
                }
                list.Add(rp);
            }

            var headwaterStructs = byUpstream.Keys
                .Where(id => !downstreamStructs.Contains(id))
                .ToList();

            if (headwaterStructs.Count == 0)
            {
                foreach (ReadPipe rp in OrderPipesDownstream(pipes))
                    result.OrderedPipes.Add(rp);
            }
            else
            {
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var queue = new Queue<string>(headwaterStructs.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));

                while (queue.Count > 0)
                {
                    string structId = queue.Dequeue();
                    if (!byUpstream.TryGetValue(structId, out List<ReadPipe>? outgoing)) continue;

                    foreach (ReadPipe rp in outgoing.OrderBy(p => p.PipeName, StringComparer.OrdinalIgnoreCase))
                    {
                        string key = rp.PipeId.Handle.ToString();
                        if (!visited.Add(key)) continue;

                        result.OrderedPipes.Add(rp);
                        string ds = rp.DownstreamStructureId.Handle.ToString();
                        if (byUpstream.ContainsKey(ds))
                            queue.Enqueue(ds);
                    }
                }

                foreach (ReadPipe rp in pipes)
                {
                    string key = rp.PipeId.Handle.ToString();
                    if (!visited.Contains(key))
                        result.OrderedPipes.Add(rp);
                }
            }

            if (result.OrderedPipes.Count > 0)
            {
                result.MaxUpstreamInvertFt = result.OrderedPipes
                    .Select(p => p.UpstreamInvertFt)
                    .DefaultIfEmpty(0.0)
                    .Max();
            }

            return result;
        }

        /// <summary>
        /// Builds HGL reaches from ordered pipes using Manning normal depth at design Q
        /// (partial-flow area and R per reach).
        /// </summary>
        public static List<NetworkReach> BuildReaches(IReadOnlyList<ReadPipe> pipes, double designFlowCfs)
            => BuildReaches(pipes, designFlowCfs, includeJunctionLosses: false);

        /// <summary>
        /// Builds HGL reaches with optional HEC-22 junction K at internal manholes.
        /// Uses Manning normal depth at design Q by default.
        /// </summary>
        public static List<NetworkReach> BuildReaches(
            IReadOnlyList<ReadPipe> pipes, double designFlowCfs, bool includeJunctionLosses)
            => BuildReaches(pipes, designFlowCfs, includeJunctionLosses, useNormalDepth: true);

        /// <summary>
        /// Builds HGL reaches with optional junction losses and geometry mode.
        /// </summary>
        public static List<NetworkReach> BuildReaches(
            IReadOnlyList<ReadPipe> pipes, double designFlowCfs, bool includeJunctionLosses,
            bool useNormalDepth)
            => BuildReaches(pipes, designFlowCfs, includeJunctionLosses, useNormalDepth, pipeFlowCfs: null);

        /// <summary>
        /// Builds HGL reaches with per-pipe design flows (routed catchment Q).
        /// </summary>
        public static List<NetworkReach> BuildReaches(
            IReadOnlyList<ReadPipe> pipes,
            IReadOnlyDictionary<string, double> pipeFlowCfs,
            bool includeJunctionLosses,
            bool useNormalDepth = true)
        {
            if (pipes == null) throw new ArgumentNullException(nameof(pipes));
            if (pipeFlowCfs == null) throw new ArgumentNullException(nameof(pipeFlowCfs));
            return BuildReaches(pipes, uniformDesignFlowCfs: 0.0, includeJunctionLosses, useNormalDepth, pipeFlowCfs);
        }

        private static List<NetworkReach> BuildReaches(
            IReadOnlyList<ReadPipe> pipes,
            double uniformDesignFlowCfs,
            bool includeJunctionLosses,
            bool useNormalDepth,
            IReadOnlyDictionary<string, double>? pipeFlowCfs)
        {
            if (pipes == null) throw new ArgumentNullException(nameof(pipes));

            var inflowCountByStructure = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (ReadPipe pipe in pipes)
            {
                if (pipe.DownstreamStructureId.IsNull) continue;

                string structureKey = pipe.DownstreamStructureId.Handle.ToString();
                inflowCountByStructure[structureKey] =
                    inflowCountByStructure.GetValueOrDefault(structureKey) + 1;
            }

            var reaches = new List<NetworkReach>(pipes.Count);
            for (int i = 0; i < pipes.Count; i++)
            {
                ReadPipe rp = pipes[i];
                double designQ = ResolveDesignFlow(rp, uniformDesignFlowCfs, pipeFlowCfs);
                NetworkReach reach = useNormalDepth
                    ? ToReachNormalDepth(rp, designQ)
                    : ToReach(rp, designQ);

                if (!rp.DownstreamStructureId.IsNull)
                {
                    string structureKey = rp.DownstreamStructureId.Handle.ToString();
                    if (inflowCountByStructure.TryGetValue(structureKey, out int inflowCount))
                        reach.DownstreamInflowCount = inflowCount;
                }

                if (i < pipes.Count - 1)
                {
                    ReadPipe next = pipes[i + 1];
                    if (!rp.DownstreamStructureId.IsNull
                        && rp.DownstreamStructureId == next.UpstreamStructureId)
                    {
                        reach.HasContinuingOutflow = true;
                        reach.DeflectionAngleDeg = PlanDeflectionDegrees(rp, next);

                        if (includeJunctionLosses)
                            reach.JunctionLossK = Hec22.DefaultManholeK;
                    }
                }

                reaches.Add(reach);
            }

            return reaches;
        }

        /// <summary>
        /// Plan-view deflection between consecutive pipes at a shared structure (0° = straight-through).
        /// </summary>
        internal static double PlanDeflectionDegrees(ReadPipe incoming, ReadPipe outgoing)
        {
            (double inX, double inY) = PlanFlowDirection(incoming);
            (double outX, double outY) = PlanFlowDirection(outgoing);

            double dot = inX * outX + inY * outY;
            dot = Math.Max(-1.0, Math.Min(1.0, dot));
            return Math.Acos(dot) * 180.0 / Math.PI;
        }

        private static (double X, double Y) PlanFlowDirection(ReadPipe pipe)
        {
            Point3d upstream = pipe.UpstreamStructureId == pipe.StartStructureId
                ? pipe.StartPoint
                : pipe.EndPoint;
            Point3d downstream = pipe.DownstreamStructureId == pipe.EndStructureId
                ? pipe.EndPoint
                : pipe.StartPoint;

            double dx = downstream.X - upstream.X;
            double dy = downstream.Y - upstream.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 1e-9)
                return (1.0, 0.0);

            return (dx / length, dy / length);
        }

        private static double ResolveDesignFlow(
            ReadPipe rp,
            double uniformDesignFlowCfs,
            IReadOnlyDictionary<string, double>? pipeFlowCfs)
        {
            if (pipeFlowCfs == null)
                return uniformDesignFlowCfs;

            string key = rp.PipeId.Handle.ToString();
            if (pipeFlowCfs.TryGetValue(key, out double routed) && routed > 0)
                return routed;

            return uniformDesignFlowCfs > 0 ? uniformDesignFlowCfs : routed;
        }

        /// <summary>
        /// Full-barrel circular area (pi*D²/4) and hydraulic radius (R = D/4).
        /// </summary>
        public static NetworkReach ToReach(ReadPipe rp, double designFlowCfs)
        {
            string name = string.IsNullOrEmpty(rp.PipeName) ? rp.PipeId.Handle.ToString() : rp.PipeName;
            return ReachFactory.FromFullBarrel(rp.Segment, designFlowCfs, rp.LengthFt, name);
        }

        /// <summary>
        /// Manning normal depth at design Q with partial-flow area, R, and velocity heads.
        /// </summary>
        public static NetworkReach ToReachNormalDepth(ReadPipe rp, double designFlowCfs)
        {
            string name = string.IsNullOrEmpty(rp.PipeName) ? rp.PipeId.Handle.ToString() : rp.PipeName;
            return ReachFactory.FromNormalDepth(rp.Segment, designFlowCfs, rp.LengthFt, name);
        }
    }
}