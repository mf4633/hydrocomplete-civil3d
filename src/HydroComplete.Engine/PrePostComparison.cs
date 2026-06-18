using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Pre/post-development peak flow comparison across a return-period storm suite.
    /// NRCS TR-55 unit hydrograph with optional detention routing (Modified Puls).
    /// </summary>
    public static class PrePostComparison
    {
        public const double PassToleranceFactor = 1.01;

        public sealed class WatershedInput
        {
            public double AreaAcres { get; set; } = 1.0;
            public double CurveNumber { get; set; } = 70.0;
            public double TcHours { get; set; } = 0.5;
        }

        public sealed class PondConfiguration
        {
            public double MaxStorageFt3 { get; set; } = 50_000.0;
            public double AvgDepthFt { get; set; } = 8.0;
            public IReadOnlyList<StageStorage.ElevationAreaPoint>? ElevAreaTable { get; set; }
            public IReadOnlyList<OutletStructures.OutletDefinition> Outlets { get; set; } =
                Array.Empty<OutletStructures.OutletDefinition>();

            public double RoutingTimestepHours { get; set; } = DetentionRouting.DefaultTimestepHours;
        }

        public sealed class StormPeakDetail
        {
            public double CurveNumber { get; set; }
            public double RunoffDepthIn { get; set; }
            public double PeakFlowCfs { get; set; }
        }

        public sealed class PostStormPeakDetail
        {
            public double CurveNumber { get; set; }
            public double RunoffDepthIn { get; set; }
            public double PeakUnroutedCfs { get; set; }
            public double PeakRoutedCfs { get; set; }
            public double PeakReductionPercent { get; set; }
        }

        public sealed class StormComparisonRow
        {
            public string ReturnPeriod { get; set; } = "";
            public double RainfallIn { get; set; }
            public StormPeakDetail PreDevelopment { get; set; } = new StormPeakDetail();
            public PostStormPeakDetail PostDevelopment { get; set; } = new PostStormPeakDetail();
            public bool Pass { get; set; }
            public double MarginCfs { get; set; }
        }

        public sealed class PrePostComparisonResult : TracedResult
        {
            public bool AllPass { get; set; }
            public List<StormComparisonRow> Rows { get; } = new List<StormComparisonRow>();
        }

        /// <summary>
        /// Compare pre- and post-development peak flows for each storm in the suite.
        /// Post-development passes when routed peak ≤ pre-development peak × 1.01.
        /// </summary>
        public static PrePostComparisonResult Run(
            WatershedInput preDevelopment,
            WatershedInput postDevelopment,
            IReadOnlyDictionary<string, double> storms,
            PondConfiguration? pondConfig = null)
        {
            if (preDevelopment == null) throw new ArgumentNullException(nameof(preDevelopment));
            if (postDevelopment == null) throw new ArgumentNullException(nameof(postDevelopment));
            if (storms == null) throw new ArgumentNullException(nameof(storms));

            var result = new PrePostComparisonResult();
            bool allPass = true;

            foreach (string key in SortStormKeys(storms.Keys))
            {
                if (!storms.TryGetValue(key, out double rainfall) || double.IsNaN(rainfall) || double.IsInfinity(rainfall))
                    continue;

                StormComparisonRow row = EvaluateStorm(
                    key, rainfall, preDevelopment, postDevelopment, pondConfig);
                result.Rows.Add(row);

                if (!row.Pass)
                    allPass = false;
            }

            result.AllPass = allPass;

            int passCount = result.Rows.Count(r => r.Pass);
            result.Steps.Add(new CalcStep("storms", result.Rows.Count, "",
                "return periods evaluated"));
            result.Steps.Add(new CalcStep("pass_count", passCount, "",
                $"{passCount} of {result.Rows.Count} storms pass"));
            result.Steps.Add(new CalcStep("all_pass", allPass ? 1.0 : 0.0, "",
                allPass ? "all storms pass peak control" : "one or more storms fail"));

            return result;
        }

        /// <summary>Peak flow = unit hydrograph peak × SCS runoff depth (in).</summary>
        public static double PeakFlowCfs(
            WatershedInput watershed,
            double rainfallInches)
        {
            double runoffDepth = ScsRunoff.RunoffDepthInches(rainfallInches, watershed.CurveNumber);
            ScsUnitHydrograph.UnitHydrographResult uh = ScsUnitHydrograph.Generate(
                watershed.AreaAcres,
                watershed.TcHours * 60.0);

            return uh.PeakFlowCfs * runoffDepth;
        }

        private static StormComparisonRow EvaluateStorm(
            string returnPeriod,
            double rainfallIn,
            WatershedInput preDev,
            WatershedInput postDev,
            PondConfiguration? pondConfig)
        {
            double preCn = preDev.CurveNumber > 0 ? preDev.CurveNumber : 65.0;
            double postCn = postDev.CurveNumber > 0 ? postDev.CurveNumber : 80.0;

            double preRunoff = ScsRunoff.RunoffDepthInches(rainfallIn, preCn);
            double postRunoff = ScsRunoff.RunoffDepthInches(rainfallIn, postCn);

            ScsUnitHydrograph.UnitHydrographResult preUh = ScsUnitHydrograph.Generate(
                preDev.AreaAcres > 0 ? preDev.AreaAcres : 1.0,
                (preDev.TcHours > 0 ? preDev.TcHours : 0.5) * 60.0);

            ScsUnitHydrograph.UnitHydrographResult postUh = ScsUnitHydrograph.Generate(
                postDev.AreaAcres > 0 ? postDev.AreaAcres : 1.0,
                (postDev.TcHours > 0 ? postDev.TcHours : 0.3) * 60.0);

            double prePeak = preUh.PeakFlowCfs * preRunoff;
            double postPeakUnrouted = postUh.PeakFlowCfs * postRunoff;
            double postPeakRouted = postPeakUnrouted;
            double peakReduction = 0.0;

            if (pondConfig != null)
            {
                try
                {
                    List<DetentionRouting.HydrographPoint> hydrograph =
                        DetentionRouting.InflowFromUnitHydrograph(postUh, postRunoff);

                    if (hydrograph.Count > 2)
                    {
                        List<DetentionRouting.StorageIndicationPoint> curve = BuildPondCurve(pondConfig);
                        DetentionRouting.RoutingResult routing = DetentionRouting.Route(
                            hydrograph,
                            curve,
                            pondConfig.RoutingTimestepHours);

                        postPeakRouted = routing.PeakOutflowCfs;
                        if (postPeakUnrouted > 0.0)
                        {
                            peakReduction = (postPeakUnrouted - postPeakRouted)
                                / postPeakUnrouted * 100.0;
                        }
                    }
                }
                catch
                {
                    // Routing failed — retain unrouted peak (matches JS behavior).
                }
            }

            bool pass = postPeakRouted <= prePeak * PassToleranceFactor;

            return new StormComparisonRow
            {
                ReturnPeriod = returnPeriod,
                RainfallIn = rainfallIn,
                PreDevelopment = new StormPeakDetail
                {
                    CurveNumber = preCn,
                    RunoffDepthIn = preRunoff,
                    PeakFlowCfs = prePeak,
                },
                PostDevelopment = new PostStormPeakDetail
                {
                    CurveNumber = postCn,
                    RunoffDepthIn = postRunoff,
                    PeakUnroutedCfs = postPeakUnrouted,
                    PeakRoutedCfs = postPeakRouted,
                    PeakReductionPercent = peakReduction,
                },
                Pass = pass,
                MarginCfs = prePeak - postPeakRouted,
            };
        }

        private static List<DetentionRouting.StorageIndicationPoint> BuildPondCurve(
            PondConfiguration config)
        {
            if (config.ElevAreaTable != null && config.ElevAreaTable.Count >= 2)
            {
                StageStorage.StageStorageResult stageStorage =
                    StageStorage.BuildFromElevationArea(config.ElevAreaTable);

                return DetentionRouting.BuildStorageIndicationCurve(
                    stageStorage.Points,
                    config.Outlets);
            }

            return DetentionRouting.BuildPrismaticStorageIndicationCurve(
                config.MaxStorageFt3,
                config.Outlets,
                config.AvgDepthFt);
        }

        private static IEnumerable<string> SortStormKeys(IEnumerable<string> keys)
        {
            return keys.OrderBy(ParseReturnPeriodOrder);
        }

        private static int ParseReturnPeriodOrder(string key)
        {
            Match match = Regex.Match(key, @"\d+");
            if (match.Success && int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                return value;

            return 999;
        }
    }
}