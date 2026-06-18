using System;
using System.Collections.Generic;

namespace HydroComplete.Engine
{
    /// <summary>Land-use categories for EMC and buildup/washoff lookup tables.</summary>
    public static class LandUse
    {
        public const string Residential = "residential-medium";
        public const string Commercial = "commercial";
        public const string Industrial = "industrial";
    }

    /// <summary>Supported BMP type keys (IDEAL / Int'l BMP Database).</summary>
    public static class BmpType
    {
        public const string Bioretention = "bioretention";
        public const string WetPond = "wet-pond";
        public const string SandFilter = "sand-filter";
        public const string VegetatedSwale = "vegetated-swale";
    }

    /// <summary>Core water-quality pollutants modeled by the IDEAL engine.</summary>
    public static class Pollutant
    {
        public const string Tss = "TSS";
        public const string Tn = "TN";
        public const string Tp = "TP";

        public static readonly IReadOnlyList<string> Core = new[] { Tss, Tn, Tp };
    }

    /// <summary>Antecedent moisture condition for SCS curve-number adjustment.</summary>
    public enum AntecedentMoistureCondition
    {
        /// <summary>AMC II — use supplied curve number unchanged.</summary>
        AmcII = 0,

        /// <summary>AMC I (dry) — CN reduced by 13 (NEH-4 approximation).</summary>
        AmcI = 1,

        /// <summary>AMC III (wet) — CN increased by 13 (NEH-4 approximation).</summary>
        AmcIII = 2,

        /// <summary>Infer AMC from antecedent dry days (&gt;5 → I, &lt;2 → III, else II).</summary>
        Auto = 3,
    }

    /// <summary>Buildup kinetics parameters: B(t) = Bmax × (1 − e^(−k·t)).</summary>
    public sealed class BuildupParameters
    {
        public BuildupParameters(double bmaxPerAcre, double kPerDay)
        {
            BmaxPerAcre = bmaxPerAcre;
            KPerDay = kPerDay;
        }

        /// <summary>Maximum accumulation, lbs/ac (or cfu/ac for bacteria).</summary>
        public double BmaxPerAcre { get; }

        /// <summary>Buildup rate constant, 1/day.</summary>
        public double KPerDay { get; }
    }

    /// <summary>Washoff kinetics parameters: W = B_avail × min(1, a·R^b).</summary>
    public sealed class WashoffParameters
    {
        public WashoffParameters(double a, double b)
        {
            A = a;
            B = b;
        }

        public double A { get; }
        public double B { get; }
    }

    /// <summary>BMP definition: trapping efficiencies and sizing design parameters.</summary>
    public sealed class BmpDefinition
    {
        public BmpDefinition(
            string key,
            string name,
            IReadOnlyDictionary<string, double> trappingEfficiency,
            double volumeReduction,
            double? avgDepthFt,
            double? surfaceAreaRatio,
            double? surfaceLoadingRateGalPerMinPerSf,
            double? bottomWidthFt,
            double? depthFt,
            double? minLengthFt)
        {
            Key = key;
            Name = name;
            TrappingEfficiency = trappingEfficiency;
            VolumeReduction = volumeReduction;
            AvgDepthFt = avgDepthFt;
            SurfaceAreaRatio = surfaceAreaRatio;
            SurfaceLoadingRateGalPerMinPerSf = surfaceLoadingRateGalPerMinPerSf;
            BottomWidthFt = bottomWidthFt;
            DepthFt = depthFt;
            MinLengthFt = minLengthFt;
        }

        public string Key { get; }
        public string Name { get; }
        public IReadOnlyDictionary<string, double> TrappingEfficiency { get; }
        public double VolumeReduction { get; }
        public double? AvgDepthFt { get; }
        public double? SurfaceAreaRatio { get; }
        public double? SurfaceLoadingRateGalPerMinPerSf { get; }
        public double? BottomWidthFt { get; }
        public double? DepthFt { get; }
        public double? MinLengthFt { get; }
    }

    /// <summary>
    /// Embedded IDEAL lookup tables: BMP efficiencies, EMC values, and
    /// land-use-specific buildup/washoff kinetics (from NSQD / NURP).
    /// </summary>
    public static class BmpLibrary
    {
        public const double GallonsPerCf = 7.48;
        public const double LbsPerGallon = 8.34;
        public const double MgPerLb = 1_000_000.0;
        public const double SqFtPerAcre = 43_560.0;
        public const double InchesPerFoot = 12.0;
        public const double MgPerLiterPerLbPerCf = 16_018.5;

        private static readonly Dictionary<string, BmpDefinition> Bmps = CreateBmpDefinitions();
        private static readonly Dictionary<string, Dictionary<string, double>> EmcByLandUse = CreateEmcTable();
        private static readonly Dictionary<string, Dictionary<string, BuildupParameters>> BuildupByLandUse = CreateBuildupTable();
        private static readonly Dictionary<string, Dictionary<string, WashoffParameters>> WashoffByLandUse = CreateWashoffTable();
        private static readonly Dictionary<string, double> FirstFlushMassFractions = CreateFirstFlushMassFractions();

        public static IReadOnlyDictionary<string, BmpDefinition> AllBmps => Bmps;

        public static BmpDefinition GetBmp(string bmpType)
        {
            if (string.IsNullOrWhiteSpace(bmpType))
                throw new ArgumentException("BMP type is required.", nameof(bmpType));

            if (!Bmps.TryGetValue(bmpType, out BmpDefinition? bmp))
                throw new ArgumentException($"Unknown BMP type: {bmpType}", nameof(bmpType));

            return bmp;
        }

        public static double GetEmc(string landUse, string pollutant)
        {
            if (!EmcByLandUse.TryGetValue(landUse, out Dictionary<string, double>? emc))
                emc = EmcByLandUse[LandUse.Residential];

            if (!emc.TryGetValue(pollutant, out double value))
                return 0.0;

            return value;
        }

        public static BuildupParameters GetBuildupParameters(string landUse, string pollutant)
        {
            if (!BuildupByLandUse.TryGetValue(landUse, out Dictionary<string, BuildupParameters>? lu))
                lu = BuildupByLandUse[LandUse.Residential];

            if (!lu.TryGetValue(pollutant, out BuildupParameters? p))
                throw new ArgumentException($"No buildup parameters for pollutant '{pollutant}'.");

            return p;
        }

        public static WashoffParameters GetWashoffParameters(string landUse, string pollutant)
        {
            if (!WashoffByLandUse.TryGetValue(landUse, out Dictionary<string, WashoffParameters>? lu))
                lu = WashoffByLandUse[LandUse.Residential];

            if (!lu.TryGetValue(pollutant, out WashoffParameters? p))
                throw new ArgumentException($"No washoff parameters for pollutant '{pollutant}'.");

            return p;
        }

        public static double GetFirstFlushMassFraction(string pollutant)
        {
            if (FirstFlushMassFractions.TryGetValue(pollutant, out double fraction))
                return fraction;

            return 0.40;
        }

        private static Dictionary<string, BmpDefinition> CreateBmpDefinitions()
        {
            return new Dictionary<string, BmpDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                [BmpType.Bioretention] = new BmpDefinition(
                    BmpType.Bioretention,
                    "Bioretention Cell (Rain Garden)",
                    new Dictionary<string, double>
                    {
                        [Pollutant.Tss] = 0.85,
                        [Pollutant.Tn] = 0.45,
                        [Pollutant.Tp] = 0.60,
                    },
                    volumeReduction: 0.0,
                    avgDepthFt: null,
                    surfaceAreaRatio: 0.05,
                    surfaceLoadingRateGalPerMinPerSf: null,
                    bottomWidthFt: null,
                    depthFt: 2.5,
                    minLengthFt: null),

                [BmpType.WetPond] = new BmpDefinition(
                    BmpType.WetPond,
                    "Wet Retention Pond",
                    new Dictionary<string, double>
                    {
                        [Pollutant.Tss] = 0.80,
                        [Pollutant.Tn] = 0.40,
                        [Pollutant.Tp] = 0.50,
                    },
                    volumeReduction: 0.0,
                    avgDepthFt: 4.0,
                    surfaceAreaRatio: null,
                    surfaceLoadingRateGalPerMinPerSf: null,
                    bottomWidthFt: null,
                    depthFt: null,
                    minLengthFt: null),

                [BmpType.SandFilter] = new BmpDefinition(
                    BmpType.SandFilter,
                    "Sand Filter",
                    new Dictionary<string, double>
                    {
                        [Pollutant.Tss] = 0.85,
                        [Pollutant.Tn] = 0.35,
                        [Pollutant.Tp] = 0.50,
                    },
                    volumeReduction: 0.0,
                    avgDepthFt: 2.5,
                    surfaceAreaRatio: null,
                    surfaceLoadingRateGalPerMinPerSf: 3.5,
                    bottomWidthFt: null,
                    depthFt: null,
                    minLengthFt: null),

                [BmpType.VegetatedSwale] = new BmpDefinition(
                    BmpType.VegetatedSwale,
                    "Vegetated Swale",
                    new Dictionary<string, double>
                    {
                        [Pollutant.Tss] = 0.65,
                        [Pollutant.Tn] = 0.35,
                        [Pollutant.Tp] = 0.40,
                    },
                    volumeReduction: 0.0,
                    avgDepthFt: null,
                    surfaceAreaRatio: null,
                    surfaceLoadingRateGalPerMinPerSf: null,
                    bottomWidthFt: 2.0,
                    depthFt: 1.5,
                    minLengthFt: 50.0),
            };
        }

        private static Dictionary<string, Dictionary<string, double>> CreateEmcTable()
        {
            return new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase)
            {
                [LandUse.Residential] = Emc(101, 2.2, 0.38),
                [LandUse.Commercial] = Emc(163, 2.7, 0.41),
                [LandUse.Industrial] = Emc(198, 2.9, 0.48),
            };
        }

        private static Dictionary<string, double> Emc(double tss, double tn, double tp)
        {
            return new Dictionary<string, double>
            {
                [Pollutant.Tss] = tss,
                [Pollutant.Tn] = tn,
                [Pollutant.Tp] = tp,
            };
        }

        private static Dictionary<string, Dictionary<string, BuildupParameters>> CreateBuildupTable()
        {
            return new Dictionary<string, Dictionary<string, BuildupParameters>>(StringComparer.OrdinalIgnoreCase)
            {
                [LandUse.Residential] = BuildupSet(40, 0.18, 2.0, 0.12, 0.38, 0.10),
                [LandUse.Commercial] = BuildupSet(80, 0.25, 2.8, 0.15, 0.42, 0.12),
                [LandUse.Industrial] = BuildupSet(100, 0.22, 3.0, 0.14, 0.50, 0.11),
            };
        }

        private static Dictionary<string, BuildupParameters> BuildupSet(
            double tssBmax, double tssK,
            double tnBmax, double tnK,
            double tpBmax, double tpK)
        {
            return new Dictionary<string, BuildupParameters>
            {
                [Pollutant.Tss] = new BuildupParameters(tssBmax, tssK),
                [Pollutant.Tn] = new BuildupParameters(tnBmax, tnK),
                [Pollutant.Tp] = new BuildupParameters(tpBmax, tpK),
            };
        }

        private static Dictionary<string, Dictionary<string, WashoffParameters>> CreateWashoffTable()
        {
            return new Dictionary<string, Dictionary<string, WashoffParameters>>(StringComparer.OrdinalIgnoreCase)
            {
                [LandUse.Residential] = WashoffSet(0.15, 1.4, 0.08, 1.2, 0.06, 1.3),
                [LandUse.Commercial] = WashoffSet(0.20, 1.5, 0.10, 1.3, 0.08, 1.4),
                [LandUse.Industrial] = WashoffSet(0.22, 1.6, 0.10, 1.3, 0.08, 1.4),
            };
        }

        private static Dictionary<string, WashoffParameters> WashoffSet(
            double tssA, double tssB,
            double tnA, double tnB,
            double tpA, double tpB)
        {
            return new Dictionary<string, WashoffParameters>
            {
                [Pollutant.Tss] = new WashoffParameters(tssA, tssB),
                [Pollutant.Tn] = new WashoffParameters(tnA, tnB),
                [Pollutant.Tp] = new WashoffParameters(tpA, tpB),
            };
        }

        private static Dictionary<string, double> CreateFirstFlushMassFractions()
        {
            return new Dictionary<string, double>
            {
                [Pollutant.Tss] = 0.50,
                [Pollutant.Tn] = 0.35,
                [Pollutant.Tp] = 0.45,
            };
        }
    }
}