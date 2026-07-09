using System;
using System.Collections.Generic;
using System.Linq;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Cost-effective BMP selection and treatment-train optimization.
    /// Weiss et al. (2007); EPA (2021); NC DEQ (2020); Int'l BMP Database (2020).
    /// </summary>
    public static class BmpOptimizer
    {
        public const double DefaultDesignLifeYears = 20.0;
        public const double DefaultDiscountRate = 0.05;
        public const double DefaultAvgPondingDepthFt = 2.0;
        public const double LitersPerCf = 28.3168;
        public const double MgPerLb = 453_592.0;

        public sealed class SiteData
        {
            public double AreaAcres { get; set; } = 1.0;
            public double ImperviousPercent { get; set; } = 50.0;
            public double RainfallDepthIn { get; set; } = 1.0;
            public double AnnualRainfallIn { get; set; } = 45.0;
            public double TssConcentrationMgPerL { get; set; } = 80.0;
        }

        public sealed class CostBmpDefinition
        {
            public CostBmpDefinition(
                string key,
                string name,
                double constructionCostPerSf,
                double annualMaintenancePct,
                double landCostPerSf,
                IReadOnlyDictionary<string, double> typicalRemoval,
                double sizingFactor,
                string reference)
            {
                Key = key;
                Name = name;
                ConstructionCostPerSf = constructionCostPerSf;
                AnnualMaintenancePct = annualMaintenancePct;
                LandCostPerSf = landCostPerSf;
                TypicalRemoval = typicalRemoval;
                SizingFactor = sizingFactor;
                Reference = reference;
            }

            public string Key { get; }
            public string Name { get; }
            public double ConstructionCostPerSf { get; }
            public double AnnualMaintenancePct { get; }
            public double LandCostPerSf { get; }
            public IReadOnlyDictionary<string, double> TypicalRemoval { get; }
            public double SizingFactor { get; }
            public string Reference { get; }
        }

        public sealed class WqvSizingResult : TracedResult
        {
            public double WqvCf { get; set; }
            public double WqvAcreFt { get; set; }
            public double RunoffCoefficientRv { get; set; }
        }

        public sealed class LifecycleCostResult : TracedResult
        {
            public double ConstructionCost { get; set; }
            public double LandCost { get; set; }
            public double AnnualMaintenance { get; set; }
            public double MaintenanceNpv { get; set; }
            public double TotalNpv { get; set; }
            public string? Error { get; set; }
        }

        public sealed class BmpSizingInfo
        {
            public double WqvCf { get; set; }
            public double FootprintSf { get; set; }
            public double AvgPondingDepthFt { get; set; }
        }

        public sealed class BmpCostInfo
        {
            public double Construction { get; set; }
            public double Land { get; set; }
            public double MaintenanceNpv { get; set; }
            public double TotalNpv { get; set; }
        }

        public sealed class BmpRankingEntry
        {
            public string BmpType { get; set; } = "";
            public string Name { get; set; } = "";
            public bool MeetsTarget { get; set; }
            public Dictionary<string, double> Removal { get; } = new Dictionary<string, double>();
            public BmpSizingInfo Sizing { get; set; } = new BmpSizingInfo();
            public BmpCostInfo Cost { get; set; } = new BmpCostInfo();
            public double CostPerLb { get; set; }
            public double AnnualTssLoadLbs { get; set; }
            public double TotalTssRemovedLbs { get; set; }
            public int Rank { get; set; }
            public string Reference { get; set; } = "";
        }

        public sealed class BmpSelectionResult : TracedResult
        {
            public List<BmpRankingEntry> Rankings { get; } = new List<BmpRankingEntry>();
            public SiteData SiteData { get; set; } = new SiteData();
            public Dictionary<string, double> TargetRemoval { get; } = new Dictionary<string, double>();
            public double WqvCf { get; set; }
            public double AnnualTssLoadLbs { get; set; }
        }

        public sealed class TreatmentTrainEntry
        {
            public List<string> BmpTypes { get; } = new List<string>();
            public List<string> Names { get; } = new List<string>();
            public Dictionary<string, double> CombinedRemoval { get; } = new Dictionary<string, double>();
            public double TotalCost { get; set; }
            public List<double> ComponentCosts { get; } = new List<double>();
            public int TrainSize { get; set; }
        }

        public sealed class TreatmentTrainResult : TracedResult
        {
            public TreatmentTrainEntry? BestTrain { get; set; }
            public List<TreatmentTrainEntry> AllTrains { get; } = new List<TreatmentTrainEntry>();
            public int TotalEvaluated { get; set; }
        }

        private static readonly Dictionary<string, CostBmpDefinition> DefaultLibrary = CreateDefaultLibrary();

        public static IReadOnlyDictionary<string, CostBmpDefinition> DefaultCostLibrary => DefaultLibrary;

        /// <summary>
        /// Water quality volume: WQV = P × Rv × A × 43560 / 12 (cf);
        /// Rv = 0.05 + 0.009 × I (Schueler 1987).
        /// </summary>
        public static WqvSizingResult CalculateWqv(SiteData siteData)
        {
            if (siteData == null) throw new ArgumentNullException(nameof(siteData));

            double area = siteData.AreaAcres > 0 ? siteData.AreaAcres : 1.0;
            double impervious = siteData.ImperviousPercent;
            double rainfall = siteData.RainfallDepthIn > 0 ? siteData.RainfallDepthIn : 1.0;

            double rv = WaterQualityEngine.RunoffCoefficientFromImpervious(impervious);
            double wqvCf = rainfall * rv * area * BmpLibrary.SqFtPerAcre / BmpLibrary.InchesPerFoot;
            double wqvAcreFt = wqvCf / BmpLibrary.SqFtPerAcre;

            var result = new WqvSizingResult
            {
                WqvCf = wqvCf,
                WqvAcreFt = wqvAcreFt,
                RunoffCoefficientRv = rv,
            };

            result.Steps.Add(new CalcStep("P", rainfall, "in", "water quality storm depth"));
            result.Steps.Add(new CalcStep("I", impervious, "%", "percent impervious"));
            result.Steps.Add(new CalcStep("Rv", rv, "", "0.05 + 0.009*I"));
            result.Steps.Add(new CalcStep("A", area, "ac", "drainage area"));
            result.Steps.Add(new CalcStep("WQV", wqvCf, "cf",
                "P*Rv*A*43560/12"));

            return result;
        }

        /// <summary>
        /// Net present value: NPV = C_const + C_land + M × [(1 − (1+r)^(−T)) / r].
        /// </summary>
        public static LifecycleCostResult LifecycleCost(
            string bmpType,
            double footprintSf,
            double designLifeYears = DefaultDesignLifeYears,
            double discountRate = DefaultDiscountRate,
            IReadOnlyDictionary<string, CostBmpDefinition>? library = null)
        {
            library ??= DefaultLibrary;

            if (!library.TryGetValue(bmpType, out CostBmpDefinition? bmp))
            {
                return new LifecycleCostResult
                {
                    Error = $"Unknown BMP type: {bmpType}",
                };
            }

            double construction = footprintSf * bmp.ConstructionCostPerSf;
            double land = footprintSf * bmp.LandCostPerSf;
            double annualMaintenance = construction * bmp.AnnualMaintenancePct;
            double pwa = PresentWorthAnnuity(discountRate, designLifeYears);
            double maintenanceNpv = annualMaintenance * pwa;
            double totalNpv = construction + land + maintenanceNpv;

            var result = new LifecycleCostResult
            {
                ConstructionCost = construction,
                LandCost = land,
                AnnualMaintenance = annualMaintenance,
                MaintenanceNpv = maintenanceNpv,
                TotalNpv = totalNpv,
            };

            result.Steps.Add(new CalcStep("C_const", construction, "$",
                $"{bmp.ConstructionCostPerSf:0.##}/sf × {footprintSf:0.##} sf"));
            result.Steps.Add(new CalcStep("C_land", land, "$",
                $"{bmp.LandCostPerSf:0.##}/sf × {footprintSf:0.##} sf"));
            result.Steps.Add(new CalcStep("M", annualMaintenance, "$/yr",
                $"{bmp.AnnualMaintenancePct * 100:0.#}% of construction"));
            result.Steps.Add(new CalcStep("r", discountRate, "", "discount rate"));
            result.Steps.Add(new CalcStep("T", designLifeYears, "yr", "design life"));
            result.Steps.Add(new CalcStep("NPV", totalNpv, "$",
                "C_const + C_land + M×PWA"));

            return result;
        }

        /// <summary>
        /// Rank all BMP types by cost-effectiveness ($/lb TSS removed over design life).
        /// </summary>
        public static BmpSelectionResult OptimizeBmpSelection(
            SiteData siteData,
            IReadOnlyDictionary<string, double> targetRemoval,
            IReadOnlyDictionary<string, CostBmpDefinition>? library = null,
            double designLifeYears = DefaultDesignLifeYears,
            double discountRate = DefaultDiscountRate)
        {
            if (siteData == null) throw new ArgumentNullException(nameof(siteData));
            if (targetRemoval == null) throw new ArgumentNullException(nameof(targetRemoval));

            library ??= DefaultLibrary;

            WqvSizingResult wqv = CalculateWqv(siteData);
            double annualTssLoad = EstimateAnnualTssLoadLbs(siteData, wqv.RunoffCoefficientRv);

            var entries = new List<BmpRankingEntry>();

            foreach (KeyValuePair<string, CostBmpDefinition> kv in library)
            {
                CostBmpDefinition bmp = kv.Value;
                bool meetsTarget = MeetsAllTargets(bmp.TypicalRemoval, targetRemoval);

                double baseFootprint = wqv.WqvCf / DefaultAvgPondingDepthFt;
                double footprint = baseFootprint * bmp.SizingFactor;
                LifecycleCostResult lc = LifecycleCost(
                    kv.Key, footprint, designLifeYears, discountRate, library);

                double tssRemoval = bmp.TypicalRemoval.TryGetValue(Pollutant.Tss, out double eta)
                    ? eta
                    : 0.0;
                double totalRemoved = annualTssLoad * tssRemoval * designLifeYears;
                // A zero SizingFactor (e.g. permeable pavement, green roof) drives footprint
                // and thus TotalNpv to 0; a costless facility must not dominate the ranking as
                // "free". Treat non-positive NPV with real removal as infinitely expensive.
                double costPerLb = (totalRemoved > 0 && lc.TotalNpv > 0)
                    ? lc.TotalNpv / totalRemoved
                    : double.PositiveInfinity;

                var entry = new BmpRankingEntry
                {
                    BmpType = kv.Key,
                    Name = bmp.Name,
                    MeetsTarget = meetsTarget,
                    CostPerLb = costPerLb,
                    AnnualTssLoadLbs = annualTssLoad,
                    TotalTssRemovedLbs = totalRemoved,
                    Reference = bmp.Reference,
                    Sizing = new BmpSizingInfo
                    {
                        WqvCf = wqv.WqvCf,
                        FootprintSf = footprint,
                        AvgPondingDepthFt = DefaultAvgPondingDepthFt,
                    },
                    Cost = new BmpCostInfo
                    {
                        Construction = lc.ConstructionCost,
                        Land = lc.LandCost,
                        MaintenanceNpv = lc.MaintenanceNpv,
                        TotalNpv = lc.TotalNpv,
                    },
                };

                foreach (KeyValuePair<string, double> removal in bmp.TypicalRemoval)
                    entry.Removal[removal.Key] = removal.Value;

                entries.Add(entry);
            }

            entries.Sort((a, b) => a.CostPerLb.CompareTo(b.CostPerLb));
            for (int i = 0; i < entries.Count; i++)
                entries[i].Rank = i + 1;

            var result = new BmpSelectionResult
            {
                SiteData = siteData,
                WqvCf = wqv.WqvCf,
                AnnualTssLoadLbs = annualTssLoad,
            };

            foreach (KeyValuePair<string, double> target in targetRemoval)
                result.TargetRemoval[target.Key] = target.Value;

            result.Rankings.AddRange(entries);

            int meeting = entries.Count(e => e.MeetsTarget);
            BmpRankingEntry? best = entries.FirstOrDefault();
            result.Steps.Add(new CalcStep("WQV", wqv.WqvCf, "cf", "water quality volume"));
            result.Steps.Add(new CalcStep("L_TSS", annualTssLoad, "lbs/yr", "Simple Method annual TSS load"));
            result.Steps.Add(new CalcStep("meets_target", meeting, "BMPs",
                $"{meeting} of {entries.Count} BMPs meet all targets"));
            if (best != null)
            {
                result.Steps.Add(new CalcStep("best_cost_per_lb", best.CostPerLb, "$/lb",
                    $"best: {best.Name}"));
            }

            return result;
        }

        /// <summary>
        /// Find minimum-cost 2- or 3-BMP treatment train meeting pollutant targets.
        /// Series removal: η_total = 1 − ∏(1 − η_i).
        /// </summary>
        public static TreatmentTrainResult OptimizeTreatmentTrain(
            SiteData siteData,
            IReadOnlyDictionary<string, double> targetRemoval,
            IReadOnlyDictionary<string, CostBmpDefinition>? library = null,
            double designLifeYears = DefaultDesignLifeYears,
            double discountRate = DefaultDiscountRate)
        {
            if (siteData == null) throw new ArgumentNullException(nameof(siteData));
            if (targetRemoval == null) throw new ArgumentNullException(nameof(targetRemoval));

            library ??= DefaultLibrary;

            WqvSizingResult wqv = CalculateWqv(siteData);
            double baseFootprint = wqv.WqvCf / DefaultAvgPondingDepthFt;
            var bmpTypes = library.Keys.ToList();
            var validTrains = new List<TreatmentTrainEntry>();

            for (int i = 0; i < bmpTypes.Count; i++)
            {
                for (int j = i; j < bmpTypes.Count; j++)
                {
                    TreatmentTrainEntry? train = EvaluateTrain(
                        new[] { bmpTypes[i], bmpTypes[j] },
                        library,
                        targetRemoval,
                        baseFootprint,
                        splitFactor: 0.6,
                        designLifeYears,
                        discountRate);

                    if (train != null)
                        validTrains.Add(train);
                }
            }

            var topSingles = library
                .OrderBy(kv => kv.Value.ConstructionCostPerSf)
                .Take(6)
                .Select(kv => kv.Key)
                .ToList();

            for (int i = 0; i < topSingles.Count; i++)
            {
                for (int j = i; j < topSingles.Count; j++)
                {
                    for (int k = j; k < topSingles.Count; k++)
                    {
                        TreatmentTrainEntry? train = EvaluateTrain(
                            new[] { topSingles[i], topSingles[j], topSingles[k] },
                            library,
                            targetRemoval,
                            baseFootprint,
                            splitFactor: 0.45,
                            designLifeYears,
                            discountRate);

                        if (train != null)
                            validTrains.Add(train);
                    }
                }
            }

            validTrains.Sort((a, b) => a.TotalCost.CompareTo(b.TotalCost));

            var result = new TreatmentTrainResult
            {
                BestTrain = validTrains.FirstOrDefault(),
                TotalEvaluated = validTrains.Count,
            };

            result.AllTrains.AddRange(validTrains.Take(20));

            TreatmentTrainEntry? best = result.BestTrain;
            result.Steps.Add(new CalcStep("trains_evaluated", validTrains.Count, "",
                "valid 2- and 3-BMP combinations"));
            if (best != null)
            {
                result.Steps.Add(new CalcStep("best_train_cost", best.TotalCost, "$",
                    string.Join(" → ", best.Names)));
                if (best.CombinedRemoval.TryGetValue(Pollutant.Tss, out double tssEta))
                {
                    result.Steps.Add(new CalcStep("eta_TSS", tssEta, "",
                        "series combined TSS removal"));
                }
            }
            else
            {
                result.Steps.Add(new CalcStep("best_train_cost", 0, "$",
                    "no valid train found"));
            }

            return result;
        }

        /// <summary>Series pollutant removal: 1 − ∏(1 − η_i).</summary>
        public static double SeriesRemoval(IReadOnlyList<double> efficiencies)
        {
            if (efficiencies == null || efficiencies.Count == 0)
                return 0.0;

            double product = 1.0;
            foreach (double eff in efficiencies)
                product *= 1.0 - eff;

            return 1.0 - product;
        }

        /// <summary>Annual TSS load via Simple Method (Schueler 1987).</summary>
        public static double EstimateAnnualTssLoadLbs(SiteData siteData, double runoffCoefficientRv)
        {
            double annualRain = siteData.AnnualRainfallIn > 0 ? siteData.AnnualRainfallIn : 45.0;
            double area = siteData.AreaAcres > 0 ? siteData.AreaAcres : 1.0;
            double tssConc = siteData.TssConcentrationMgPerL > 0
                ? siteData.TssConcentrationMgPerL
                : 80.0;

            double annualRunoffCf = annualRain * runoffCoefficientRv * area
                * BmpLibrary.SqFtPerAcre / BmpLibrary.InchesPerFoot;
            double annualRunoffL = annualRunoffCf * LitersPerCf;
            return tssConc * annualRunoffL / MgPerLb;
        }

        public static double PresentWorthAnnuity(double discountRate, double years)
        {
            if (years <= 0) return 0.0;
            if (discountRate <= 0) return years;
            return (1.0 - Math.Pow(1.0 + discountRate, -years)) / discountRate;
        }

        private static bool MeetsAllTargets(
            IReadOnlyDictionary<string, double> bmpRemoval,
            IReadOnlyDictionary<string, double> targetRemoval)
        {
            foreach (KeyValuePair<string, double> target in targetRemoval)
            {
                if (!bmpRemoval.TryGetValue(target.Key, out double actual) || actual < target.Value)
                    return false;
            }

            return true;
        }

        private static TreatmentTrainEntry? EvaluateTrain(
            IReadOnlyList<string> bmpKeys,
            IReadOnlyDictionary<string, CostBmpDefinition> library,
            IReadOnlyDictionary<string, double> targetRemoval,
            double baseFootprint,
            double splitFactor,
            double designLifeYears,
            double discountRate)
        {
            var combinedRemoval = new Dictionary<string, double>();
            foreach (KeyValuePair<string, double> target in targetRemoval)
            {
                var efficiencies = bmpKeys
                    .Select(k => library[k].TypicalRemoval.TryGetValue(target.Key, out double e) ? e : 0.0)
                    .ToList();
                combinedRemoval[target.Key] = SeriesRemoval(efficiencies);

                if (combinedRemoval[target.Key] < target.Value)
                    return null;
            }

            var train = new TreatmentTrainEntry { TrainSize = bmpKeys.Count };
            double totalCost = 0.0;

            foreach (string key in bmpKeys)
            {
                CostBmpDefinition bmp = library[key];
                double footprint = baseFootprint * bmp.SizingFactor * splitFactor;
                LifecycleCostResult lc = LifecycleCost(
                    key, footprint, designLifeYears, discountRate, library);

                train.BmpTypes.Add(key);
                train.Names.Add(bmp.Name);
                train.ComponentCosts.Add(lc.TotalNpv);
                totalCost += lc.TotalNpv;
            }

            train.TotalCost = totalCost;
            foreach (KeyValuePair<string, double> kv in combinedRemoval)
                train.CombinedRemoval[kv.Key] = kv.Value;

            return train;
        }

        private static Dictionary<string, CostBmpDefinition> CreateDefaultLibrary()
        {
            return new Dictionary<string, CostBmpDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["bioretention"] = Bmp(
                    "bioretention", "Bioretention Cell", 28.00, 0.05, 5.00, 1.0,
                    Tss(0.85, 0.45, 0.55), "NC DEQ (2020); Hunt & Lord (2006)"),

                ["constructed-wetland"] = Bmp(
                    "constructed-wetland", "Constructed Stormwater Wetland", 12.00, 0.02, 5.00, 2.5,
                    Tss(0.80, 0.35, 0.50), "NC DEQ (2020); Kadlec & Wallace (2009)"),

                ["wet-pond"] = Bmp(
                    "wet-pond", "Wet Detention Pond", 8.50, 0.03, 5.00, 3.0,
                    Tss(0.80, 0.30, 0.45), "NC DEQ (2020); Int'l BMP Database (2020)"),

                ["dry-pond"] = Bmp(
                    "dry-pond", "Dry Extended Detention Basin", 5.50, 0.03, 5.00, 2.0,
                    Tss(0.60, 0.20, 0.20), "EPA (2021); Int'l BMP Database"),

                ["sand-filter"] = Bmp(
                    "sand-filter", "Sand Filter", 35.00, 0.08, 5.00, 0.8,
                    Tss(0.85, 0.35, 0.50), "Barrett (2003); EPA (2021)"),

                ["permeable-pavement"] = Bmp(
                    "permeable-pavement", "Permeable Pavement", 18.00, 0.04, 0.00, 0.0,
                    Tss(0.85, 0.30, 0.60), "UNHSC (2012); Bean et al. (2007)"),

                ["grass-swale"] = Bmp(
                    "grass-swale", "Grassed Swale", 4.00, 0.06, 3.00, 1.5,
                    Tss(0.50, 0.20, 0.20), "Int'l BMP Database (2020); Stagge et al. (2012)"),

                ["green-roof"] = Bmp(
                    "green-roof", "Green Roof", 30.00, 0.02, 0.00, 0.0,
                    Tss(0.80, 0.40, 0.35), "Berndtsson (2010); NC DEQ (2020)"),

                ["infiltration-basin"] = Bmp(
                    "infiltration-basin", "Infiltration Basin", 10.00, 0.06, 5.00, 1.2,
                    Tss(0.90, 0.55, 0.65), "EPA (2021); Guo (2001)"),

                ["level-spreader-filter"] = Bmp(
                    "level-spreader-filter", "Level Spreader — Vegetated Filter", 15.00, 0.04, 4.00, 1.8,
                    Tss(0.70, 0.30, 0.40), "NC DEQ (2020); Winston et al. (2011)"),
            };
        }

        private static CostBmpDefinition Bmp(
            string key,
            string name,
            double construction,
            double maintenancePct,
            double land,
            double sizingFactor,
            Dictionary<string, double> removal,
            string reference)
        {
            return new CostBmpDefinition(
                key, name, construction, maintenancePct, land, removal, sizingFactor, reference);
        }

        private static Dictionary<string, double> Tss(double tss, double tn, double tp) =>
            new Dictionary<string, double>
            {
                [Pollutant.Tss] = tss,
                [Pollutant.Tn] = tn,
                [Pollutant.Tp] = tp,
            };
    }
}