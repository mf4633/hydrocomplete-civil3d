using System;
using System.Collections.Generic;
using System.Linq;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Routes synthetic catchment hydrographs through a storm pipe network:
    /// TR-20 convolution per catchment, travel-time lag on reaches, superposition
    /// at junctions, and optional Muskingum-Cunge on long pipes.
    /// Ports HydraflowEngine.combineAtJunction, combineHydrographs, routeMuskingumCunge.
    /// </summary>
    public static class HydrographRouter
    {
        public sealed class RoutedHydrographOrdinate
        {
            public double TimeMinutes { get; set; }
            public double FlowCfs { get; set; }
        }

        public sealed class HydrographRouterOptions
        {
            public double StormDepthIn { get; set; } = 5.0;
            public double TimestepHours { get; set; } = 0.25;
            public HydrographConvolution.UnitHydrographMethod UnitHydroMethod { get; set; } =
                HydrographConvolution.UnitHydrographMethod.Scs;
            public bool ApplyMuskingumCunge { get; set; } = true;
            public double MuskingumCungeMinLengthFt { get; set; } = 1000.0;
            public double DefaultTcMinutes { get; set; } = 10.0;
        }

        public sealed class CatchmentHydrographResult
        {
            public Catchment Catchment { get; set; } = null!;
            public double CurveNumber { get; set; }
            public string? AssignedStructureId { get; set; }
            public HydrographConvolution.ConvolutionResult Hydrograph { get; set; } = null!;
        }

        public sealed class PipeHydrographResult
        {
            public string PipeKey { get; set; } = "";
            public string NetworkName { get; set; } = "";
            public string PipeName { get; set; } = "";
            public double PeakFlowCfs { get; set; }
            public double TimeToPeakMinutes { get; set; }
            public double VolumeAcreFt { get; set; }
            public double TravelTimeMinutes { get; set; }
            public bool MuskingumCungeApplied { get; set; }
            public List<RoutedHydrographOrdinate> Ordinates { get; } = new List<RoutedHydrographOrdinate>();
        }

        public sealed class HydrographRouterResult : TracedResult
        {
            public CatchmentAssignmentMethod AssignmentMethod { get; set; }
            public double TimestepHours { get; set; }
            public List<CatchmentHydrographResult> CatchmentHydrographs { get; } =
                new List<CatchmentHydrographResult>();
            public Dictionary<string, PipeHydrographResult> PipeHydrographs { get; } =
                new Dictionary<string, PipeHydrographResult>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, List<RoutedHydrographOrdinate>> StructureInflowHydrographs { get; } =
                new Dictionary<string, List<RoutedHydrographOrdinate>>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Full routed-hydrograph pipeline: per-catchment TR-20 hydrographs through
        /// the pipe topology in Kahn order (same confluence rules as
        /// <see cref="CatchmentFlowRouter"/>).
        /// </summary>
        public static HydrographRouterResult Route(
            IReadOnlyList<Catchment> catchments,
            IReadOnlyList<NetworkAnalysisPipe> pipes,
            HydrographRouterOptions options,
            IReadOnlyDictionary<string, string>? structureIdToName = null)
        {
            if (catchments == null) throw new ArgumentNullException(nameof(catchments));
            if (pipes == null) throw new ArgumentNullException(nameof(pipes));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (catchments.Count == 0)
                throw new ArgumentException("At least one catchment is required.", nameof(catchments));
            if (options.TimestepHours <= 0)
                throw new ArgumentOutOfRangeException(nameof(options), "Timestep must be positive.");
            if (options.StormDepthIn < 0)
                throw new ArgumentOutOfRangeException(nameof(options), "Storm depth must be >= 0.");

            var normalized = NormalizePipes(pipes);
            var result = new HydrographRouterResult { TimestepHours = options.TimestepHours };
            double dtHours = options.TimestepHours;

            var tributaryByStructure = new Dictionary<string, List<double[]>>(StringComparer.OrdinalIgnoreCase);
            var structureNames = StructureNamesFromPipes(normalized, structureIdToName);

            foreach (Catchment cm in catchments)
            {
                double cn = ScsRunoff.ResolveCurveNumber(cm);
                double tc = cm.TcMinutes > 0 ? cm.TcMinutes : options.DefaultTcMinutes;

                HydrographConvolution.ConvolutionResult hydro =
                    HydrographConvolution.GenerateTr20Hydrograph(
                        cm.AreaAcres,
                        cn,
                        tc,
                        options.StormDepthIn,
                        dtHours,
                        options.UnitHydroMethod);

                var catchmentResult = new CatchmentHydrographResult
                {
                    Catchment = cm,
                    CurveNumber = cn,
                    Hydrograph = hydro,
                };
                result.CatchmentHydrographs.Add(catchmentResult);
            }

            var knownStructures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (NetworkAnalysisPipe link in normalized)
            {
                knownStructures.Add(link.UpstreamNodeId);
                knownStructures.Add(link.DownstreamNodeId);
            }

            int assignedCount = AssignCatchmentsToStructures(
                result.CatchmentHydrographs,
                tributaryByStructure,
                structureNames,
                knownStructures,
                dtHours);

            var unassigned = result.CatchmentHydrographs
                .Where(r => string.IsNullOrEmpty(r.AssignedStructureId))
                .ToList();

            if (unassigned.Count > 0 && normalized.Count > 0)
                AreaWeightUnassignedToHeadwaters(unassigned, tributaryByStructure, normalized, dtHours);

            if (normalized.Count == 0)
            {
                result.AssignmentMethod = CatchmentAssignmentMethod.UniformFallback;
                if (result.CatchmentHydrographs.Count == 1)
                {
                    var only = ToFlowSeries(result.CatchmentHydrographs[0].Hydrograph, dtHours);
                    result.Steps.Add(new CalcStep("pipes", 0, "-", "no topology — catchment hydrograph only"));
                    return result;
                }

                throw new ArgumentException(
                    "Pipe topology is required when multiple catchments are supplied.",
                    nameof(pipes));
            }

            result.AssignmentMethod = assignedCount > 0
                ? CatchmentAssignmentMethod.OutletStructure
                : CatchmentAssignmentMethod.AreaWeightedHeadwater;

            foreach (var group in normalized.GroupBy(p => p.NetworkName, StringComparer.OrdinalIgnoreCase))
                RouteNetwork(group.ToList(), tributaryByStructure, options, result);

            result.Steps.Add(new CalcStep("catchments", catchments.Count, "-", "TR-20 hydrographs generated"));
            result.Steps.Add(new CalcStep("pipes", result.PipeHydrographs.Count, "-", "routed pipe hydrographs"));
            return result;
        }

        /// <summary>Superpose two equal-timestep flow series (HydraflowEngine.combineHydrographs).</summary>
        public static double[] CombineHydrographs(IReadOnlyList<double> a, IReadOnlyList<double> b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));

            int n = Math.Max(a.Count, b.Count);
            var combined = new double[n];
            for (int i = 0; i < n; i++)
            {
                double qa = i < a.Count ? a[i] : 0.0;
                double qb = i < b.Count ? b[i] : 0.0;
                combined[i] = Math.Max(0.0, qa + qb);
            }

            return combined;
        }

        /// <summary>Sum inflow branches at a junction (HydraflowEngine.combineAtJunction).</summary>
        public static double[] CombineAtJunction(IReadOnlyList<IReadOnlyList<double>> branches)
        {
            if (branches == null || branches.Count == 0)
                return Array.Empty<double>();

            double[] combined = Array.Empty<double>();
            foreach (IReadOnlyList<double> branch in branches)
            {
                if (branch == null || branch.Count == 0) continue;
                combined = combined.Length == 0
                    ? branch.ToArray()
                    : CombineHydrographs(combined, branch);
            }

            return combined;
        }

        /// <summary>Shift a hydrograph forward by travel-time lag (hours).</summary>
        public static double[] ShiftLagHydrograph(IReadOnlyList<double> flows, double dtHours, double lagHours)
        {
            if (flows == null) throw new ArgumentNullException(nameof(flows));
            if (dtHours <= 0) throw new ArgumentOutOfRangeException(nameof(dtHours));
            if (lagHours < 0) throw new ArgumentOutOfRangeException(nameof(lagHours));
            if (flows.Count == 0) return Array.Empty<double>();

            int shiftSteps = (int)Math.Round(lagHours / dtHours);
            if (shiftSteps <= 0)
                return flows.Select(q => Math.Max(0.0, q)).ToArray();

            var shifted = new double[flows.Count + shiftSteps];
            for (int i = 0; i < flows.Count; i++)
                shifted[i + shiftSteps] = Math.Max(0.0, flows[i]);
            return shifted;
        }

        /// <summary>
        /// Route inflow through a reach: travel-time lag, or Muskingum-Cunge when
        /// enabled and the pipe exceeds the length threshold.
        /// </summary>
        public static double[] RouteMuskingumCunge(
            IReadOnlyList<double> inflowCfs,
            NetworkAnalysisPipe pipe,
            double dtHours,
            bool applyMuskingumCunge,
            double minLengthFt)
        {
            if (inflowCfs == null) throw new ArgumentNullException(nameof(inflowCfs));
            if (pipe == null) throw new ArgumentNullException(nameof(pipe));
            if (inflowCfs.Count == 0) return Array.Empty<double>();

            double travelMin = PipeTravelTimeMinutes(pipe);
            double lagHours = travelMin / 60.0;

            if (applyMuskingumCunge && pipe.LengthFt >= minLengthFt && pipe.LengthFt > 0)
            {
                var reach = BuildReachParameters(pipe);
                MuskingumCungeRouting.RoutingResult routed =
                    MuskingumCungeRouting.Route(inflowCfs.ToList(), reach, dtHours);
                return routed.Points.Select(p => Math.Max(0.0, p.OutflowCfs)).ToArray();
            }

            return ShiftLagHydrograph(inflowCfs, dtHours, lagHours);
        }

        private static void RouteNetwork(
            List<NetworkAnalysisPipe> pipes,
            Dictionary<string, List<double[]>> globalTributary,
            HydrographRouterOptions options,
            HydrographRouterResult result)
        {
            var byUpstream = new Dictionary<string, List<NetworkAnalysisPipe>>(StringComparer.OrdinalIgnoreCase);
            var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var inflowSeries = new Dictionary<string, List<double[]>>(StringComparer.OrdinalIgnoreCase);

            void Ensure(string s)
            {
                if (!inDegree.ContainsKey(s)) inDegree[s] = 0;
                if (!inflowSeries.ContainsKey(s)) inflowSeries[s] = new List<double[]>();
            }

            foreach (NetworkAnalysisPipe link in pipes)
            {
                Ensure(link.UpstreamNodeId);
                Ensure(link.DownstreamNodeId);

                if (!byUpstream.TryGetValue(link.UpstreamNodeId, out List<NetworkAnalysisPipe>? list))
                {
                    list = new List<NetworkAnalysisPipe>();
                    byUpstream[link.UpstreamNodeId] = list;
                }
                list.Add(link);
                inDegree[link.DownstreamNodeId]++;
            }

            foreach (var pair in globalTributary)
            {
                if (inflowSeries.ContainsKey(pair.Key))
                    inflowSeries[pair.Key].AddRange(pair.Value);
            }

            var ready = new Queue<string>(inDegree
                .Where(kv => kv.Value == 0)
                .Select(kv => kv.Key)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase));

            var visitedPipes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            double dtHours = options.TimestepHours;

            while (ready.Count > 0)
            {
                string structId = ready.Dequeue();
                double[] structInflow = CombineAtJunction(inflowSeries[structId]);
                result.StructureInflowHydrographs[structId] = ToOrdinates(structInflow, dtHours);

                if (!byUpstream.TryGetValue(structId, out List<NetworkAnalysisPipe>? outgoing))
                    continue;

                foreach (NetworkAnalysisPipe link in outgoing.OrderBy(p => p.PipeName, StringComparer.OrdinalIgnoreCase))
                {
                    if (!visitedPipes.Add(link.PipeKey)) continue;

                    bool useMc = options.ApplyMuskingumCunge
                        && link.LengthFt >= options.MuskingumCungeMinLengthFt;
                    double[] pipeOut = RouteMuskingumCunge(
                        structInflow,
                        link,
                        dtHours,
                        options.ApplyMuskingumCunge,
                        options.MuskingumCungeMinLengthFt);

                    result.PipeHydrographs[link.PipeKey] = BuildPipeResult(link, pipeOut, dtHours, useMc);

                    string ds = link.DownstreamNodeId;
                    Ensure(ds);
                    inflowSeries[ds].Add(pipeOut);
                    if (--inDegree[ds] == 0)
                        ready.Enqueue(ds);
                }
            }

            foreach (NetworkAnalysisPipe link in pipes)
            {
                if (result.PipeHydrographs.ContainsKey(link.PipeKey)) continue;

                string us = link.UpstreamNodeId;
                double[] fallback = inflowSeries.TryGetValue(us, out List<double[]>? branches)
                    ? CombineAtJunction(branches)
                    : Array.Empty<double>();

                bool useMc = options.ApplyMuskingumCunge
                    && link.LengthFt >= options.MuskingumCungeMinLengthFt;
                double[] pipeOut = RouteMuskingumCunge(
                    fallback,
                    link,
                    dtHours,
                    options.ApplyMuskingumCunge,
                    options.MuskingumCungeMinLengthFt);
                result.PipeHydrographs[link.PipeKey] = BuildPipeResult(link, pipeOut, dtHours, useMc);
            }
        }

        private static PipeHydrographResult BuildPipeResult(
            NetworkAnalysisPipe link,
            IReadOnlyList<double> flows,
            double dtHours,
            bool muskingumApplied)
        {
            var ordinates = ToOrdinates(flows, dtHours);
            double peak = 0.0;
            double tPeakMin = 0.0;
            foreach (RoutedHydrographOrdinate o in ordinates)
            {
                if (o.FlowCfs > peak)
                {
                    peak = o.FlowCfs;
                    tPeakMin = o.TimeMinutes;
                }
            }

            var result = new PipeHydrographResult
            {
                PipeKey = link.PipeKey,
                NetworkName = link.NetworkName,
                PipeName = link.PipeName,
                PeakFlowCfs = peak,
                TimeToPeakMinutes = tPeakMin,
                VolumeAcreFt = MuskingumRouting.HydrographVolumeAcreFt(flows.ToList(), dtHours),
                TravelTimeMinutes = PipeTravelTimeMinutes(link),
                MuskingumCungeApplied = muskingumApplied,
            };
            result.Ordinates.AddRange(ordinates);
            return result;
        }

        private static List<RoutedHydrographOrdinate> ToOrdinates(IReadOnlyList<double> flows, double dtHours)
        {
            var ordinates = new List<RoutedHydrographOrdinate>();
            if (flows == null || flows.Count == 0)
            {
                ordinates.Add(new RoutedHydrographOrdinate { TimeMinutes = 0.0, FlowCfs = 0.0 });
                return ordinates;
            }

            for (int i = 0; i < flows.Count; i++)
            {
                double q = Math.Max(0.0, flows[i]);
                if (q > 0.001 || i == 0)
                {
                    ordinates.Add(new RoutedHydrographOrdinate
                    {
                        TimeMinutes = i * dtHours * 60.0,
                        FlowCfs = q,
                    });
                }
            }

            if (ordinates.Count == 0)
                ordinates.Add(new RoutedHydrographOrdinate { TimeMinutes = 0.0, FlowCfs = 0.0 });
            return ordinates;
        }

        private static double[] ToFlowSeries(HydrographConvolution.ConvolutionResult hydro, double dtHours)
        {
            if (hydro.Ordinates.Count == 0) return Array.Empty<double>();

            double maxTime = hydro.Ordinates.Max(o => o.TimeHours);
            int steps = (int)Math.Ceiling(maxTime / dtHours) + 1;
            var flows = new double[steps];

            foreach (HydrographConvolution.HydrographOrdinate ord in hydro.Ordinates)
            {
                int idx = (int)Math.Round(ord.TimeHours / dtHours);
                if (idx >= 0 && idx < steps)
                    flows[idx] = Math.Max(flows[idx], ord.FlowCfs);
            }

            return flows;
        }

        private static double PipeTravelTimeMinutes(NetworkAnalysisPipe pipe)
        {
            double velocity = ResolveVelocityFps(pipe);
            if (pipe.LengthFt <= 0 || velocity <= 0) return 0.0;
            return pipe.LengthFt / velocity / 60.0;
        }

        private static double ResolveVelocityFps(NetworkAnalysisPipe pipe)
        {
            double slope = ResolveSlope(pipe);
            PipeSegment segment = pipe.Segment;
            if (segment.DiameterFt > 0 && slope > 0 && segment.ManningN > 0)
            {
                try
                {
                    return Manning.Capacity(segment).FullVelocityFps;
                }
                catch (ArgumentOutOfRangeException)
                {
                    // fall through
                }
            }

            return NetworkTcEstimator.DefaultVelocityFps;
        }

        private static double ResolveSlope(NetworkAnalysisPipe pipe)
        {
            if (pipe.Segment.Slope > 0) return pipe.Segment.Slope;
            if (pipe.LengthFt <= 0) return 0.0;

            double drop = pipe.UpstreamInvertFt - pipe.DownstreamInvertFt;
            return drop > 0 ? drop / pipe.LengthFt : 0.0;
        }

        private static MuskingumCungeRouting.ReachParameters BuildReachParameters(NetworkAnalysisPipe pipe)
        {
            double diameter = pipe.Segment.DiameterFt > 0 ? pipe.Segment.DiameterFt : 2.0;
            double slope = ResolveSlope(pipe);
            if (slope <= 0) slope = 0.001;

            return new MuskingumCungeRouting.ReachParameters
            {
                LengthFt = Math.Max(pipe.LengthFt, 1.0),
                SlopeFtPerFt = slope,
                ManningN = pipe.Segment.ManningN > 0 ? pipe.Segment.ManningN : 0.013,
                BottomWidthFt = diameter,
                SideSlopeZ = 0.0,
                BankfullDepthFt = diameter,
            };
        }

        private static List<NetworkAnalysisPipe> NormalizePipes(IReadOnlyList<NetworkAnalysisPipe> pipes)
        {
            var normalized = new List<NetworkAnalysisPipe>(pipes.Count);
            foreach (NetworkAnalysisPipe p in pipes)
            {
                string us = string.IsNullOrWhiteSpace(p.UpstreamNodeId)
                    ? "__src::" + p.PipeKey
                    : p.UpstreamNodeId;
                string ds = string.IsNullOrWhiteSpace(p.DownstreamNodeId)
                    ? "__out::" + p.PipeKey
                    : p.DownstreamNodeId;

                normalized.Add(new NetworkAnalysisPipe
                {
                    PipeKey = p.PipeKey,
                    NetworkName = p.NetworkName,
                    PipeName = p.PipeName,
                    Link = p.Link,
                    Segment = p.Segment,
                    UpstreamNodeId = us,
                    DownstreamNodeId = ds,
                    LengthFt = p.LengthFt,
                    UpstreamInvertFt = p.UpstreamInvertFt,
                    DownstreamInvertFt = p.DownstreamInvertFt,
                });
            }

            return normalized;
        }

        private static int AssignCatchmentsToStructures(
            IReadOnlyList<CatchmentHydrographResult> catchmentHydrographs,
            Dictionary<string, List<double[]>> tributary,
            Dictionary<string, string> structureNames,
            HashSet<string> knownStructures,
            double dtHours)
        {
            int assignedCount = 0;
            foreach (CatchmentHydrographResult routed in catchmentHydrographs)
            {
                Catchment cm = routed.Catchment;
                string? structId = ResolveStructureId(cm, structureNames, knownStructures);
                if (string.IsNullOrEmpty(structId))
                    continue;

                routed.AssignedStructureId = structId;
                AddTributary(tributary, structId!, ToFlowSeries(routed.Hydrograph, dtHours));
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
            IReadOnlyList<CatchmentHydrographResult> unassigned,
            Dictionary<string, List<double[]>> tributary,
            IReadOnlyList<NetworkAnalysisPipe> pipes,
            double dtHours)
        {
            foreach (var group in pipes.GroupBy(p => p.NetworkName, StringComparer.OrdinalIgnoreCase))
            {
                var headwaters = FindHeadwaterStructures(group.ToList());
                if (headwaters.Count == 0) continue;

                if (headwaters.Count == 1)
                {
                    string hw = headwaters[0];
                    foreach (CatchmentHydrographResult routed in unassigned)
                    {
                        AddTributary(tributary, hw, ToFlowSeries(routed.Hydrograph, dtHours));
                        routed.AssignedStructureId ??= hw;
                    }

                    continue;
                }

                double totalArea = unassigned.Sum(r => r.Catchment.AreaAcres);
                if (totalArea <= 0)
                {
                    foreach (CatchmentHydrographResult routed in unassigned)
                    {
                        double share = 1.0 / headwaters.Count;
                        double[] series = ScaleSeries(ToFlowSeries(routed.Hydrograph, dtHours), share);
                        foreach (string hw in headwaters)
                        {
                            AddTributary(tributary, hw, series);
                            routed.AssignedStructureId ??= hw;
                        }
                    }

                    continue;
                }

                foreach (CatchmentHydrographResult routed in unassigned)
                {
                    double areaShare = routed.Catchment.AreaAcres / totalArea;
                    foreach (string hw in headwaters)
                    {
                        double hwShare = areaShare / headwaters.Count;
                        AddTributary(tributary, hw, ScaleSeries(ToFlowSeries(routed.Hydrograph, dtHours), hwShare));
                        routed.AssignedStructureId ??= hw;
                    }
                }
            }
        }

        private static double[] ScaleSeries(IReadOnlyList<double> flows, double factor)
        {
            return flows.Select(q => Math.Max(0.0, q * factor)).ToArray();
        }

        private static List<string> FindHeadwaterStructures(List<NetworkAnalysisPipe> pipes)
        {
            var upstream = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var downstream = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (NetworkAnalysisPipe link in pipes)
            {
                upstream.Add(link.UpstreamNodeId);
                downstream.Add(link.DownstreamNodeId);
            }

            return upstream
                .Where(id => !downstream.Contains(id))
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static Dictionary<string, string> StructureNamesFromPipes(
            IReadOnlyList<NetworkAnalysisPipe> pipes,
            IReadOnlyDictionary<string, string>? structureIdToName)
        {
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (structureIdToName == null) return names;

            foreach (NetworkAnalysisPipe link in pipes)
            {
                if (structureIdToName.TryGetValue(link.UpstreamNodeId, out string? usName)
                    && !string.IsNullOrWhiteSpace(usName))
                {
                    names[link.UpstreamNodeId] = usName;
                }

                if (structureIdToName.TryGetValue(link.DownstreamNodeId, out string? dsName)
                    && !string.IsNullOrWhiteSpace(dsName))
                {
                    names[link.DownstreamNodeId] = dsName;
                }
            }

            return names;
        }

        private static void AddTributary(
            Dictionary<string, List<double[]>> tributary,
            string structId,
            double[] flowSeries)
        {
            if (!tributary.TryGetValue(structId, out List<double[]>? list))
            {
                list = new List<double[]>();
                tributary[structId] = list;
            }

            list.Add(flowSeries);
        }
    }
}