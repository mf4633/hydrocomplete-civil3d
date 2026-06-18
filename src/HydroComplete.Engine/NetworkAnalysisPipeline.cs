using System;
using System.Collections.Generic;
using System.Linq;

namespace HydroComplete.Engine
{
    /// <summary>One pipe in a network analysis run (topology + Manning geometry).</summary>
    public sealed class NetworkAnalysisPipe
    {
        public string PipeKey { get; set; } = "";

        public string NetworkName { get; set; } = "";

        public string PipeName { get; set; } = "";

        public NetworkPipeLink Link { get; set; } = null!;

        public PipeSegment Segment { get; set; } = null!;

        public string UpstreamNodeId { get; set; } = "";

        public string DownstreamNodeId { get; set; } = "";

        public double LengthFt { get; set; }

        public double UpstreamInvertFt { get; set; }

        public double DownstreamInvertFt { get; set; }
    }

    /// <summary>Engine inputs for a full-network analysis run (no CAD types).</summary>
    public sealed class NetworkAnalysisInput
    {
        public IReadOnlyList<Catchment> Catchments { get; set; } = Array.Empty<Catchment>();

        public IReadOnlyList<NetworkAnalysisPipe> Pipes { get; set; } = Array.Empty<NetworkAnalysisPipe>();

        public string StateCode { get; set; } = "NC";

        public string DevelopmentType { get; set; } = "residential";

        public IdfCurve Idf { get; set; } = null!;

        /// <summary>24-hr design rainfall for SCS runoff depth (inches).</summary>
        public double ScsDesignRainfallInches { get; set; }

        public IReadOnlyDictionary<string, string>? StructureIdToName { get; set; }

        /// <summary>Optional structure rim/invert data for cover and HGL flooding checks.</summary>
        public IReadOnlyDictionary<string, ReviewNodeInput>? ReviewNodes { get; set; }

        public ReviewCriteria? ReviewCriteria { get; set; }

        public HglProfileOptions? HglOptions { get; set; }

        /// <summary>Placeholder BMP chain for treatment-train load reduction (default: bioretention).</summary>
        public IReadOnlyList<string>? PlaceholderBmpChain { get; set; }

        public string LandUse { get; set; } = "commercial";

        public double RusleSlopePercent { get; set; } = 5.0;

        public double RusleSlopeLengthFt { get; set; } = 300.0;
    }

    /// <summary>Rational + SCS runoff for one catchment.</summary>
    public sealed class CatchmentHydrologyResult
    {
        public Catchment Catchment { get; set; } = null!;

        public Rational.PeakFlowResult Rational { get; set; } = null!;

        public ScsRunoff.CatchmentRunoffResult Scs { get; set; } = null!;
    }

    /// <summary>Manning capacity check for one pipe at routed design Q.</summary>
    public sealed class PipeCapacityAnalysisResult
    {
        public NetworkAnalysisPipe Pipe { get; set; } = null!;

        public double DesignFlowCfs { get; set; }

        public Manning.CapacityResult Capacity { get; set; } = null!;

        public Manning.NormalDepthResult NormalDepth { get; set; } = null!;

        public double FlowRatio => Capacity.FullFlowCfs > 0
            ? DesignFlowCfs / Capacity.FullFlowCfs
            : 0.0;

        public bool Surcharged => NormalDepth.Surcharged;
    }

    /// <summary>Steady backwater HGL profile for one pipe network.</summary>
    public sealed class NetworkHglResult
    {
        public string NetworkName { get; set; } = "";

        public double TailwaterFt { get; set; }

        public List<NetworkAnalysisPipe> OrderedPipes { get; } = new List<NetworkAnalysisPipe>();

        public List<NetworkReach> Reaches { get; } = new List<NetworkReach>();

        public List<HglProfilePoint> Profile { get; } = new List<HglProfilePoint>();
    }

    /// <summary>
    /// Full-network analysis output — .NET equivalent of AnalysisController.runFullAnalysis.
    /// </summary>
    public sealed class NetworkAnalysisResult : TracedResult
    {
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        public string StateCode { get; set; } = "";

        public bool OverallPass { get; set; }

        public List<string> Warnings { get; } = new List<string>();

        public List<string> Errors { get; } = new List<string>();

        public List<CatchmentHydrologyResult> Hydrology { get; } = new List<CatchmentHydrologyResult>();

        public CatchmentFlowRouterResult? Routing { get; set; }

        public List<PipeCapacityAnalysisResult> Capacity { get; } = new List<PipeCapacityAnalysisResult>();

        public List<NetworkHglResult> HglNetworks { get; } = new List<NetworkHglResult>();

        public List<SedimentEngine.RusleResult> Sediment { get; } = new List<SedimentEngine.RusleResult>();

        public WaterQualityEngine.WqvResult? Wqv { get; set; }

        public WaterQualityEngine.TreatmentTrainResult? TreatmentTrain { get; set; }

        public ComplianceReport? Compliance { get; set; }

        public List<DesignFinding> DesignReview { get; } = new List<DesignFinding>();
    }

    /// <summary>
    /// Orchestrates hydrology, routing, conveyance, HGL, sediment, water quality,
    /// compliance, and design review on portable engine inputs.
    /// </summary>
    public static class NetworkAnalysisPipeline
    {
        private static readonly string[] DefaultPlaceholderBmpChain = { "bioretention" };

        /// <summary>Run the full analysis pipeline and return aggregated results.</summary>
        public static NetworkAnalysisResult Run(NetworkAnalysisInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (input.Idf == null) throw new ArgumentNullException(nameof(input.Idf));
            if (input.Catchments == null) throw new ArgumentNullException(nameof(input.Catchments));
            if (input.Pipes == null) throw new ArgumentNullException(nameof(input.Pipes));
            if (input.Catchments.Count == 0)
                throw new ArgumentException("At least one catchment is required.", nameof(input));

            StateComplianceConfig state = StateCompliance.Get(input.StateCode);
            var result = new NetworkAnalysisResult
            {
                StateCode = state.Code,
            };

            result.Steps.Add(new CalcStep("state", 0, "", $"{state.Code} — {state.Name}"));
            result.Steps.Add(new CalcStep("catchments", input.Catchments.Count, "", "drainage areas"));
            result.Steps.Add(new CalcStep("pipes", input.Pipes.Count, "", "network links"));

            double scsRainfall = input.ScsDesignRainfallInches > 0
                ? input.ScsDesignRainfallInches
                : state.DesignStormInches;

            // 1. Hydrology — Rational + SCS per catchment
            foreach (Catchment cm in input.Catchments)
            {
                Rational.PeakFlowResult rational = Rational.Peak(cm, input.Idf);
                ScsRunoff.CatchmentRunoffResult scs = ScsRunoff.ComputeCatchment(cm, scsRainfall);
                result.Hydrology.Add(new CatchmentHydrologyResult
                {
                    Catchment = cm,
                    Rational = rational,
                    Scs = scs,
                });
            }

            double totalPeakCfs = result.Hydrology.Sum(h => h.Rational.PeakFlowCfs);
            result.Steps.Add(new CalcStep("Q_total", totalPeakCfs, "cfs", "sum of Rational peaks"));

            // 2. Route catchment flows through pipe topology
            var links = input.Pipes.Select(p => p.Link).ToList();
            result.Routing = CatchmentFlowRouter.Route(
                input.Catchments, links, input.Idf, input.StructureIdToName);

            // 3. Manning capacity per pipe
            foreach (NetworkAnalysisPipe pipe in input.Pipes)
            {
                if (!result.Routing.PipeFlowCfs.TryGetValue(pipe.PipeKey, out double designQ) || designQ <= 0)
                    continue;

                try
                {
                    Manning.CapacityResult cap = Manning.Capacity(pipe.Segment);
                    Manning.NormalDepthResult nd = Manning.NormalDepth(pipe.Segment, designQ);
                    result.Capacity.Add(new PipeCapacityAnalysisResult
                    {
                        Pipe = pipe,
                        DesignFlowCfs = designQ,
                        Capacity = cap,
                        NormalDepth = nd,
                    });
                }
                catch (ArgumentOutOfRangeException)
                {
                    result.Warnings.Add(
                        $"Pipe {pipe.PipeName}: skipped capacity (invalid geometry).");
                }
            }

            int surchargedCount = result.Capacity.Count(c => c.Surcharged);
            if (surchargedCount > 0)
            {
                result.Warnings.Add(
                    $"{surchargedCount} pipe(s) surcharged at routed design Q.");
            }

            // 4. HGL backwater profile per network
            HglProfileOptions hglOptions = input.HglOptions ?? new HglProfileOptions
            {
                IncludeJunctionLosses = true,
                IncludeExitLoss = true,
            };

            foreach (IGrouping<string, NetworkAnalysisPipe> group in input.Pipes
                         .GroupBy(p => p.NetworkName, StringComparer.OrdinalIgnoreCase))
            {
                List<NetworkAnalysisPipe> ordered = OrderPipesDownstream(group.ToList());
                if (ordered.Count == 0) continue;

                var reaches = BuildReaches(ordered, result.Routing.PipeFlowCfs, hglOptions.IncludeJunctionLosses);
                double tailwater = ordered[ordered.Count - 1].DownstreamInvertFt;
                List<HglProfilePoint> profile = Hgl.SteadyBackwaterFromOutfall(reaches, tailwater, hglOptions);

                var netHgl = new NetworkHglResult
                {
                    NetworkName = group.Key,
                    TailwaterFt = tailwater,
                };
                netHgl.OrderedPipes.AddRange(ordered);
                netHgl.Reaches.AddRange(reaches);
                netHgl.Profile.AddRange(profile);
                result.HglNetworks.Add(netHgl);
            }

            // 5. RUSLE sediment per catchment
            foreach (CatchmentHydrologyResult hydro in result.Hydrology)
            {
                SedimentEngine.RusleResult rusle = SedimentEngine.Rusle(
                    hydro.Catchment.AreaAcres,
                    input.RusleSlopePercent,
                    input.RusleSlopeLengthFt,
                    hydro.Catchment.RunoffC,
                    state.DefaultRFactor,
                    name: hydro.Catchment.Name);
                result.Sediment.Add(rusle);
            }

            // 6. WQV + placeholder treatment train
            result.Wqv = WaterQualityEngine.ComputeWqvFromCatchments(
                input.Catchments, state.WqVolumeFactorInches);

            double wqRunoffDepth = state.WqVolumeFactorInches > 0
                ? state.WqVolumeFactorInches * (result.Wqv.RunoffCoefficientRv)
                : 0.0;

            IReadOnlyList<string> bmpChain = input.PlaceholderBmpChain ?? DefaultPlaceholderBmpChain;
            var initialLoads = BuildPlaceholderLoads(input.Catchments, input.LandUse, wqRunoffDepth);
            if (initialLoads.Count > 0)
            {
                result.TreatmentTrain = WaterQualityEngine.ApplyTreatmentTrain(initialLoads, bmpChain);
            }

            // 7. Regulatory compliance
            result.Compliance = ComplianceChecker.CheckCompliance(
                BuildComplianceInput(result, state, input.DevelopmentType),
                state.Code,
                input.DevelopmentType);

            if (result.Compliance != null && !result.Compliance.OverallPass)
                result.Warnings.Add($"Regulatory compliance check FAILED for {state.Code}.");

            // 8. Design review
            var reviewNodes = BuildReviewNodes(input, result);
            var reviewPipes = BuildReviewPipes(result.Capacity);
            result.DesignReview.AddRange(DesignReview.ReviewNetwork(
                reviewPipes, reviewNodes, input.ReviewCriteria));

            int designErrors = result.DesignReview.Count(f => f.Severity == DesignFindingSeverity.Error);
            if (designErrors > 0)
                result.Warnings.Add($"{designErrors} design-criteria error(s) found.");

            result.OverallPass = (result.Compliance?.OverallPass ?? false)
                                 && designErrors == 0
                                 && surchargedCount == 0;

            result.Steps.Add(new CalcStep("overall_pass", result.OverallPass ? 1.0 : 0.0, "-",
                "compliance && no design errors && no surcharge"));

            return result;
        }

        private static ComplianceAnalysisResults BuildComplianceInput(
            NetworkAnalysisResult result,
            StateComplianceConfig state,
            string developmentType)
        {
            var input = new ComplianceAnalysisResults();

            if (result.Wqv != null)
            {
                var wq = new WaterQualityComplianceInput
                {
                    BmpCount = result.TreatmentTrain?.ChainLength ?? 0,
                    WqvRequiredCf = result.Wqv.WqvCf,
                    WqvProvidedCf = 0.0,
                };

                if (result.TreatmentTrain != null)
                {
                    double? tssEta = result.TreatmentTrain.OverallRemovalEfficiency
                        .TryGetValue(Pollutant.Tss, out double tss) ? tss * 100.0 : null;
                    double? tnEta = result.TreatmentTrain.OverallRemovalEfficiency
                        .TryGetValue(Pollutant.Tn, out double tn) ? tn * 100.0 : null;
                    double? tpEta = result.TreatmentTrain.OverallRemovalEfficiency
                        .TryGetValue(Pollutant.Tp, out double tp) ? tp * 100.0 : null;

                    wq.BmpEfficiency.Add(new BmpEfficiencyInput
                    {
                        Type = "bioretention",
                        TssRemovalPercent = tssEta ?? 0.0,
                        TnRemovalPercent = tnEta ?? 0.0,
                        TpRemovalPercent = tpEta ?? 0.0,
                    });
                }

                input.WaterQuality = wq;
            }

            if (result.Sediment.Count > 0)
            {
                input.Sediment = new SedimentComplianceInput
                {
                    TotalSoilLossTonsPerAcYr = SedimentEngine.WeightedAverageSoilLoss(result.Sediment),
                    SedimentControlCount = 0,
                    WatershedResults = result.Sediment.Select(r => new WatershedSedimentInput
                    {
                        Name = r.Name,
                        RiskLevel = r.RiskLevel,
                    }).ToList(),
                };
            }

            double postPeak = result.Routing?.TotalPeakCfs ?? 0.0;
            if (postPeak > 0)
            {
                input.Hydrology = new HydrologyComplianceInput
                {
                    HasDetention = false,
                    PrePeakCfs = postPeak * 0.8,
                    PostPeakCfs = postPeak,
                };
            }

            return input;
        }

        private static Dictionary<string, double> BuildPlaceholderLoads(
            IReadOnlyList<Catchment> catchments,
            string landUse,
            double runoffDepthIn)
        {
            var totals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (string pollutant in Pollutant.Core)
                totals[pollutant] = 0.0;

            if (runoffDepthIn <= 0) return totals;

            foreach (Catchment cm in catchments)
            {
                foreach (string pollutant in Pollutant.Core)
                {
                    WaterQualityEngine.EmcLoadResult load =
                        WaterQualityEngine.CalculateEmcLoad(pollutant, landUse, runoffDepthIn, cm.AreaAcres);
                    totals[pollutant] += load.EmcLoadLbs;
                }
            }

            return totals;
        }

        private static List<ReviewPipeInput> BuildReviewPipes(
            IReadOnlyList<PipeCapacityAnalysisResult> capacity)
        {
            var review = new List<ReviewPipeInput>();
            foreach (PipeCapacityAnalysisResult row in capacity)
            {
                NetworkAnalysisPipe pipe = row.Pipe;
                double slope = pipe.LengthFt > 0
                    ? (pipe.UpstreamInvertFt - pipe.DownstreamInvertFt) / pipe.LengthFt
                    : pipe.Segment.Slope;

                review.Add(new ReviewPipeInput
                {
                    Id = string.IsNullOrEmpty(pipe.NetworkName)
                        ? pipe.PipeName
                        : pipe.NetworkName + "/" + pipe.PipeName,
                    UpstreamNodeId = pipe.UpstreamNodeId,
                    DownstreamNodeId = pipe.DownstreamNodeId,
                    DiameterFt = pipe.Segment.DiameterFt,
                    Slope = slope,
                    DesignFlowCfs = row.DesignFlowCfs,
                    FullCapacityCfs = row.Capacity.FullFlowCfs,
                    VelocityFps = row.NormalDepth.VelocityFps,
                    Surcharged = row.Surcharged,
                    UpstreamInvertFt = pipe.UpstreamInvertFt,
                    DownstreamInvertFt = pipe.DownstreamInvertFt,
                });
            }

            return review;
        }

        private static Dictionary<string, ReviewNodeInput> BuildReviewNodes(
            NetworkAnalysisInput input,
            NetworkAnalysisResult result)
        {
            var nodes = new Dictionary<string, ReviewNodeInput>(StringComparer.OrdinalIgnoreCase);
            if (input.ReviewNodes != null)
            {
                foreach (KeyValuePair<string, ReviewNodeInput> pair in input.ReviewNodes)
                    nodes[pair.Key] = pair.Value;
            }

            foreach (NetworkHglResult net in result.HglNetworks)
            {
                for (int i = 0; i < net.OrderedPipes.Count && i < net.Profile.Count; i++)
                {
                    NetworkAnalysisPipe pipe = net.OrderedPipes[i];
                    HglProfilePoint point = net.Profile[i];
                    BumpNodeHgl(nodes, pipe.UpstreamNodeId, point.HglUpstreamFt);
                    BumpNodeHgl(nodes, pipe.DownstreamNodeId, point.HglFt);
                }
            }

            return nodes;
        }

        private static void BumpNodeHgl(
            Dictionary<string, ReviewNodeInput> nodes,
            string nodeId,
            double hglFt)
        {
            if (string.IsNullOrWhiteSpace(nodeId)) return;

            if (!nodes.TryGetValue(nodeId, out ReviewNodeInput? node))
            {
                node = new ReviewNodeInput { Id = nodeId };
                nodes[nodeId] = node;
            }

            if (!node.HglFt.HasValue || hglFt > node.HglFt.Value)
                node.HglFt = hglFt;
        }

        internal static List<NetworkAnalysisPipe> OrderPipesDownstream(
            IReadOnlyList<NetworkAnalysisPipe> pipes)
        {
            var byUpstream = new Dictionary<string, List<NetworkAnalysisPipe>>(StringComparer.OrdinalIgnoreCase);
            var downstreamStructs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (NetworkAnalysisPipe pipe in pipes)
            {
                downstreamStructs.Add(pipe.DownstreamNodeId);
                if (!byUpstream.TryGetValue(pipe.UpstreamNodeId, out List<NetworkAnalysisPipe>? list))
                {
                    list = new List<NetworkAnalysisPipe>();
                    byUpstream[pipe.UpstreamNodeId] = list;
                }
                list.Add(pipe);
            }

            var headwaters = byUpstream.Keys
                .Where(id => !downstreamStructs.Contains(id))
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var ordered = new List<NetworkAnalysisPipe>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>(headwaters);

            while (queue.Count > 0)
            {
                string structId = queue.Dequeue();
                if (!byUpstream.TryGetValue(structId, out List<NetworkAnalysisPipe>? outgoing))
                    continue;

                foreach (NetworkAnalysisPipe pipe in outgoing
                             .OrderBy(p => p.PipeName, StringComparer.OrdinalIgnoreCase))
                {
                    if (!visited.Add(pipe.PipeKey))
                        continue;

                    ordered.Add(pipe);
                    if (byUpstream.ContainsKey(pipe.DownstreamNodeId))
                        queue.Enqueue(pipe.DownstreamNodeId);
                }
            }

            foreach (NetworkAnalysisPipe pipe in pipes)
            {
                if (!visited.Contains(pipe.PipeKey))
                    ordered.Add(pipe);
            }

            return ordered;
        }

        internal static List<NetworkReach> BuildReaches(
            IReadOnlyList<NetworkAnalysisPipe> orderedPipes,
            IReadOnlyDictionary<string, double> pipeFlowCfs,
            bool includeJunctionLosses)
        {
            var reaches = new List<NetworkReach>(orderedPipes.Count);

            for (int i = 0; i < orderedPipes.Count; i++)
            {
                NetworkAnalysisPipe pipe = orderedPipes[i];
                double designQ = pipeFlowCfs.TryGetValue(pipe.PipeKey, out double q) ? q : 0.0;
                NetworkReach reach = ReachFactory.FromNormalDepth(
                    pipe.Segment, designQ, pipe.LengthFt, pipe.PipeName);

                if (includeJunctionLosses && i < orderedPipes.Count - 1)
                {
                    NetworkAnalysisPipe next = orderedPipes[i + 1];
                    if (string.Equals(
                            pipe.DownstreamNodeId,
                            next.UpstreamNodeId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        reach.JunctionLossK = Hec22.DefaultManholeK;
                    }
                }

                reaches.Add(reach);
            }

            return reaches;
        }
    }
}