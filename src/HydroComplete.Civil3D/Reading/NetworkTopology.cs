using System;
using System.Collections.Generic;
using System.Linq;
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
        /// Builds HGL reaches from ordered pipes using full-barrel circular area and
        /// hydraulic radius (R = D/4) with a uniform design flow.
        /// </summary>
        public static List<NetworkReach> BuildReaches(IReadOnlyList<ReadPipe> pipes, double designFlowCfs)
            => BuildReaches(pipes, designFlowCfs, includeJunctionLosses: false);

        /// <summary>
        /// Builds HGL reaches with optional HEC-22 junction K at internal manholes.
        /// </summary>
        public static List<NetworkReach> BuildReaches(
            IReadOnlyList<ReadPipe> pipes, double designFlowCfs, bool includeJunctionLosses)
        {
            if (pipes == null) throw new ArgumentNullException(nameof(pipes));

            var reaches = new List<NetworkReach>(pipes.Count);
            for (int i = 0; i < pipes.Count; i++)
            {
                ReadPipe rp = pipes[i];
                NetworkReach reach = ToReach(rp, designFlowCfs);

                if (includeJunctionLosses && i < pipes.Count - 1)
                {
                    ReadPipe next = pipes[i + 1];
                    if (rp.DownstreamStructureId == next.UpstreamStructureId)
                        reach.JunctionLossK = Hec22.DefaultManholeK;
                }

                reaches.Add(reach);
            }

            return reaches;
        }

        public static NetworkReach ToReach(ReadPipe rp, double designFlowCfs)
        {
            double d = rp.Segment.DiameterFt;
            double areaFull = Math.PI * d * d / 4.0;
            double rFull = d / 4.0;

            return new NetworkReach
            {
                Name = string.IsNullOrEmpty(rp.PipeName) ? rp.PipeId.Handle.ToString() : rp.PipeName,
                LengthFt = rp.LengthFt,
                ManningN = rp.Segment.ManningN,
                AreaFt2 = areaFull,
                HydRadiusFt = rFull,
                FlowCfs = designFlowCfs,
            };
        }
    }
}