using System;
using System.Collections.Generic;
using System.Linq;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Multi-return-period Rational routing and Manning capacity checks per pipe.
    /// Standard return periods match NOAA Atlas 14 PFDS tables and embedded presets.
    /// </summary>
    public static class MultiRpAnalysis
    {
        public static readonly int[] StandardReturnPeriods = Atlas14Fetcher.StandardReturnPeriods;

        /// <summary>One pipe with topology key and Manning geometry.</summary>
        public sealed class AnalysisPipe
        {
            public string PipeKey { get; set; } = "";

            public string NetworkName { get; set; } = "";

            public string PipeName { get; set; } = "";

            public NetworkPipeLink Link { get; set; } = null!;

            public PipeSegment Segment { get; set; } = null!;
        }

        /// <summary>Peak Q and capacity metrics for one pipe at one return period.</summary>
        public sealed class ReturnPeriodPipeResult
        {
            public int ReturnPeriodYears { get; set; }

            public double PeakFlowCfs { get; set; }

            /// <summary>Design Q divided by full-barrel Manning capacity (Q / Q_full).</summary>
            public double CapacityRatio { get; set; }

            public double QFullCfs { get; set; }

            public double RelativeDepth { get; set; }

            public bool Surcharged { get; set; }
        }

        /// <summary>Per-pipe results across all analyzed return periods.</summary>
        public sealed class PipeMultiRpRow
        {
            public string PipeKey { get; set; } = "";

            public string NetworkName { get; set; } = "";

            public string PipeName { get; set; } = "";

            public Dictionary<int, ReturnPeriodPipeResult> ByReturnPeriod { get; } =
                new Dictionary<int, ReturnPeriodPipeResult>();
        }

        /// <summary>Full multi-RP analysis output.</summary>
        public sealed class MultiRpResult
        {
            public IReadOnlyList<int> ReturnPeriods { get; set; } = Array.Empty<int>();

            public List<PipeMultiRpRow> Pipes { get; } = new List<PipeMultiRpRow>();

            public Dictionary<int, CatchmentFlowRouterResult> RouteByReturnPeriod { get; } =
                new Dictionary<int, CatchmentFlowRouterResult>();
        }

        /// <summary>Build IDF curves for the standard 2/10/25/100-yr periods from an embedded preset.</summary>
        public static Dictionary<int, IdfCurve> CurvesFromPreset(Atlas14Presets.Preset preset)
        {
            if (preset == null) throw new ArgumentNullException(nameof(preset));

            var curves = new Dictionary<int, IdfCurve>(StandardReturnPeriods.Length);
            foreach (int rp in StandardReturnPeriods)
                curves[rp] = preset.ToCurve(rp);
            return curves;
        }

        /// <summary>
        /// Resolve multi-RP IDF curves from an Atlas 14 resolution or the nearest embedded preset.
        /// </summary>
        public static Dictionary<int, IdfCurve> ResolveIdfCurves(Atlas14Resolution? resolution)
        {
            if (resolution?.PresetKey != null)
            {
                Atlas14Presets.Preset? preset = Atlas14Presets.Find(resolution.PresetKey);
                if (preset != null)
                    return CurvesFromPreset(preset);
            }

            Atlas14Presets.Preset? nearest =
                Atlas14Presets.ResolveForDrawing(resolution?.Lat, resolution?.Lon);
            if (nearest != null)
                return CurvesFromPreset(nearest);

            Atlas14Presets.Preset fallback = Atlas14Presets.Find("charlotte-nc")
                ?? throw new InvalidOperationException("No embedded Atlas 14 presets are available.");
            return CurvesFromPreset(fallback);
        }

        /// <summary>
        /// Route catchment flows and evaluate Manning capacity for each pipe and return period.
        /// </summary>
        public static MultiRpResult Analyze(
            IReadOnlyList<Catchment> catchments,
            IReadOnlyList<AnalysisPipe> pipes,
            IReadOnlyDictionary<int, IdfCurve> idfByReturnPeriod,
            IReadOnlyDictionary<string, string>? structureIdToName = null)
        {
            if (catchments == null) throw new ArgumentNullException(nameof(catchments));
            if (pipes == null) throw new ArgumentNullException(nameof(pipes));
            if (idfByReturnPeriod == null) throw new ArgumentNullException(nameof(idfByReturnPeriod));
            if (catchments.Count == 0)
                throw new ArgumentException("At least one catchment is required.", nameof(catchments));
            if (pipes.Count == 0)
                throw new ArgumentException("At least one pipe is required.", nameof(pipes));

            var returnPeriods = idfByReturnPeriod.Keys.OrderBy(rp => rp).ToList();
            if (returnPeriods.Count == 0)
                throw new ArgumentException("At least one IDF return period is required.", nameof(idfByReturnPeriod));

            var links = pipes.Select(p => p.Link).ToList();
            var result = new MultiRpResult { ReturnPeriods = returnPeriods };

            var rowsByKey = new Dictionary<string, PipeMultiRpRow>(StringComparer.OrdinalIgnoreCase);
            foreach (AnalysisPipe pipe in pipes)
            {
                rowsByKey[pipe.PipeKey] = new PipeMultiRpRow
                {
                    PipeKey = pipe.PipeKey,
                    NetworkName = pipe.NetworkName,
                    PipeName = pipe.PipeName,
                };
            }

            foreach (int rp in returnPeriods)
            {
                if (!idfByReturnPeriod.TryGetValue(rp, out IdfCurve? idf))
                    throw new ArgumentException($"Missing IDF curve for {rp}-yr return period.", nameof(idfByReturnPeriod));

                CatchmentFlowRouterResult route = CatchmentFlowRouter.Route(
                    catchments, links, idf, structureIdToName);
                result.RouteByReturnPeriod[rp] = route;

                foreach (AnalysisPipe pipe in pipes)
                {
                    if (!rowsByKey.TryGetValue(pipe.PipeKey, out PipeMultiRpRow? row))
                        continue;

                    if (!route.PipeFlowCfs.TryGetValue(pipe.PipeKey, out double peakQ) || peakQ <= 0)
                        continue;

                    try
                    {
                        Manning.CapacityResult cap = Manning.Capacity(pipe.Segment);
                        Manning.NormalDepthResult nd = Manning.NormalDepth(pipe.Segment, peakQ);
                        row.ByReturnPeriod[rp] = new ReturnPeriodPipeResult
                        {
                            ReturnPeriodYears = rp,
                            PeakFlowCfs = peakQ,
                            QFullCfs = cap.FullFlowCfs,
                            CapacityRatio = cap.FullFlowCfs > 0 ? peakQ / cap.FullFlowCfs : 0.0,
                            RelativeDepth = nd.RelativeDepth,
                            Surcharged = nd.Surcharged,
                        };
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        // Skip pipes with invalid geometry (zero slope/diameter).
                    }
                }
            }

            result.Pipes.AddRange(rowsByKey.Values
                .Where(row => row.ByReturnPeriod.Count > 0)
                .OrderBy(row => row.NetworkName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.PipeName, StringComparer.OrdinalIgnoreCase));

            return result;
        }
    }
}