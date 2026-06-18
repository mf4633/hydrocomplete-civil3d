using System;
using System.Collections.Generic;
using System.Linq;

namespace HydroComplete.Engine
{
    /// <summary>BMP catalog and sediment-control structure sizing (basin, trap, fence, check dam, treatment train).</summary>
    internal static class SedimentBmpCatalog
    {
        internal sealed class TrappingEfficiencySet
        {
            public double Clay { get; set; }
            public double Silt { get; set; }
            public double Sand { get; set; }
            public double Overall { get; set; }
        }

        internal sealed class PollutantRemovalSet
        {
            public double Tss { get; set; }
            public double Tn { get; set; }
            public double Tp { get; set; }
            public double Bacteria { get; set; }
            public double Metals { get; set; }
        }

        internal sealed class BmpDefinition
        {
            public string Key { get; set; } = "";
            public string Name { get; set; } = "";
            public TrappingEfficiencySet TrappingEfficiency { get; set; } = new TrappingEfficiencySet();
            public PollutantRemovalSet PollutantRemoval { get; set; } = new PollutantRemovalSet();
            public string PollutantRemovalReference { get; set; } = "";
        }

        private static readonly Dictionary<string, BmpDefinition> Catalog =
            new Dictionary<string, BmpDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["sediment-basin"] = new BmpDefinition
                {
                    Key = "sediment-basin",
                    Name = "Sediment Basin (Trap)",
                    TrappingEfficiency = new TrappingEfficiencySet { Clay = 0.20, Silt = 0.65, Sand = 0.95, Overall = 0.70 },
                    PollutantRemoval = new PollutantRemovalSet { Tss = 0.70, Tn = 0.20, Tp = 0.25, Bacteria = 0.50, Metals = 0.40 },
                    PollutantRemovalReference = "Int'l BMP Database (2020); Barrett et al. (2004); NCDEQ",
                },
                ["sediment-trap"] = new BmpDefinition
                {
                    Key = "sediment-trap",
                    Name = "Sediment Trap (Small)",
                    TrappingEfficiency = new TrappingEfficiencySet { Clay = 0.10, Silt = 0.50, Sand = 0.90, Overall = 0.60 },
                    PollutantRemoval = new PollutantRemovalSet { Tss = 0.60, Tn = 0.15, Tp = 0.20, Bacteria = 0.40, Metals = 0.30 },
                    PollutantRemovalReference = "Int'l BMP Database (2020); NCDEQ",
                },
                ["silt-fence"] = new BmpDefinition
                {
                    Key = "silt-fence",
                    Name = "Silt Fence",
                    TrappingEfficiency = new TrappingEfficiencySet { Clay = 0.05, Silt = 0.45, Sand = 0.85, Overall = 0.50 },
                    PollutantRemoval = new PollutantRemovalSet { Tss = 0.50, Tn = 0.10, Tp = 0.15, Bacteria = 0.20, Metals = 0.25 },
                    PollutantRemovalReference = "Barrett et al. (1998); USEPA Fact Sheet",
                },
                ["rock-check-dam"] = new BmpDefinition
                {
                    Key = "rock-check-dam",
                    Name = "Rock Check Dam",
                    TrappingEfficiency = new TrappingEfficiencySet { Clay = 0.15, Silt = 0.40, Sand = 0.75, Overall = 0.45 },
                    PollutantRemoval = new PollutantRemovalSet { Tss = 0.45, Tn = 0.10, Tp = 0.10, Bacteria = 0.15, Metals = 0.20 },
                    PollutantRemovalReference = "USEPA National Menu of BMPs; NCDEQ",
                },
            };

        internal static bool TryGet(string key, out BmpDefinition definition) =>
            Catalog.TryGetValue(key ?? "", out definition!);

        internal static BmpDefinition GetRequired(string key)
        {
            if (!TryGet(key, out BmpDefinition? def))
                throw new ArgumentException($"Unknown BMP type '{key}'.", nameof(key));
            return def;
        }

        internal static double PollutantEfficiency(BmpDefinition bmp, string pollutant)
        {
            switch (pollutant.ToUpperInvariant())
            {
                case "TSS": return bmp.PollutantRemoval.Tss > 0 ? bmp.PollutantRemoval.Tss : bmp.TrappingEfficiency.Overall;
                case "TN": return bmp.PollutantRemoval.Tn;
                case "TP": return bmp.PollutantRemoval.Tp;
                case "BACTERIA": return bmp.PollutantRemoval.Bacteria;
                case "METALS": return bmp.PollutantRemoval.Metals;
                default: return 0.0;
            }
        }
    }

    /// <summary>Sediment basin design (NCDEQ surface-area method).</summary>
    public static class SedimentBasin
    {
        public const double SurfaceAreaRatioSfPerCfs = 435.0;
        public const double MinimumDepthFt = 3.0;
        public const double LengthWidthRatio = 2.0;
        public const double DewateringHours = 48.0;
        public const double DefaultForebayFraction = 0.15;

        public sealed class DesignOptions
        {
            public SedimentSettling.SitePsdInput? Psd { get; set; }
            public double BulkDensityLbPerCf { get; set; } = 80.0;
            public double ForebayFraction { get; set; } = DefaultForebayFraction;
            public double ForebayDepthFt { get; set; } = 4.0;
            public double WaterTempC { get; set; } = 20.0;
            public double SpecificGravity { get; set; } = 2.65;
        }

        public sealed class DesignResult : TracedResult
        {
            public double SurfaceAreaSf { get; set; }
            public double LengthFt { get; set; }
            public double WidthFt { get; set; }
            public double DepthFt { get; set; }
            public double PoolVolumeCf { get; set; }
            public double SedimentStorageCf { get; set; }
            public double TotalVolumeCf { get; set; }
            public double TrappingEfficiencyPct { get; set; }
            public string TrapEfficiencyMethod { get; set; } = "";
            public double DewateringTimeHr { get; set; }
            public double ForebayVolumeCf { get; set; }
            public double ForebayLengthFt { get; set; }
            public double ForebayWidthFt { get; set; }
            public SedimentSettling.CampEfficiencyResult? Camp { get; set; }
        }

        /// <summary>
        /// Size a sediment basin: surface area = Q × 435 sf/cfs, pool volume, sediment storage, forebay.
        /// </summary>
        public static DesignResult Design(
            double designFlowCfs,
            double drainageAreaAc,
            double sedimentYieldTonsPerAcreYr,
            DesignOptions? options = null)
        {
            if (designFlowCfs < 0) throw new ArgumentOutOfRangeException(nameof(designFlowCfs));
            if (drainageAreaAc < 0) throw new ArgumentOutOfRangeException(nameof(drainageAreaAc));
            if (sedimentYieldTonsPerAcreYr < 0) throw new ArgumentOutOfRangeException(nameof(sedimentYieldTonsPerAcreYr));

            options ??= new DesignOptions();

            double surfaceArea = designFlowCfs * SurfaceAreaRatioSfPerCfs;
            double depth = MinimumDepthFt;
            double volume = surfaceArea * depth;
            double width = Math.Sqrt(surfaceArea / LengthWidthRatio);
            double length = width * LengthWidthRatio;

            double sedimentStorage = sedimentYieldTonsPerAcreYr * drainageAreaAc * 2000.0
                / options.BulkDensityLbPerCf * 0.5;
            double totalVolume = volume + sedimentStorage;

            double trappingPct;
            string method;
            SedimentSettling.CampEfficiencyResult? camp = null;

            if (options.Psd != null)
            {
                var psd7 = SedimentSettling.BuildSevenBinPsd(options.Psd);
                camp = SedimentSettling.CampEfficiency(
                    designFlowCfs, surfaceArea, psd7, options.WaterTempC, options.SpecificGravity);
                trappingPct = camp.OverallEfficiencyPct;
                method = "Camp (1946) with Stokes/Rubey";
            }
            else
            {
                trappingPct = SedimentBmpCatalog.GetRequired("sediment-basin").TrappingEfficiency.Overall * 100.0;
                method = "default";
            }

            double forebayVolume = volume * options.ForebayFraction;
            double forebaySurface = forebayVolume / options.ForebayDepthFt;
            double forebayWidth = Math.Sqrt(forebaySurface / LengthWidthRatio);
            double forebayLength = forebayWidth * LengthWidthRatio;

            var result = new DesignResult
            {
                SurfaceAreaSf = surfaceArea,
                LengthFt = length,
                WidthFt = width,
                DepthFt = depth,
                PoolVolumeCf = volume,
                SedimentStorageCf = sedimentStorage,
                TotalVolumeCf = totalVolume,
                TrappingEfficiencyPct = trappingPct,
                TrapEfficiencyMethod = method,
                DewateringTimeHr = DewateringHours,
                ForebayVolumeCf = forebayVolume,
                ForebayLengthFt = forebayLength,
                ForebayWidthFt = forebayWidth,
                Camp = camp,
            };

            result.Steps.Add(new CalcStep("As", surfaceArea, "ft^2",
                $"Q*435 = {designFlowCfs:0.####}*{SurfaceAreaRatioSfPerCfs:0.###}"));
            result.Steps.Add(new CalcStep("V_pool", volume, "ft^3", $"As*d = {surfaceArea:0.####}*{depth:0.###}"));
            result.Steps.Add(new CalcStep("V_sed", sedimentStorage, "ft^3",
                $"Y*A*2000/gamma_bulk*0.5 = {sedimentYieldTonsPerAcreYr:0.##}*{drainageAreaAc:0.###}*2000/{options.BulkDensityLbPerCf:0.###}*0.5"));
            result.Steps.Add(new CalcStep("V_forebay", forebayVolume, "ft^3",
                $"V_pool*f_forebay = {volume:0.####}*{options.ForebayFraction:0.##}"));
            result.Steps.Add(new CalcStep("t_dewater", DewateringHours, "hr", "NCDEQ max dewatering time"));

            if (camp != null)
            {
                foreach (CalcStep step in camp.Steps)
                    result.Steps.Add(step);
            }
            else
            {
                result.Steps.Add(new CalcStep("eta", trappingPct, "%", "Structure default trapping efficiency"));
            }

            return result;
        }
    }

    /// <summary>Small sediment trap for drainage areas under 5 acres (NCDEQ 3600 cf/ac).</summary>
    public static class SedimentTrap
    {
        public const double StorageVolumeCfPerAc = 3600.0;
        public const double MinimumDepthFt = 2.0;
        public const double LengthWidthRatio = 2.0;
        public const double MaxDrainageAreaAc = 5.0;

        public static readonly IReadOnlyDictionary<string, SedimentSettling.ThreeBinPsd> DefaultPsdBySoil =
            new Dictionary<string, SedimentSettling.ThreeBinPsd>(StringComparer.OrdinalIgnoreCase)
            {
                ["sand"] = new SedimentSettling.ThreeBinPsd { Clay = 0.05, Silt = 0.15, Sand = 0.80 },
                ["loam"] = new SedimentSettling.ThreeBinPsd { Clay = 0.20, Silt = 0.40, Sand = 0.40 },
                ["clay-loam"] = new SedimentSettling.ThreeBinPsd { Clay = 0.35, Silt = 0.35, Sand = 0.30 },
                ["construction-site"] = new SedimentSettling.ThreeBinPsd { Clay = 0.10, Silt = 0.30, Sand = 0.60 },
            };

        public sealed class DesignResult : TracedResult
        {
            public double DrainageAreaAc { get; set; }
            public double VolumeCf { get; set; }
            public double SurfaceAreaSf { get; set; }
            public double LengthFt { get; set; }
            public double WidthFt { get; set; }
            public double DepthFt { get; set; }
            public int RiserDiameterIn { get; set; }
            public double TrappingEfficiencyPct { get; set; }
            public bool Appropriate { get; set; }
        }

        public static DesignResult Design(double drainageAreaAc, string soilType = "loam", SedimentSettling.ThreeBinPsd? psd = null)
        {
            if (drainageAreaAc < 0) throw new ArgumentOutOfRangeException(nameof(drainageAreaAc));

            double area = Math.Min(drainageAreaAc, MaxDrainageAreaAc);
            double volume = area * StorageVolumeCfPerAc;
            double depth = MinimumDepthFt;
            double surfaceArea = volume / depth;
            double width = Math.Sqrt(surfaceArea / LengthWidthRatio);
            double length = width * LengthWidthRatio;

            var psdUsed = psd ?? (DefaultPsdBySoil.TryGetValue(soilType, out SedimentSettling.ThreeBinPsd? lookup)
                ? lookup
                : DefaultPsdBySoil["loam"]);
            var trapEff = SedimentSettling.WeightedTrapEfficiency("sediment-trap", psdUsed);

            int riserDiameter = area <= 1.0 ? 12 : area <= 3.0 ? 18 : 24;

            var result = new DesignResult
            {
                DrainageAreaAc = area,
                VolumeCf = volume,
                SurfaceAreaSf = surfaceArea,
                LengthFt = length,
                WidthFt = width,
                DepthFt = depth,
                RiserDiameterIn = riserDiameter,
                TrappingEfficiencyPct = trapEff.OverallEfficiencyPct,
                Appropriate = drainageAreaAc <= MaxDrainageAreaAc,
            };

            result.Steps.Add(new CalcStep("V", volume, "ft^3",
                $"A_drain*3600 = {area:0.####}*{StorageVolumeCfPerAc:0.###}"));
            result.Steps.Add(new CalcStep("As", surfaceArea, "ft^2", $"V/d = {volume:0.####}/{depth:0.###}"));
            foreach (CalcStep step in trapEff.Steps)
                result.Steps.Add(step);

            return result;
        }
    }

    /// <summary>Silt fence sizing for sheet-flow filtration (NCDEQ / USEPA).</summary>
    public static class SiltFence
    {
        public const double MaxSlopeLengthFt = 100.0;
        public const double MaxSlopeFraction = 0.50;
        public const double MaxDrainageAreaPer100LfAc = 0.25;
        public const double PostSpacingFt = 6.0;
        public const double FenceHeightFt = 3.0;

        public sealed class DesignResult : TracedResult
        {
            public bool Feasible { get; set; }
            public string? InfeasibleReason { get; set; }
            public double FenceLengthLf { get; set; }
            public int PostCount { get; set; }
            public double TssRemovalPct { get; set; }
        }

        public static DesignResult Design(double slopeLengthFt, double slopePercent, double drainageAreaAc)
        {
            if (slopeLengthFt < 0) throw new ArgumentOutOfRangeException(nameof(slopeLengthFt));
            if (slopePercent < 0) throw new ArgumentOutOfRangeException(nameof(slopePercent));
            if (drainageAreaAc < 0) throw new ArgumentOutOfRangeException(nameof(drainageAreaAc));

            if (slopeLengthFt > MaxSlopeLengthFt)
            {
                return new DesignResult
                {
                    Feasible = false,
                    InfeasibleReason = $"Slope length {slopeLengthFt:0.###} ft exceeds maximum {MaxSlopeLengthFt:0.###} ft",
                };
            }

            if (slopePercent / 100.0 > MaxSlopeFraction)
            {
                return new DesignResult
                {
                    Feasible = false,
                    InfeasibleReason = $"Slope {slopePercent:0.###}% exceeds maximum {MaxSlopeFraction * 100:0.###}%",
                };
            }

            double maxDaPerLf = MaxDrainageAreaPer100LfAc / 100.0;
            double fenceLength = drainageAreaAc / maxDaPerLf;
            int postCount = (int)Math.Ceiling(fenceLength / PostSpacingFt) + 1;
            double tssRemoval = SedimentBmpCatalog.GetRequired("silt-fence").TrappingEfficiency.Overall * 100.0;

            var result = new DesignResult
            {
                Feasible = true,
                FenceLengthLf = fenceLength,
                PostCount = postCount,
                TssRemovalPct = tssRemoval,
            };

            result.Steps.Add(new CalcStep("L_fence", fenceLength, "LF",
                $"A_drain/0.25ac*100 = {drainageAreaAc:0.####}/0.25*100"));
            result.Steps.Add(new CalcStep("N_posts", postCount, "",
                $"ceil(L_fence/s)+1 = ceil({fenceLength:0.####}/{PostSpacingFt:0.###})+1"));
            result.Steps.Add(new CalcStep("E_TSS", tssRemoval, "%", "Barrett et al. (1998) sheet-flow filtration"));

            return result;
        }
    }

    /// <summary>Rock check dam spacing and weir capacity (NCDEQ / VDOT).</summary>
    public static class CheckDam
    {
        public const double MaxHeightFt = 2.0;
        public const double WeirCoefficient = 2.65;

        public sealed class DesignResult : TracedResult
        {
            public int NumberOfDams { get; set; }
            public double SpacingFt { get; set; }
            public double HeightFt { get; set; }
            public double NotchWidthFt { get; set; }
            public double NotchDepthFt { get; set; }
            public double WeirCapacityCfs { get; set; }
            public double TotalRockVolumeCf { get; set; }
            public double TotalRockTons { get; set; }
            public double TssRemovalPct { get; set; }
        }

        public static DesignResult Design(double channelLengthFt, double slopePercent, double channelWidthFt = 6.0)
        {
            if (channelLengthFt < 0) throw new ArgumentOutOfRangeException(nameof(channelLengthFt));
            if (slopePercent <= 0) throw new ArgumentOutOfRangeException(nameof(slopePercent));
            if (channelWidthFt <= 0) throw new ArgumentOutOfRangeException(nameof(channelWidthFt));

            double height = MaxHeightFt;
            double spacing = height * 100.0 / slopePercent;
            int numberOfDams = (int)Math.Ceiling(channelLengthFt / spacing);

            double notchWidth = channelWidthFt / 3.0;
            double notchDepth = height / 3.0;
            double weirCapacity = WeirCoefficient * notchWidth * Math.Pow(notchDepth, 1.5);

            double rockVolumePerDam = channelWidthFt * height * 3.0;
            double totalRockVolume = rockVolumePerDam * numberOfDams;
            double totalRockTons = totalRockVolume * 165.0 / 2000.0;
            double tssRemoval = SedimentBmpCatalog.GetRequired("rock-check-dam").TrappingEfficiency.Overall * 100.0;

            var result = new DesignResult
            {
                NumberOfDams = numberOfDams,
                SpacingFt = spacing,
                HeightFt = height,
                NotchWidthFt = notchWidth,
                NotchDepthFt = notchDepth,
                WeirCapacityCfs = weirCapacity,
                TotalRockVolumeCf = totalRockVolume,
                TotalRockTons = totalRockTons,
                TssRemovalPct = tssRemoval,
            };

            result.Steps.Add(new CalcStep("S", spacing, "ft", $"H*100/s% = {height:0.###}*100/{slopePercent:0.###}"));
            result.Steps.Add(new CalcStep("n_dams", numberOfDams, "", $"ceil(L_channel/S) = ceil({channelLengthFt:0.###}/{spacing:0.###})"));
            result.Steps.Add(new CalcStep("Q_weir", weirCapacity, "cfs",
                $"Cw*L*h^1.5 = {WeirCoefficient:0.###}*{notchWidth:0.###}*{notchDepth:0.###}^1.5"));
            result.Steps.Add(new CalcStep("V_rock", totalRockVolume, "ft^3",
                $"n*w*H*t = {numberOfDams}*{channelWidthFt:0.###}*{height:0.###}*3.0"));

            return result;
        }
    }

    /// <summary>Sequential BMP treatment train pollutant removal.</summary>
    public static class TreatmentTrain
    {
        public static readonly IReadOnlyList<string> Pollutants =
            new[] { "TSS", "TN", "TP", "bacteria", "metals" };

        public sealed class BmpStageInput
        {
            public string Type { get; set; } = "";
        }

        public sealed class BmpStageResult
        {
            public string Type { get; set; } = "";
            public string Name { get; set; } = "";
            public Dictionary<string, double> IncomingLoads { get; set; } = new Dictionary<string, double>();
            public Dictionary<string, double> RemovedLoads { get; set; } = new Dictionary<string, double>();
            public Dictionary<string, double> OutgoingLoads { get; set; } = new Dictionary<string, double>();
            public Dictionary<string, double> EfficienciesPct { get; set; } = new Dictionary<string, double>();
        }

        public sealed class AnalysisResult : TracedResult
        {
            public IReadOnlyList<BmpStageResult> Stages { get; set; } = Array.Empty<BmpStageResult>();
            public Dictionary<string, double> TotalEfficienciesPct { get; set; } = new Dictionary<string, double>();
            public Dictionary<string, double> FinalLoads { get; set; } = new Dictionary<string, double>();
        }

        /// <summary>
        /// Series removal: remaining load after each BMP; total E = 1 - product(1 - Ei).
        /// </summary>
        public static AnalysisResult Analyze(
            IReadOnlyList<BmpStageInput> bmps,
            IReadOnlyDictionary<string, double>? incomingLoads = null)
        {
            if (bmps == null) throw new ArgumentNullException(nameof(bmps));

            var loads = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (string pollutant in Pollutants)
                loads[pollutant] = 100.0;
            if (incomingLoads != null)
            {
                foreach (var kv in incomingLoads)
                    loads[kv.Key] = kv.Value;
            }

            var initialLoads = new Dictionary<string, double>(loads, StringComparer.OrdinalIgnoreCase);
            var stages = new List<BmpStageResult>();

            foreach (BmpStageInput bmp in bmps)
            {
                if (!SedimentBmpCatalog.TryGet(bmp.Type, out SedimentBmpCatalog.BmpDefinition? definition))
                {
                    stages.Add(new BmpStageResult { Type = bmp.Type, Name = bmp.Type });
                    continue;
                }

                var stage = new BmpStageResult
                {
                    Type = bmp.Type,
                    Name = definition.Name,
                };

                foreach (string pollutant in Pollutants)
                {
                    double eff = SedimentBmpCatalog.PollutantEfficiency(definition, pollutant);
                    double incoming = loads.TryGetValue(pollutant, out double inLoad) ? inLoad : 100.0;
                    double removed = incoming * eff;
                    double outgoing = incoming - removed;

                    stage.IncomingLoads[pollutant] = incoming;
                    stage.RemovedLoads[pollutant] = removed;
                    stage.OutgoingLoads[pollutant] = outgoing;
                    stage.EfficienciesPct[pollutant] = eff * 100.0;
                    loads[pollutant] = outgoing;
                }

                stages.Add(stage);
            }

            var totalEfficiencies = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (string pollutant in Pollutants)
            {
                double initial = initialLoads.TryGetValue(pollutant, out double init) ? init : 100.0;
                double final = loads.TryGetValue(pollutant, out double fin) ? fin : 100.0;
                totalEfficiencies[pollutant] = initial > 0 ? (initial - final) / initial * 100.0 : 0.0;
            }

            var result = new AnalysisResult
            {
                Stages = stages,
                TotalEfficienciesPct = totalEfficiencies,
                FinalLoads = loads,
            };

            foreach (string pollutant in Pollutants)
            {
                string productTerms = string.Join(" * ",
                    bmps.Select(b =>
                    {
                        if (!SedimentBmpCatalog.TryGet(b.Type, out SedimentBmpCatalog.BmpDefinition? def))
                            return "(1 - 0.00)";
                        double eff = SedimentBmpCatalog.PollutantEfficiency(def, pollutant);
                        return $"(1 - {eff:0.##})";
                    }));
                result.Steps.Add(new CalcStep($"E_{pollutant}", totalEfficiencies[pollutant], "%",
                    $"1 - {productTerms}"));
            }

            return result;
        }
    }
}