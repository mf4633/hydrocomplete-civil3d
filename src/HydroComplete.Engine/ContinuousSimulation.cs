using System;
using System.Collections.Generic;
using System.Linq;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Daily continuous simulation: Hargreaves ET, soil moisture bucket, moisture-adjusted
    /// SCS runoff, event pollutant loads, and BMP routing with inter-event media recovery.
    /// </summary>
    public static class ContinuousSimulation
    {
        private static readonly int[] DaysPerMonth = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };

        public sealed class SoilMoistureParameters
        {
            public double Porosity { get; set; }
            public double FieldCapacity { get; set; }
            public double WiltingPoint { get; set; }
            public double KsatInPerHr { get; set; }
            public double RootDepthIn { get; set; }
            public double CropCoefficient { get; set; }
        }

        public sealed class SoilMoistureBalanceResult
        {
            public double ThetaNew { get; set; }
            public double RunoffExcessIn { get; set; }
            public double InfiltrationIn { get; set; }
            public double EtActualIn { get; set; }
            public double PercolationIn { get; set; }
        }

        public sealed class HargreavesEtResult
        {
            public double Et0In { get; set; }
            public double Et0Mm { get; set; }
        }

        public sealed class DailyRainfallEvent
        {
            public int DayIndex { get; set; }
            public int Month { get; set; }
            public int DayOfYear { get; set; }
            public double RainfallIn { get; set; }
            public int AntecedentDryDays { get; set; }
        }

        public sealed class LocationClimateStats
        {
            public double LatitudeDeg { get; set; }
            public double[] RainDaysPerMonth { get; set; } = Array.Empty<double>();
            public double[] MeanEventDepthIn { get; set; } = Array.Empty<double>();
            public double[] StdDevDepthIn { get; set; } = Array.Empty<double>();
            public double[] MonthlyTminF { get; set; } = Array.Empty<double>();
            public double[] MonthlyTmaxF { get; set; } = Array.Empty<double>();
            public int MaxDryDays { get; set; } = 30;
            public Dictionary<int, double> AnnualPeaksIn { get; set; } = new Dictionary<int, double>();
        }

        public sealed class SiteData
        {
            public string Location { get; set; } = "charlotte-nc";
            public double AreaAcres { get; set; } = 5.0;
            public double CurveNumber { get; set; } = 75.0;
            public string LandUse { get; set; } = global::HydroComplete.Engine.LandUse.Commercial;
            public double ImperviousPercent { get; set; } = 50.0;
            public int Years { get; set; } = 3;
        }

        public sealed class BmpSimulationConfig
        {
            public string BmpType { get; set; } = global::HydroComplete.Engine.BmpType.Bioretention;
            public double SurfaceAreaSf { get; set; }
            public BioretentionRouting.BioretentionConfig? Bioretention { get; set; }
        }

        public sealed class MonthlyAverageSummary
        {
            public int Month { get; set; }
            public double AvgRainfallIn { get; set; }
            public double AvgRunoffAcreIn { get; set; }
            public double AvgEtIn { get; set; }
            public double AvgTssLbs { get; set; }
            public double AvgTnLbs { get; set; }
        }

        public sealed class ContinuousSimulationResult : TracedResult
        {
            public string Method { get; set; } = "";
            public string Reference { get; set; } = "";
            public int Years { get; set; }
            public string Location { get; set; } = "";
            public string LandUse { get; set; } = "";
            public int EventCount { get; set; }
            public double TotalRainfallIn { get; set; }
            public double TotalRunoffAcreIn { get; set; }
            public double TotalEtIn { get; set; }
            public double AnnualAvgRunoffAcreIn { get; set; }
            public double AnnualAvgEtIn { get; set; }
            public Dictionary<string, double> TotalLoadsLbs { get; } =
                new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, double> TotalTreatedLbs { get; } =
                new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, double>? OverallRemovalPercent { get; set; }
            public List<MonthlyAverageSummary> MonthlyAverage { get; } = new List<MonthlyAverageSummary>();
        }

        public static SoilMoistureParameters GetSoilParamsForLandUse(string landUse)
        {
            switch (landUse?.ToLowerInvariant().Replace(" ", "-"))
            {
                case "residential-low":
                    return new SoilMoistureParameters
                    {
                        Porosity = 0.42, FieldCapacity = 0.28, WiltingPoint = 0.12,
                        KsatInPerHr = 0.6, RootDepthIn = 14, CropCoefficient = 0.85,
                    };
                case LandUse.Commercial:
                    return new SoilMoistureParameters
                    {
                        Porosity = 0.35, FieldCapacity = 0.22, WiltingPoint = 0.10,
                        KsatInPerHr = 0.2, RootDepthIn = 8, CropCoefficient = 0.30,
                    };
                case LandUse.Industrial:
                    return new SoilMoistureParameters
                    {
                        Porosity = 0.34, FieldCapacity = 0.22, WiltingPoint = 0.10,
                        KsatInPerHr = 0.2, RootDepthIn = 6, CropCoefficient = 0.25,
                    };
                case "open-space":
                    return new SoilMoistureParameters
                    {
                        Porosity = 0.45, FieldCapacity = 0.30, WiltingPoint = 0.12,
                        KsatInPerHr = 1.0, RootDepthIn = 18, CropCoefficient = 1.00,
                    };
                case "roadway":
                case "parking":
                    return new SoilMoistureParameters
                    {
                        Porosity = 0.30, FieldCapacity = 0.20, WiltingPoint = 0.10,
                        KsatInPerHr = 0.1, RootDepthIn = 6, CropCoefficient = 0.10,
                    };
                default:
                    return new SoilMoistureParameters
                    {
                        Porosity = 0.40, FieldCapacity = 0.26, WiltingPoint = 0.11,
                        KsatInPerHr = 0.5, RootDepthIn = 12, CropCoefficient = 0.70,
                    };
            }
        }

        public static HargreavesEtResult CalculateHargreavesEt(int dayOfYear, int month, string location)
        {
            if (!ClimateStats.TryGetValue(location, out LocationClimateStats? stats) ||
                stats.MonthlyTminF.Length != 12)
            {
                return new HargreavesEtResult { Et0In = 0.05, Et0Mm = 1.27 };
            }

            double tMin = stats.MonthlyTminF[month];
            double tMax = stats.MonthlyTmaxF[month];
            double tMean = (tMin + tMax) / 2.0;
            double tRange = Math.Max(tMax - tMin, 1.0);
            double latRad = stats.LatitudeDeg * Math.PI / 180.0;

            double dr = 1.0 + 0.033 * Math.Cos(2.0 * Math.PI * dayOfYear / 365.0);
            double delta = 0.409 * Math.Sin(2.0 * Math.PI * dayOfYear / 365.0 - 1.39);
            double ws = Math.Acos(Math.Max(-1.0, Math.Min(1.0, -Math.Tan(latRad) * Math.Tan(delta))));
            double raMj = (24.0 * 60.0 / Math.PI) * 0.0820 * dr *
                (ws * Math.Sin(latRad) * Math.Sin(delta) +
                 Math.Cos(latRad) * Math.Cos(delta) * Math.Sin(ws));
            double raMm = raMj * 0.408;

            double tMeanC = (tMean - 32.0) * 5.0 / 9.0;
            double tRangeC = tRange * 5.0 / 9.0;
            double et0Mm = 0.0023 * (tMeanC + 17.8) * Math.Pow(tRangeC, 0.5) * raMm;
            double et0In = Math.Max(0.0, et0Mm / 25.4);

            return new HargreavesEtResult { Et0In = et0In, Et0Mm = Math.Max(0.0, et0Mm) };
        }

        public static SoilMoistureBalanceResult DailySoilMoistureBalance(
            double thetaPrev,
            double rainfallIn,
            double et0In,
            SoilMoistureParameters soilParams)
        {
            if (soilParams == null) throw new ArgumentNullException(nameof(soilParams));

            double moistureRatio = thetaPrev > soilParams.WiltingPoint
                ? Math.Min(1.0, (thetaPrev - soilParams.WiltingPoint) /
                                (soilParams.FieldCapacity - soilParams.WiltingPoint))
                : 0.0;
            double etActual = et0In * soilParams.CropCoefficient * moistureRatio;
            double deficitIn = Math.Max(0.0, (soilParams.FieldCapacity - thetaPrev) * soilParams.RootDepthIn);
            double maxInfil = deficitIn + soilParams.KsatInPerHr * 24.0;
            double infiltration = Math.Min(rainfallIn, maxInfil);
            double runoffExcess = Math.Max(0.0, rainfallIn - infiltration);

            double thetaAfterRain = thetaPrev + infiltration / soilParams.RootDepthIn;
            double percIn = thetaAfterRain > soilParams.FieldCapacity
                ? (thetaAfterRain - soilParams.FieldCapacity) * soilParams.RootDepthIn * 0.5
                : 0.0;

            double thetaNew = thetaPrev +
                (infiltration / soilParams.RootDepthIn) -
                (etActual / soilParams.RootDepthIn) -
                (percIn / soilParams.RootDepthIn);
            thetaNew = Math.Max(soilParams.WiltingPoint, Math.Min(soilParams.Porosity, thetaNew));

            return new SoilMoistureBalanceResult
            {
                ThetaNew = thetaNew,
                RunoffExcessIn = runoffExcess,
                InfiltrationIn = infiltration,
                EtActualIn = etActual,
                PercolationIn = percIn,
            };
        }

        public static IReadOnlyList<DailyRainfallEvent> GenerateHistoricalEvents(string location, int years)
        {
            if (!ClimateStats.TryGetValue(location, out LocationClimateStats? stats))
                return Array.Empty<DailyRainfallEvent>();

            int totalDays = years * 365;
            var rng = new SeededRandom(location, years);
            var events = new List<DailyRainfallEvent>(totalDays);
            int daysSinceRain = 2;

            for (int day = 0; day < totalDays; day++)
            {
                int dayOfYear = day % 365;
                int month = 0;
                int cumDays = 0;
                for (int m = 0; m < 12; m++)
                {
                    cumDays += DaysPerMonth[m];
                    if (dayOfYear < cumDays)
                    {
                        month = m;
                        break;
                    }
                }

                double pRain = stats.RainDaysPerMonth[month] / DaysPerMonth[month];
                double rainfall = 0.0;
                if (rng.NextDouble() < pRain)
                {
                    double mean = stats.MeanEventDepthIn[month];
                    double stdDev = stats.StdDevDepthIn[month];
                    rainfall = mean + stdDev * rng.NextNormal();
                    rainfall = Math.Max(0.01, rainfall);
                    double maxDepth = stats.AnnualPeaksIn.TryGetValue(100, out double peak) ? peak : 8.0;
                    rainfall = Math.Min(rainfall, maxDepth);
                }

                if (rainfall == 0.0 && daysSinceRain >= stats.MaxDryDays)
                {
                    double mean = stats.MeanEventDepthIn[month];
                    rainfall = Math.Max(0.01, mean * 0.5);
                }

                events.Add(new DailyRainfallEvent
                {
                    DayIndex = day,
                    Month = month,
                    DayOfYear = dayOfYear + 1,
                    RainfallIn = rainfall,
                    AntecedentDryDays = daysSinceRain,
                });

                daysSinceRain = rainfall > 0 ? 0 : daysSinceRain + 1;
            }

            return events;
        }

        public static ContinuousSimulationResult Run(SiteData siteData, BmpSimulationConfig? bmpConfig = null)
        {
            if (siteData == null) throw new ArgumentNullException(nameof(siteData));
            if (!ClimateStats.ContainsKey(siteData.Location))
                throw new ArgumentException($"Unknown climate location: {siteData.Location}", nameof(siteData));

            SoilMoistureParameters soilParams = GetSoilParamsForLandUse(siteData.LandUse);
            IReadOnlyList<DailyRainfallEvent> events = GenerateHistoricalEvents(siteData.Location, siteData.Years);

            double theta = soilParams.FieldCapacity;
            int antecedentDryDays = 3;
            double cn = siteData.CurveNumber;

            BioretentionRouting.BioretentionConfig? bmpBioretention = bmpConfig?.Bioretention ??
                new BioretentionRouting.BioretentionConfig();
            double bmpMediaMoisture = bmpConfig != null ? bmpBioretention.FieldCapacity : 0.0;

            var monthlyTotals = Enumerable.Range(0, 12).Select(_ => new MonthlyAccumulator()).ToArray();
            var totalLoads = InitPollutantTotals();
            var totalTreated = InitPollutantTotals();
            double totalRunoffAcreIn = 0.0;
            double totalRainfall = 0.0;
            double totalEt = 0.0;
            int eventCount = 0;

            foreach (DailyRainfallEvent evt in events)
            {
                HargreavesEtResult et = CalculateHargreavesEt(evt.DayOfYear, evt.Month, siteData.Location);
                SoilMoistureBalanceResult smb = DailySoilMoistureBalance(
                    theta, evt.RainfallIn, et.Et0In, soilParams);
                theta = smb.ThetaNew;

                double moistureAdjust = theta > soilParams.FieldCapacity
                    ? 0.4 * (theta - soilParams.FieldCapacity) /
                      (soilParams.Porosity - soilParams.FieldCapacity)
                    : -0.3 * (soilParams.FieldCapacity - theta) /
                      (soilParams.FieldCapacity - soilParams.WiltingPoint);
                double cnAdj = Math.Max(30.0, Math.Min(98.0, cn * (1.0 + moistureAdjust)));
                double sAdj = 1000.0 / cnAdj - 10.0;
                double iaAdj = 0.2 * sAdj;

                double runoff = 0.0;
                if (evt.RainfallIn > iaAdj)
                {
                    double excess = evt.RainfallIn - iaAdj;
                    runoff = (excess * excess) / (excess + sAdj);
                }

                runoff = Math.Max(runoff, smb.RunoffExcessIn);
                totalRunoffAcreIn += runoff * siteData.AreaAcres;
                totalRainfall += evt.RainfallIn;
                totalEt += et.Et0In;

                MonthlyAccumulator month = monthlyTotals[evt.Month];
                month.RunoffAcreIn += runoff * siteData.AreaAcres;
                month.RainfallIn += evt.RainfallIn;
                month.EtIn += et.Et0In;
                month.Days++;

                if (evt.RainfallIn > 0.01 && runoff > 0)
                {
                    eventCount++;
                    WaterQualityEngine.EventPollutantLoadResult loads =
                        WaterQualityEngine.CalculateEventPollutantLoads(
                            runoff, siteData.AreaAcres, siteData.LandUse, antecedentDryDays);

                    foreach (string pollutant in Pollutant.Core)
                    {
                        double load = loads.LoadsLbs[pollutant];
                        totalLoads[pollutant] += load;
                        month.LoadsLbs[pollutant] += load;
                    }

                    if (bmpConfig != null)
                    {
                        double designVol = runoff * siteData.AreaAcres * BmpLibrary.SqFtPerAcre / BmpLibrary.InchesPerFoot;
                        double surfaceArea = bmpConfig.SurfaceAreaSf > 0
                            ? bmpConfig.SurfaceAreaSf
                            : siteData.AreaAcres * BmpLibrary.SqFtPerAcre * 0.05;

                        if (string.Equals(bmpConfig.BmpType, BmpType.Bioretention, StringComparison.OrdinalIgnoreCase))
                        {
                            bmpBioretention.CurrentMediaMoisture = bmpMediaMoisture;
                            BioretentionRouting.BioretentionRoutingResult routing =
                                BioretentionRouting.Route(bmpBioretention, designVol, surfaceArea);
                            bmpMediaMoisture = routing.PostEventMoisture;

                            foreach (string pollutant in Pollutant.Core)
                            {
                                if (!routing.RemovalEfficiency.TryGetValue(pollutant, out BioretentionRouting.PollutantRemovalEfficiency? eff))
                                    continue;

                                double removed = loads.LoadsLbs[pollutant] * eff.BlendedPercent / 100.0;
                                totalTreated[pollutant] += removed;
                                month.TreatedLbs[pollutant] += removed;
                            }
                        }
                        else if (string.Equals(bmpConfig.BmpType, BmpType.WetPond, StringComparison.OrdinalIgnoreCase))
                        {
                            WetlandRouting.WetPondRoutingResult routing = WetlandRouting.RouteWetPond(
                                new WetlandRouting.WetPondConfig(), designVol, surfaceArea);
                            ApplyWetlandRemoval(loads, routing.RemovalEfficiency, totalTreated, month);
                        }
                        else if (string.Equals(bmpConfig.BmpType, "constructed-wetland", StringComparison.OrdinalIgnoreCase))
                        {
                            WetlandRouting.ConstructedWetlandRoutingResult routing =
                                WetlandRouting.RouteConstructedWetland(
                                    new WetlandRouting.WetlandConfig(), designVol, surfaceArea);
                            ApplyWetlandRemoval(loads, routing.RemovalEfficiency, totalTreated, month);
                        }
                    }

                    antecedentDryDays = 0;
                }
                else
                {
                    antecedentDryDays++;
                    if (bmpConfig != null && bmpMediaMoisture > bmpBioretention.FieldCapacity)
                    {
                        double bmpKsatFtPerDay = (bmpBioretention.KsatInPerHr / BmpLibrary.InchesPerFoot) * 24.0;
                        double bmpMediaDepth = bmpBioretention.MediaDepthFt;
                        double drainage = bmpKsatFtPerDay / bmpMediaDepth;
                        double bmpEt = et.Et0In / (bmpMediaDepth * BmpLibrary.InchesPerFoot) * 0.5;
                        bmpMediaMoisture = Math.Max(
                            bmpBioretention.FieldCapacity,
                            bmpMediaMoisture - drainage - bmpEt);
                    }
                }
            }

            var result = new ContinuousSimulationResult
            {
                Method = "Continuous simulation: Hargreaves ET + daily soil moisture balance + land-use kinetics",
                Reference = "Hargreaves & Samani (1985); Rawls et al. (1983); NSQD (Pitt et al. 2004)",
                Years = siteData.Years,
                Location = siteData.Location,
                LandUse = siteData.LandUse,
                EventCount = eventCount,
                TotalRainfallIn = totalRainfall,
                TotalRunoffAcreIn = totalRunoffAcreIn,
                TotalEtIn = totalEt,
                AnnualAvgRunoffAcreIn = totalRunoffAcreIn / siteData.Years,
                AnnualAvgEtIn = totalEt / siteData.Years,
            };

            foreach (string pollutant in Pollutant.Core)
            {
                result.TotalLoadsLbs[pollutant] = totalLoads[pollutant];
                result.TotalTreatedLbs[pollutant] = totalTreated[pollutant];
            }

            if (bmpConfig != null)
            {
                result.OverallRemovalPercent = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (string pollutant in Pollutant.Core)
                {
                    result.OverallRemovalPercent[pollutant] = totalLoads[pollutant] > 0
                        ? (totalTreated[pollutant] / totalLoads[pollutant]) * 100.0
                        : 0.0;
                }
            }

            for (int i = 0; i < 12; i++)
            {
                MonthlyAccumulator m = monthlyTotals[i];
                result.MonthlyAverage.Add(new MonthlyAverageSummary
                {
                    Month = i,
                    AvgRainfallIn = m.RainfallIn / siteData.Years,
                    AvgRunoffAcreIn = m.RunoffAcreIn / siteData.Years,
                    AvgEtIn = m.EtIn / siteData.Years,
                    AvgTssLbs = m.LoadsLbs[Pollutant.Tss] / siteData.Years,
                    AvgTnLbs = m.LoadsLbs[Pollutant.Tn] / siteData.Years,
                });
            }

            result.Steps.Add(new CalcStep("events", eventCount, "-",
                $"Rainfall days with runoff over {siteData.Years} yr at {siteData.Location}"));
            result.Steps.Add(new CalcStep("Q_annual", result.AnnualAvgRunoffAcreIn, "ac-in",
                "Moisture-adjusted SCS CN + infiltration excess"));
            return result;
        }

        internal static IReadOnlyDictionary<string, LocationClimateStats> ClimateStats => CreateClimateStats();

        private static void ApplyWetlandRemoval(
            WaterQualityEngine.EventPollutantLoadResult loads,
            IReadOnlyDictionary<string, WetlandRouting.PollutantRemovalEfficiency> removal,
            Dictionary<string, double> totalTreated,
            MonthlyAccumulator month)
        {
            foreach (string pollutant in Pollutant.Core)
            {
                if (!removal.TryGetValue(pollutant, out WetlandRouting.PollutantRemovalEfficiency? eff))
                    continue;

                double treated = loads.LoadsLbs[pollutant] * eff.BlendedPercent / 100.0;
                totalTreated[pollutant] += treated;
                month.TreatedLbs[pollutant] += treated;
            }
        }

        private static Dictionary<string, double> InitPollutantTotals()
        {
            return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                [Pollutant.Tss] = 0.0,
                [Pollutant.Tn] = 0.0,
                [Pollutant.Tp] = 0.0,
            };
        }

        private sealed class MonthlyAccumulator
        {
            public double RunoffAcreIn { get; set; }
            public double RainfallIn { get; set; }
            public double EtIn { get; set; }
            public int Days { get; set; }
            public Dictionary<string, double> LoadsLbs { get; } = InitPollutantTotals();
            public Dictionary<string, double> TreatedLbs { get; } = InitPollutantTotals();
        }

        private sealed class SeededRandom
        {
            private int _seed;
            private bool _hasSpare;
            private double _spare;

            public SeededRandom(string location, int years)
            {
                int seed = 0;
                foreach (char ch in location)
                    seed = ((seed << 5) - seed + ch) | 0;
                _seed = Math.Abs(seed * 31 + years * 17 + 12345) % 2147483647;
                if (_seed <= 0) _seed = 1;
            }

            public double NextDouble()
            {
                _seed = (_seed * 16807) % 2147483647;
                return (double)_seed / 2147483647.0;
            }

            public double NextNormal()
            {
                if (_hasSpare)
                {
                    _hasSpare = false;
                    return _spare;
                }

                double u;
                double v;
                double s;
                do
                {
                    u = NextDouble() * 2.0 - 1.0;
                    v = NextDouble() * 2.0 - 1.0;
                    s = u * u + v * v;
                }
                while (s >= 1.0 || s == 0.0);

                double mul = Math.Sqrt(-2.0 * Math.Log(s) / s);
                _spare = v * mul;
                _hasSpare = true;
                return u * mul;
            }
        }

        private static Dictionary<string, LocationClimateStats> CreateClimateStats()
        {
            return new Dictionary<string, LocationClimateStats>(StringComparer.OrdinalIgnoreCase)
            {
                ["charlotte-nc"] = new LocationClimateStats
                {
                    LatitudeDeg = 35.23,
                    RainDaysPerMonth = D(8, 8, 9, 8, 9, 10, 11, 10, 8, 7, 7, 8),
                    MeanEventDepthIn = D(0.50, 0.43, 0.50, 0.41, 0.42, 0.37, 0.37, 0.38, 0.45, 0.47, 0.46, 0.43),
                    StdDevDepthIn = D(0.55, 0.48, 0.58, 0.44, 0.48, 0.42, 0.45, 0.43, 0.52, 0.55, 0.50, 0.48),
                    MonthlyTminF = D(30, 32, 39, 47, 56, 65, 69, 68, 61, 49, 39, 32),
                    MonthlyTmaxF = D(51, 55, 63, 73, 80, 87, 91, 89, 83, 72, 62, 53),
                    MaxDryDays = 21,
                    AnnualPeaksIn = new Dictionary<int, double>
                    {
                        [2] = 2.8, [5] = 3.5, [10] = 4.2, [25] = 5.1, [50] = 5.9, [100] = 6.8,
                    },
                },
                ["raleigh-nc"] = new LocationClimateStats
                {
                    LatitudeDeg = 35.78,
                    RainDaysPerMonth = D(8, 7, 9, 8, 9, 10, 11, 11, 9, 7, 7, 8),
                    MeanEventDepthIn = D(0.44, 0.43, 0.45, 0.37, 0.42, 0.41, 0.43, 0.43, 0.47, 0.48, 0.45, 0.46),
                    StdDevDepthIn = D(0.50, 0.48, 0.52, 0.42, 0.50, 0.46, 0.50, 0.48, 0.55, 0.55, 0.50, 0.52),
                    MonthlyTminF = D(29, 31, 38, 46, 55, 64, 68, 67, 61, 48, 38, 31),
                    MonthlyTmaxF = D(50, 54, 63, 73, 80, 88, 91, 89, 83, 72, 62, 52),
                    MaxDryDays = 20,
                    AnnualPeaksIn = new Dictionary<int, double>
                    {
                        [2] = 3.0, [5] = 3.7, [10] = 4.5, [25] = 5.4, [50] = 6.2, [100] = 7.1,
                    },
                },
                ["atlanta-ga"] = new LocationClimateStats
                {
                    LatitudeDeg = 33.75,
                    RainDaysPerMonth = D(8, 8, 9, 8, 9, 10, 11, 10, 8, 6, 7, 8),
                    MeanEventDepthIn = D(0.48, 0.45, 0.50, 0.42, 0.45, 0.42, 0.45, 0.44, 0.46, 0.48, 0.46, 0.45),
                    StdDevDepthIn = D(0.55, 0.52, 0.58, 0.48, 0.52, 0.48, 0.52, 0.50, 0.54, 0.56, 0.52, 0.50),
                    MonthlyTminF = D(33, 36, 43, 51, 60, 67, 71, 70, 64, 53, 43, 35),
                    MonthlyTmaxF = D(52, 57, 65, 73, 81, 87, 90, 89, 83, 73, 63, 54),
                    MaxDryDays = 22,
                    AnnualPeaksIn = new Dictionary<int, double>
                    {
                        [2] = 3.0, [5] = 3.8, [10] = 4.6, [25] = 5.5, [50] = 6.4, [100] = 7.3,
                    },
                },
            };
        }

        private static double[] D(params double[] values) => values;
    }
}