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

        /// <summary>Returns the slug keys of all cities available for continuous simulation.</summary>
        public static IReadOnlyList<string> AvailableLocations() =>
            new List<string>(CreateClimateStats().Keys);

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
                    AnnualPeaksIn = new Dictionary<int, double> { [2]=3.0,[5]=3.8,[10]=4.6,[25]=5.5,[50]=6.4,[100]=7.3 },
                },
                ["new-york-ny"] = new LocationClimateStats
                {
                    LatitudeDeg = 40.71,
                    RainDaysPerMonth = D(9,8,10,10,10,10,10,9,9,8,9,9),
                    MeanEventDepthIn = D(0.40,0.36,0.41,0.41,0.42,0.44,0.46,0.47,0.46,0.49,0.42,0.43),
                    StdDevDepthIn = D(0.48,0.42,0.50,0.48,0.50,0.52,0.55,0.55,0.54,0.58,0.48,0.50),
                    MonthlyTminF = D(26,28,34,44,53,63,68,67,60,49,39,31),
                    MonthlyTmaxF = D(39,42,50,62,72,81,85,84,77,65,54,43),
                    MaxDryDays = 22,
                    AnnualPeaksIn = new Dictionary<int, double> { [2]=2.7,[5]=3.5,[10]=4.2,[25]=5.2,[50]=6.1,[100]=7.0 },
                },
                ["los-angeles-ca"] = new LocationClimateStats
                {
                    LatitudeDeg = 34.05,
                    RainDaysPerMonth = D(5,5,4,2,1,0,0,0,1,2,3,5),
                    MeanEventDepthIn = D(0.62,0.70,0.59,0.42,0.26,0.06,0.01,0.04,0.21,0.36,0.44,0.50),
                    StdDevDepthIn = D(0.75,0.82,0.70,0.50,0.32,0.08,0.02,0.06,0.28,0.45,0.55,0.60),
                    MonthlyTminF = D(48,49,51,53,57,60,63,64,63,58,52,47),
                    MonthlyTmaxF = D(68,69,70,73,74,78,84,85,83,79,73,68),
                    MaxDryDays = 60,
                    AnnualPeaksIn = new Dictionary<int, double> { [2]=2.0,[5]=2.7,[10]=3.3,[25]=4.3,[50]=5.0,[100]=5.8 },
                },
                ["chicago-il"] = new LocationClimateStats
                {
                    LatitudeDeg = 41.88,
                    RainDaysPerMonth = D(7,7,9,10,10,10,10,9,8,8,8,8),
                    MeanEventDepthIn = D(0.26,0.26,0.28,0.36,0.41,0.39,0.38,0.44,0.41,0.38,0.39,0.37),
                    StdDevDepthIn = D(0.32,0.32,0.35,0.42,0.50,0.48,0.48,0.52,0.48,0.44,0.44,0.42),
                    MonthlyTminF = D(18,21,31,41,51,61,67,66,58,46,34,23),
                    MonthlyTmaxF = D(32,36,47,59,70,80,84,82,75,63,49,36),
                    MaxDryDays = 24,
                    AnnualPeaksIn = new Dictionary<int, double> { [2]=2.2,[5]=2.8,[10]=3.4,[25]=4.2,[50]=4.8,[100]=5.5 },
                },
                ["dallas-tx"] = new LocationClimateStats
                {
                    LatitudeDeg = 32.78,
                    RainDaysPerMonth = D(5,6,7,7,8,7,4,4,6,7,6,5),
                    MeanEventDepthIn = D(0.45,0.47,0.51,0.48,0.61,0.54,0.54,0.54,0.47,0.60,0.48,0.55),
                    StdDevDepthIn = D(0.55,0.58,0.62,0.58,0.75,0.68,0.70,0.68,0.58,0.72,0.58,0.65),
                    MonthlyTminF = D(36,40,48,56,65,73,77,77,69,58,47,38),
                    MonthlyTmaxF = D(57,62,70,78,86,94,98,98,91,80,68,58),
                    MaxDryDays = 35,
                    AnnualPeaksIn = new Dictionary<int, double> { [2]=2.8,[5]=3.7,[10]=4.5,[25]=5.6,[50]=6.6,[100]=7.7 },
                },
                ["houston-tx"] = new LocationClimateStats
                {
                    LatitudeDeg = 29.76,
                    RainDaysPerMonth = D(7,6,7,6,8,9,7,8,8,7,7,7),
                    MeanEventDepthIn = D(0.48,0.47,0.48,0.58,0.65,0.63,0.55,0.59,0.63,0.67,0.55,0.53),
                    StdDevDepthIn = D(0.60,0.58,0.60,0.72,0.82,0.78,0.70,0.75,0.78,0.82,0.68,0.65),
                    MonthlyTminF = D(42,45,52,59,67,73,75,75,70,60,51,44),
                    MonthlyTmaxF = D(63,67,73,80,87,93,95,96,91,83,73,64),
                    MaxDryDays = 30,
                    AnnualPeaksIn = new Dictionary<int, double> { [2]=3.5,[5]=4.6,[10]=5.6,[25]=7.0,[50]=8.2,[100]=9.5 },
                },
                ["washington-dc"] = new LocationClimateStats
                {
                    LatitudeDeg = 38.91,
                    RainDaysPerMonth = D(8,8,9,9,10,10,10,9,8,7,7,8),
                    MeanEventDepthIn = D(0.36,0.33,0.39,0.33,0.40,0.38,0.39,0.37,0.42,0.44,0.42,0.42),
                    StdDevDepthIn = D(0.42,0.38,0.46,0.38,0.48,0.44,0.46,0.44,0.50,0.52,0.48,0.48),
                    MonthlyTminF = D(28,30,37,46,56,65,70,69,62,50,40,32),
                    MonthlyTmaxF = D(44,48,57,68,77,85,89,87,81,69,58,47),
                    MaxDryDays = 22,
                    AnnualPeaksIn = new Dictionary<int, double> { [2]=2.5,[5]=3.2,[10]=3.9,[25]=4.8,[50]=5.5,[100]=6.4 },
                },
                ["miami-fl"] = new LocationClimateStats
                {
                    LatitudeDeg = 25.76,
                    RainDaysPerMonth = D(5,5,6,6,10,14,12,13,14,12,7,5),
                    MeanEventDepthIn = D(0.40,0.43,0.47,0.56,0.59,0.66,0.54,0.61,0.57,0.53,0.51,0.46),
                    StdDevDepthIn = D(0.52,0.55,0.60,0.70,0.75,0.82,0.68,0.78,0.72,0.68,0.62,0.58),
                    MonthlyTminF = D(60,62,65,68,73,76,77,77,76,73,67,62),
                    MonthlyTmaxF = D(76,78,80,83,87,90,91,91,89,86,82,78),
                    MaxDryDays = 30,
                    AnnualPeaksIn = new Dictionary<int, double> { [2]=3.6,[5]=4.8,[10]=5.8,[25]=7.4,[50]=8.7,[100]=10.1 },
                },
                ["philadelphia-pa"] = new LocationClimateStats
                {
                    LatitudeDeg = 39.95,
                    RainDaysPerMonth = D(8,7,9,9,9,9,10,9,8,7,8,9),
                    MeanEventDepthIn = D(0.38,0.38,0.42,0.36,0.40,0.41,0.43,0.42,0.43,0.42,0.39,0.43),
                    StdDevDepthIn = D(0.44,0.44,0.50,0.42,0.48,0.48,0.52,0.50,0.52,0.50,0.46,0.50),
                    MonthlyTminF = D(25,27,34,43,53,62,68,66,59,47,37,29),
                    MonthlyTmaxF = D(40,44,53,64,74,83,87,85,78,66,55,44),
                    MaxDryDays = 22,
                    AnnualPeaksIn = new Dictionary<int, double> { [2]=2.5,[5]=3.2,[10]=3.9,[25]=4.8,[50]=5.6,[100]=6.4 },
                },
                ["phoenix-az"] = new LocationClimateStats
                {
                    LatitudeDeg = 33.45,
                    RainDaysPerMonth = D(3,3,3,1,1,0,3,3,2,2,2,3),
                    MeanEventDepthIn = D(0.22,0.23,0.25,0.28,0.12,0.01,0.33,0.31,0.32,0.33,0.33,0.22),
                    StdDevDepthIn = D(0.30,0.32,0.35,0.35,0.18,0.02,0.42,0.40,0.40,0.42,0.42,0.30),
                    MonthlyTminF = D(44,47,52,58,67,76,83,82,75,63,51,43),
                    MonthlyTmaxF = D(67,71,78,86,95,105,107,105,101,90,77,66),
                    MaxDryDays = 90,
                    AnnualPeaksIn = new Dictionary<int, double> { [2]=1.4,[5]=1.8,[10]=2.2,[25]=2.7,[50]=3.1,[100]=3.6 },
                },
                ["boston-ma"] = new LocationClimateStats
                {
                    LatitudeDeg = 42.36,
                    RainDaysPerMonth = D(9,8,10,10,9,9,8,8,8,9,9,9),
                    MeanEventDepthIn = D(0.38,0.39,0.40,0.37,0.37,0.39,0.42,0.43,0.45,0.43,0.42,0.40),
                    StdDevDepthIn = D(0.44,0.46,0.48,0.42,0.44,0.46,0.50,0.52,0.54,0.52,0.48,0.46),
                    MonthlyTminF = D(22,23,30,40,49,59,65,64,57,46,37,27),
                    MonthlyTmaxF = D(36,39,46,56,67,76,82,80,73,62,51,41),
                    MaxDryDays = 20,
                    AnnualPeaksIn = new Dictionary<int, double> { [2]=2.4,[5]=3.1,[10]=3.7,[25]=4.6,[50]=5.3,[100]=6.1 },
                },
                ["detroit-mi"] = new LocationClimateStats
                {
                    LatitudeDeg = 42.33,
                    RainDaysPerMonth = D(7,7,8,9,9,9,8,8,8,8,8,8),
                    MeanEventDepthIn = D(0.28,0.28,0.30,0.33,0.37,0.37,0.40,0.39,0.38,0.31,0.34,0.25),
                    StdDevDepthIn = D(0.34,0.34,0.36,0.40,0.44,0.44,0.48,0.46,0.46,0.38,0.40,0.32),
                    MonthlyTminF = D(19,20,27,38,48,58,63,62,54,43,33,24),
                    MonthlyTmaxF = D(33,36,46,58,70,80,84,82,74,61,48,37),
                    MaxDryDays = 24,
                    AnnualPeaksIn = new Dictionary<int, double> { [2]=1.9,[5]=2.5,[10]=3.0,[25]=3.7,[50]=4.3,[100]=4.9 },
                },
                ["seattle-wa"] = new LocationClimateStats
                {
                    LatitudeDeg = 47.61,
                    RainDaysPerMonth = D(15,12,13,10,8,7,4,4,6,10,15,16),
                    MeanEventDepthIn = D(0.37,0.32,0.29,0.26,0.25,0.22,0.18,0.22,0.26,0.32,0.38,0.39),
                    StdDevDepthIn = D(0.42,0.36,0.34,0.30,0.30,0.26,0.22,0.28,0.32,0.38,0.44,0.44),
                    MonthlyTminF = D(36,36,38,41,47,52,56,56,52,45,40,36),
                    MonthlyTmaxF = D(47,50,54,59,65,70,76,76,71,60,51,46),
                    MaxDryDays = 35,
                    AnnualPeaksIn = new Dictionary<int, double> { [2]=1.6,[5]=2.1,[10]=2.5,[25]=3.1,[50]=3.5,[100]=4.1 },
                },
                ["minneapolis-mn"] = new LocationClimateStats
                {
                    LatitudeDeg = 44.98,
                    RainDaysPerMonth = D(5,5,7,8,9,10,9,9,8,7,6,5),
                    MeanEventDepthIn = D(0.18,0.16,0.25,0.36,0.38,0.43,0.42,0.47,0.38,0.30,0.30,0.29),
                    StdDevDepthIn = D(0.24,0.22,0.32,0.42,0.46,0.52,0.50,0.56,0.46,0.38,0.36,0.36),
                    MonthlyTminF = D(6,11,24,37,49,59,65,62,53,40,26,13),
                    MonthlyTmaxF = D(24,30,42,57,69,79,84,81,72,58,41,28),
                    MaxDryDays = 28,
                    AnnualPeaksIn = new Dictionary<int, double> { [2]=2.1,[5]=2.7,[10]=3.2,[25]=3.9,[50]=4.6,[100]=5.3 },
                },
                ["denver-co"] = new LocationClimateStats
                {
                    LatitudeDeg = 39.74,
                    RainDaysPerMonth = D(4,4,6,7,8,7,8,7,5,4,4,4),
                    MeanEventDepthIn = D(0.13,0.12,0.22,0.25,0.30,0.27,0.27,0.26,0.26,0.24,0.17,0.15),
                    StdDevDepthIn = D(0.18,0.16,0.28,0.32,0.38,0.34,0.35,0.33,0.32,0.30,0.22,0.20),
                    MonthlyTminF = D(17,20,27,34,44,53,59,57,48,36,25,17),
                    MonthlyTmaxF = D(45,48,56,62,72,83,90,87,79,66,53,44),
                    MaxDryDays = 35,
                    AnnualPeaksIn = new Dictionary<int, double> { [2]=1.3,[5]=1.7,[10]=2.1,[25]=2.6,[50]=3.0,[100]=3.5 },
                },
            };
        }

        private static double[] D(params double[] values) => values;
    }
}