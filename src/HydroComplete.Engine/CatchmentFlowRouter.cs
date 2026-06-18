using System;
using System.Collections.Generic;
using System.Linq;

namespace HydroComplete.Engine
{
    /// <summary>How catchment flows were assigned to pipe-network structures.</summary>
    public enum CatchmentAssignmentMethod
    {
        /// <summary>Each catchment routed via OutfallStructureId or name match.</summary>
        OutletStructure,

        /// <summary>No outlet links — flows split area-weighted across headwater structures.</summary>
        AreaWeightedHeadwater,

        /// <summary>No usable topology — uniform Q applied to every pipe.</summary>
        UniformFallback,
    }

    /// <summary>Per-catchment peak flow and structure assignment.</summary>
    public sealed class RoutedCatchmentFlow
    {
        public Catchment Catchment { get; set; } = null!;

        public double PeakFlowCfs { get; set; }

        public string? AssignedStructureId { get; set; }
    }

    /// <summary>Result of routing catchment peak flows through a pipe network.</summary>
    public sealed class CatchmentFlowRouterResult
    {
        public CatchmentAssignmentMethod AssignmentMethod { get; set; }

        /// <summary>Design Q per pipe key (ObjectId handle string in Civil 3D).</summary>
        public Dictionary<string, double> PipeFlowCfs { get; } =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Total inflow at each structure after tributary + upstream accumulation.</summary>
        public Dictionary<string, double> StructureInflowCfs { get; } =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        public List<RoutedCatchmentFlow> CatchmentFlows { get; } = new List<RoutedCatchmentFlow>();

        /// <summary>Uniform Q when <see cref="AssignmentMethod"/> is UniformFallback.</summary>
        public double? UniformFallbackCfs { get; set; }

        /// <summary>Sum of per-catchment peak flows (before network accumulation).</summary>
        public double TotalPeakCfs { get; set; }
    }

    /// <summary>
    /// Routes per-catchment Rational peak flows through a storm pipe network,
    /// accumulating discharge at junctions from headwater to outfall.
    /// </summary>
    public static class CatchmentFlowRouter
    {
        /// <summary>
        /// Route catchments through the pipe topology using per-catchment IDF intensity.
        /// </summary>
        /// <param name="structureIdToName">
        /// Optional map of structure handle → name for outlet name matching.
        /// </param>
        public static CatchmentFlowRouterResult Route(
            IReadOnlyList<Catchment> catchments,
            IReadOnlyList<NetworkPipeLink> pipes,
            IdfCurve idf,
            IReadOnlyDictionary<string, string>? structureIdToName = null,
            double? uniformFallbackCfs = null)
        {
            if (catchments == null) throw new ArgumentNullException(nameof(catchments));
            if (pipes == null) throw new ArgumentNullException(nameof(pipes));
            if (idf == null) throw new ArgumentNullException(nameof(idf));
            if (catchments.Count == 0)
                throw new ArgumentException("At least one catchment is required.", nameof(catchments));

            var result = new CatchmentFlowRouterResult();
            var tributary = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var structureNames = StructureNamesFromPipes(pipes, structureIdToName);

            foreach (Catchment cm in catchments)
            {
                var peak = Rational.Peak(cm, idf);
                var routed = new RoutedCatchmentFlow
                {
                    Catchment = cm,
                    PeakFlowCfs = peak.PeakFlowCfs,
                };
                result.CatchmentFlows.Add(routed);
                result.TotalPeakCfs += peak.PeakFlowCfs;
            }

            var knownStructures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (NetworkPipeLink link in pipes)
            {
                if (!string.IsNullOrEmpty(link.UpstreamStructureId))
                    knownStructures.Add(link.UpstreamStructureId);
                if (!string.IsNullOrEmpty(link.DownstreamStructureId))
                    knownStructures.Add(link.DownstreamStructureId);
            }

            int assignedCount = AssignCatchmentsToStructures(
                result.CatchmentFlows, tributary, structureNames, knownStructures);

            var unassigned = result.CatchmentFlows
                .Where(r => string.IsNullOrEmpty(r.AssignedStructureId))
                .ToList();

            if (unassigned.Count > 0 && pipes.Count > 0)
                AreaWeightUnassignedToHeadwaters(unassigned, tributary, pipes);

            if (pipes.Count == 0 || tributary.Count == 0)
            {
                double uniform = uniformFallbackCfs ?? result.TotalPeakCfs;
                if (uniform <= 0)
                    throw new ArgumentOutOfRangeException(nameof(uniformFallbackCfs),
                        "Uniform fallback flow must be positive when topology is unavailable.");

                result.AssignmentMethod = CatchmentAssignmentMethod.UniformFallback;
                result.UniformFallbackCfs = uniform;
                foreach (NetworkPipeLink link in pipes)
                    result.PipeFlowCfs[link.PipeKey] = uniform;
                return result;
            }

            result.AssignmentMethod = assignedCount == catchments.Count
                ? CatchmentAssignmentMethod.OutletStructure
                : assignedCount > 0
                    ? CatchmentAssignmentMethod.OutletStructure
                    : CatchmentAssignmentMethod.AreaWeightedHeadwater;

            if (assignedCount == 0)
                result.AssignmentMethod = CatchmentAssignmentMethod.AreaWeightedHeadwater;

            foreach (var group in pipes.GroupBy(p => p.NetworkName, StringComparer.OrdinalIgnoreCase))
                RouteNetwork(group.ToList(), tributary, result);

            return result;
        }

        private static int AssignCatchmentsToStructures(
            IReadOnlyList<RoutedCatchmentFlow> catchmentFlows,
            Dictionary<string, double> tributary,
            Dictionary<string, string> structureNames,
            HashSet<string> knownStructures)
        {
            int assignedCount = 0;
            foreach (RoutedCatchmentFlow routed in catchmentFlows)
            {
                Catchment cm = routed.Catchment;
                string? structId = ResolveStructureId(cm, structureNames, knownStructures);
                if (string.IsNullOrEmpty(structId))
                    continue;

                routed.AssignedStructureId = structId;
                AddTributary(tributary, structId, routed.PeakFlowCfs);
                assignedCount++;
            }

            return assignedCount;
        }

        private static string? ResolveStructureId(
            Catchment catchment,
            Dictionary<string, string> structureNames,
            HashSet<string> knownStructures)
        {
            if (!string.IsNullOrWhiteSpace(catchment.OutfallStructureId))
            {
                string id = catchment.OutfallStructureId.Trim();
                if (knownStructures.Contains(id))
                    return id;
            }

            if (string.IsNullOrWhiteSpace(catchment.OutfallStructureName))
                return null;

            string target = catchment.OutfallStructureName.Trim();
            foreach (var pair in structureNames)
            {
                if (string.Equals(pair.Value, target, StringComparison.OrdinalIgnoreCase)
                    && knownStructures.Contains(pair.Key))
                {
                    return pair.Key;
                }
            }

            return null;
        }

        private static void AreaWeightUnassignedToHeadwaters(
            IReadOnlyList<RoutedCatchmentFlow> unassigned,
            Dictionary<string, double> tributary,
            IReadOnlyList<NetworkPipeLink> pipes)
        {
            foreach (var group in pipes.GroupBy(p => p.NetworkName, StringComparer.OrdinalIgnoreCase))
            {
                var headwaters = FindHeadwaterStructures(group.ToList());
                if (headwaters.Count == 0) continue;

                if (headwaters.Count == 1)
                {
                    string hw = headwaters[0];
                    double totalQ = 0.0;
                    foreach (RoutedCatchmentFlow routed in unassigned)
                    {
                        totalQ += routed.PeakFlowCfs;
                        routed.AssignedStructureId ??= hw;
                    }

                    AddTributary(tributary, hw, totalQ);
                    continue;
                }

                double totalArea = 0.0;
                foreach (RoutedCatchmentFlow routed in unassigned)
                    totalArea += routed.Catchment.AreaAcres;

                if (totalArea <= 0)
                {
                    double perHeadwater = unassigned.Sum(r => r.PeakFlowCfs) / headwaters.Count;
                    foreach (string hw in headwaters)
                    {
                        AddTributary(tributary, hw, perHeadwater);
                        foreach (RoutedCatchmentFlow routed in unassigned)
                            routed.AssignedStructureId ??= hw;
                    }
                    continue;
                }

                foreach (RoutedCatchmentFlow routed in unassigned)
                {
                    double share = routed.Catchment.AreaAcres / totalArea;
                    double perHeadwater = routed.PeakFlowCfs * share;
                    foreach (string hw in headwaters)
                    {
                        AddTributary(tributary, hw, perHeadwater / headwaters.Count);
                        routed.AssignedStructureId ??= hw;
                    }
                }
            }
        }

        private static void RouteNetwork(
            List<NetworkPipeLink> pipes,
            Dictionary<string, double> globalTributary,
            CatchmentFlowRouterResult result)
        {
            var byUpstream = new Dictionary<string, List<NetworkPipeLink>>(StringComparer.OrdinalIgnoreCase);
            var downstreamStructs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (NetworkPipeLink link in pipes)
            {
                downstreamStructs.Add(link.DownstreamStructureId);
                if (!byUpstream.TryGetValue(link.UpstreamStructureId, out List<NetworkPipeLink>? list))
                {
                    list = new List<NetworkPipeLink>();
                    byUpstream[link.UpstreamStructureId] = list;
                }
                list.Add(link);
            }

            var structureInflow = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (NetworkPipeLink link in pipes)
            {
                EnsureStructure(structureInflow, link.UpstreamStructureId);
                EnsureStructure(structureInflow, link.DownstreamStructureId);
            }

            foreach (var pair in globalTributary)
            {
                if (structureInflow.ContainsKey(pair.Key))
                    structureInflow[pair.Key] = pair.Value;
            }

            var headwaters = byUpstream.Keys
                .Where(id => !downstreamStructs.Contains(id))
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (headwaters.Count == 0)
            {
                foreach (NetworkPipeLink link in pipes)
                    result.PipeFlowCfs[link.PipeKey] = result.TotalPeakCfs;
                return;
            }

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>(headwaters);

            while (queue.Count > 0)
            {
                string structId = queue.Dequeue();
                if (!byUpstream.TryGetValue(structId, out List<NetworkPipeLink>? outgoing))
                    continue;

                foreach (NetworkPipeLink link in outgoing.OrderBy(p => p.PipeName, StringComparer.OrdinalIgnoreCase))
                {
                    if (!visited.Add(link.PipeKey)) continue;

                    double pipeFlow = structureInflow.TryGetValue(structId, out double inflow) ? inflow : 0.0;
                    result.PipeFlowCfs[link.PipeKey] = pipeFlow;

                    string ds = link.DownstreamStructureId;
                    if (!structureInflow.ContainsKey(ds))
                        structureInflow[ds] = 0.0;
                    structureInflow[ds] += pipeFlow;

                    if (byUpstream.ContainsKey(ds))
                        queue.Enqueue(ds);
                }
            }

            foreach (NetworkPipeLink link in pipes)
            {
                if (!result.PipeFlowCfs.ContainsKey(link.PipeKey))
                {
                    double inflow = structureInflow.TryGetValue(link.UpstreamStructureId, out double q) ? q : 0.0;
                    result.PipeFlowCfs[link.PipeKey] = inflow;
                }
            }

            foreach (var pair in structureInflow)
            {
                if (!result.StructureInflowCfs.ContainsKey(pair.Key))
                    result.StructureInflowCfs[pair.Key] = pair.Value;
                else
                    result.StructureInflowCfs[pair.Key] = Math.Max(
                        result.StructureInflowCfs[pair.Key], pair.Value);
            }
        }

        private static List<string> FindHeadwaterStructures(List<NetworkPipeLink> pipes)
        {
            var upstream = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var downstream = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (NetworkPipeLink link in pipes)
            {
                upstream.Add(link.UpstreamStructureId);
                downstream.Add(link.DownstreamStructureId);
            }

            return upstream
                .Where(id => !downstream.Contains(id))
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Builds a structure-name lookup from pipe endpoint names.</summary>
        public static Dictionary<string, string> StructureNamesFromPipes(
            IReadOnlyList<NetworkPipeLink> pipes,
            IReadOnlyDictionary<string, string>? structureIdToName)
        {
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (structureIdToName == null) return names;

            foreach (NetworkPipeLink link in pipes)
            {
                if (structureIdToName.TryGetValue(link.UpstreamStructureId, out string? usName)
                    && !string.IsNullOrWhiteSpace(usName))
                {
                    names[link.UpstreamStructureId] = usName;
                }

                if (structureIdToName.TryGetValue(link.DownstreamStructureId, out string? dsName)
                    && !string.IsNullOrWhiteSpace(dsName))
                {
                    names[link.DownstreamStructureId] = dsName;
                }
            }

            return names;
        }

        private static void AddTributary(Dictionary<string, double> tributary, string structId, double flowCfs)
        {
            if (!tributary.TryGetValue(structId, out double existing))
                tributary[structId] = flowCfs;
            else
                tributary[structId] = existing + flowCfs;
        }

        private static void EnsureStructure(Dictionary<string, double> structureInflow, string structId)
        {
            if (!structureInflow.ContainsKey(structId))
                structureInflow[structId] = 0.0;
        }
    }
}