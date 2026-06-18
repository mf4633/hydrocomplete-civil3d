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
    /// accumulating discharge at junctions from headwater to outfall in true
    /// topological order (so trunk pipes below confluences carry the full sum of
    /// every upstream branch, regardless of branch length).
    /// </summary>
    public static class CatchmentFlowRouter
    {
        /// <summary>Route catchments through the pipe topology using per-catchment IDF intensity.</summary>
        /// <param name="catchments">Catchments to route (peak Q computed per catchment Tc).</param>
        /// <param name="pipes">Pipe links defining network connectivity.</param>
        /// <param name="idf">IDF curve for per-catchment design intensity.</param>
        /// <param name="structureIdToName">Optional structure handle → name map for outlet name matching.</param>
        /// <param name="uniformFallbackCfs">Uniform Q to apply when no usable topology exists.</param>
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

            // Replace null/empty structure ids with per-pipe synthetic ids so a pipe
            // with an unconnected end (e.g. an outfall with no structure) can never
            // produce a null dictionary key, and so distinct unconnected ends don't
            // collide on the empty string.
            var links = NormalizePipes(pipes);

            var result = new CatchmentFlowRouterResult();
            var tributary = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var structureNames = StructureNamesFromPipes(links, structureIdToName);

            foreach (Catchment cm in catchments)
            {
                var peak = Rational.Peak(cm, idf);
                result.CatchmentFlows.Add(new RoutedCatchmentFlow
                {
                    Catchment = cm,
                    PeakFlowCfs = peak.PeakFlowCfs,
                });
                result.TotalPeakCfs += peak.PeakFlowCfs;
            }

            var knownStructures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (NetworkPipeLink link in links)
            {
                knownStructures.Add(link.UpstreamStructureId);
                knownStructures.Add(link.DownstreamStructureId);
            }

            int assignedCount = AssignCatchmentsToStructures(
                result.CatchmentFlows, tributary, structureNames, knownStructures);

            var unassigned = result.CatchmentFlows
                .Where(r => string.IsNullOrEmpty(r.AssignedStructureId))
                .ToList();

            if (unassigned.Count > 0 && links.Count > 0)
                AreaWeightUnassignedToHeadwaters(unassigned, tributary, links);

            if (links.Count == 0 || tributary.Count == 0)
            {
                double uniform = uniformFallbackCfs ?? result.TotalPeakCfs;
                if (uniform <= 0)
                    throw new ArgumentOutOfRangeException(nameof(uniformFallbackCfs),
                        "Uniform fallback flow must be positive when topology is unavailable.");

                result.AssignmentMethod = CatchmentAssignmentMethod.UniformFallback;
                result.UniformFallbackCfs = uniform;
                foreach (NetworkPipeLink link in links)
                    result.PipeFlowCfs[link.PipeKey] = uniform;
                return result;
            }

            result.AssignmentMethod = assignedCount > 0
                ? CatchmentAssignmentMethod.OutletStructure
                : CatchmentAssignmentMethod.AreaWeightedHeadwater;

            foreach (var group in links.GroupBy(p => p.NetworkName, StringComparer.OrdinalIgnoreCase))
                RouteNetwork(group.ToList(), tributary, result);

            return result;
        }

        private static List<NetworkPipeLink> NormalizePipes(IReadOnlyList<NetworkPipeLink> pipes)
        {
            var normalized = new List<NetworkPipeLink>(pipes.Count);
            foreach (NetworkPipeLink p in pipes)
            {
                normalized.Add(new NetworkPipeLink
                {
                    PipeKey = p.PipeKey,
                    NetworkName = p.NetworkName,
                    PipeName = p.PipeName,
                    UpstreamStructureId = string.IsNullOrWhiteSpace(p.UpstreamStructureId)
                        ? "__src::" + p.PipeKey
                        : p.UpstreamStructureId,
                    DownstreamStructureId = string.IsNullOrWhiteSpace(p.DownstreamStructureId)
                        ? "__out::" + p.PipeKey
                        : p.DownstreamStructureId,
                });
            }

            return normalized;
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
                AddTributary(tributary, structId!, routed.PeakFlowCfs);
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

                double totalArea = unassigned.Sum(r => r.Catchment.AreaAcres);

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

        /// <summary>
        /// Accumulates flow from headwaters to outfall in topological (Kahn) order.
        /// A structure's outgoing pipe(s) are assigned only after EVERY pipe entering
        /// that structure has been resolved, so confluences with unequal-length
        /// tributary branches sum correctly (a plain BFS does not guarantee this).
        /// </summary>
        private static void RouteNetwork(
            List<NetworkPipeLink> pipes,
            Dictionary<string, double> globalTributary,
            CatchmentFlowRouterResult result)
        {
            var byUpstream = new Dictionary<string, List<NetworkPipeLink>>(StringComparer.OrdinalIgnoreCase);
            var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var inflow = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            void Ensure(string s)
            {
                if (!inDegree.ContainsKey(s)) inDegree[s] = 0;
                if (!inflow.ContainsKey(s)) inflow[s] = 0.0;
            }

            foreach (NetworkPipeLink link in pipes)
            {
                Ensure(link.UpstreamStructureId);
                Ensure(link.DownstreamStructureId);

                if (!byUpstream.TryGetValue(link.UpstreamStructureId, out List<NetworkPipeLink>? list))
                {
                    list = new List<NetworkPipeLink>();
                    byUpstream[link.UpstreamStructureId] = list;
                }
                list.Add(link);
                inDegree[link.DownstreamStructureId]++;
            }

            // Seed catchment (tributary) inflow at its assigned structures.
            foreach (var pair in globalTributary)
                if (inflow.ContainsKey(pair.Key))
                    inflow[pair.Key] += pair.Value;

            // Kahn topological order: a structure is ready once all incoming pipes resolve.
            var ready = new Queue<string>(inDegree
                .Where(kv => kv.Value == 0)
                .Select(kv => kv.Key)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase));

            var visitedPipes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (ready.Count > 0)
            {
                string structId = ready.Dequeue();
                if (!byUpstream.TryGetValue(structId, out List<NetworkPipeLink>? outgoing))
                    continue;

                // inflow[structId] is now fully accumulated (all incoming pipes resolved).
                double structInflow = inflow[structId];

                foreach (NetworkPipeLink link in outgoing.OrderBy(p => p.PipeName, StringComparer.OrdinalIgnoreCase))
                {
                    if (!visitedPipes.Add(link.PipeKey)) continue;

                    result.PipeFlowCfs[link.PipeKey] = structInflow;

                    string ds = link.DownstreamStructureId;
                    inflow[ds] += structInflow;
                    if (--inDegree[ds] == 0)
                        ready.Enqueue(ds);
                }
            }

            // Cycle / unresolved fallback: any pipe a topo sort couldn't reach (a loop)
            // gets its upstream structure's inflow as a best effort.
            foreach (NetworkPipeLink link in pipes)
            {
                if (!result.PipeFlowCfs.ContainsKey(link.PipeKey))
                    result.PipeFlowCfs[link.PipeKey] =
                        inflow.TryGetValue(link.UpstreamStructureId, out double q) ? q : 0.0;
            }

            foreach (var pair in inflow)
            {
                if (!result.StructureInflowCfs.TryGetValue(pair.Key, out double existing))
                    result.StructureInflowCfs[pair.Key] = pair.Value;
                else
                    result.StructureInflowCfs[pair.Key] = Math.Max(existing, pair.Value);
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
    }
}
