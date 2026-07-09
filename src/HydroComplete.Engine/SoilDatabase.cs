using System;
using System.Collections.Generic;
using System.Linq;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Embedded USDA-NRCS soil map unit lookup: HSG, K-factor, infiltration, and BMP suitability.
    /// </summary>
    public static class SoilDatabase
    {
        public sealed class SoilProperties
        {
            public string Key { get; set; } = "";
            public string Name { get; set; } = "";
            public string Series { get; set; } = "";
            public string Region { get; set; } = "";
            public string Texture { get; set; } = "";
            public char HydrologicSoilGroup { get; set; }
            public double KFactor { get; set; }
            public double InfiltrationRateInPerHr { get; set; }
            public string Drainage { get; set; } = "";
        }

        public enum BmpSuitability
        {
            Excellent,
            Good,
            Marginal,
            Poor,
            NotRecommended,
        }

        public sealed class BmpSuggestionResult
        {
            public SoilProperties Soil { get; set; } = null!;
            public string BmpType { get; set; } = "";
            public BmpSuitability Suitability { get; set; }
            public string Rationale { get; set; } = "";
            public IReadOnlyList<string> Alternatives { get; set; } = Array.Empty<string>();
        }

        private static readonly Dictionary<string, SoilProperties> Soils = CreateSoilTable();

        /// <summary>Lookup soil properties by map unit name or series key (case-insensitive, fuzzy).</summary>
        public static SoilProperties Lookup(string soilName)
        {
            if (string.IsNullOrWhiteSpace(soilName))
                throw new ArgumentException("Soil name is required.", nameof(soilName));

            string normalized = NormalizeKey(soilName);
            if (Soils.TryGetValue(normalized, out SoilProperties? exact))
                return Clone(exact);

            SoilProperties? partial = Soils.Values
                .FirstOrDefault(s =>
                    NormalizeKey(s.Name).IndexOf(normalized, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    NormalizeKey(s.Series).IndexOf(normalized, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    normalized.IndexOf(NormalizeKey(s.Series), StringComparison.OrdinalIgnoreCase) >= 0);

            if (partial != null)
                return Clone(partial);

            throw new ArgumentException($"Unknown soil map unit: {soilName}", nameof(soilName));
        }

        /// <summary>Evaluate BMP suitability for a soil map unit and BMP type.</summary>
        public static BmpSuggestionResult SuggestBmp(string soilName, string bmpType) =>
            SuggestBmp(Lookup(soilName), bmpType);

        /// <summary>Evaluate BMP suitability from resolved soil properties.</summary>
        public static BmpSuggestionResult SuggestBmp(SoilProperties soil, string bmpType)
        {
            string bmp = NormalizeBmpType(bmpType);
            BmpSuitability suitability;
            string rationale;
            var alternatives = new List<string>();

            switch (bmp)
            {
                case BmpType.Bioretention:
                case "rain-garden":
                    EvaluateInfiltrationBmp(soil, out suitability, out rationale, alternatives);
                    break;
                case BmpType.WetPond:
                    if (soil.HydrologicSoilGroup == 'A')
                    {
                        // High-infiltration HSG A soils cannot sustain a permanent pool without
                        // a liner, so infiltration IS the limiting condition for a wet pond.
                        suitability = BmpSuitability.Marginal;
                        rationale = $"HSG A soils ({soil.InfiltrationRateInPerHr:0.##} in/hr) drain too quickly " +
                                    $"to hold a permanent pool; a liner is required for a wet pond here.";
                        alternatives.Add("bioretention");
                    }
                    else
                    {
                        suitability = BmpSuitability.Excellent;
                        rationale = $"HSG {soil.HydrologicSoilGroup} soils are well suited to wet detention; " +
                                    $"infiltration rate ({soil.InfiltrationRateInPerHr:0.##} in/hr) is not limiting.";
                    }
                    break;
                case "constructed-wetland":
                    suitability = soil.HydrologicSoilGroup is 'C' or 'D'
                        ? BmpSuitability.Excellent
                        : BmpSuitability.Good;
                    rationale = soil.HydrologicSoilGroup is 'C' or 'D'
                        ? $"Poorly drained HSG {soil.HydrologicSoilGroup} ({soil.Drainage}) — wetland treatment matches site hydrology."
                        : $"HSG {soil.HydrologicSoilGroup} supports wetland BMP; verify permanent pool hydraulics.";
                    if (soil.HydrologicSoilGroup is 'A' or 'B')
                        alternatives.Add("bioretention");
                    break;
                case BmpType.SandFilter:
                    suitability = soil.HydrologicSoilGroup is 'A' or 'B'
                        ? BmpSuitability.Good
                        : BmpSuitability.Marginal;
                    rationale = $"Sand filter tolerates HSG {soil.HydrologicSoilGroup}; verify underdrain on low-K soils.";
                    break;
                case "infiltration-basin":
                    EvaluateInfiltrationBmp(soil, out suitability, out rationale, alternatives);
                    break;
                default:
                    suitability = BmpSuitability.Marginal;
                    rationale = $"No specific guidance for BMP '{bmpType}'; defaulting to HSG {soil.HydrologicSoilGroup} screening.";
                    break;
            }

            return new BmpSuggestionResult
            {
                Soil = soil,
                BmpType = bmp,
                Suitability = suitability,
                Rationale = rationale,
                Alternatives = alternatives,
            };
        }

        public static IReadOnlyList<string> AllSoilKeys() => Soils.Keys.OrderBy(k => k).ToList();

        private static void EvaluateInfiltrationBmp(
            SoilProperties soil,
            out BmpSuitability suitability,
            out string rationale,
            List<string> alternatives)
        {
            switch (soil.HydrologicSoilGroup)
            {
                case 'A':
                    suitability = BmpSuitability.Excellent;
                    rationale = $"HSG A ({soil.InfiltrationRateInPerHr:0.##} in/hr) — high infiltration supports bioretention.";
                    break;
                case 'B':
                    suitability = BmpSuitability.Good;
                    rationale = $"HSG B ({soil.InfiltrationRateInPerHr:0.##} in/hr) — bioretention feasible with amended media.";
                    break;
                case 'C':
                    suitability = BmpSuitability.Marginal;
                    rationale = $"HSG C ({soil.InfiltrationRateInPerHr:0.##} in/hr) — limited infiltration; underdrain required.";
                    alternatives.Add(BmpType.WetPond);
                    alternatives.Add("constructed-wetland");
                    break;
                default:
                    suitability = BmpSuitability.NotRecommended;
                    rationale = $"HSG D ({soil.InfiltrationRateInPerHr:0.##} in/hr) — infiltration BMP not recommended on {soil.Drainage} soils.";
                    alternatives.Add(BmpType.WetPond);
                    alternatives.Add("constructed-wetland");
                    break;
            }
        }

        private static string NormalizeBmpType(string bmpType)
        {
            if (string.IsNullOrWhiteSpace(bmpType))
                return BmpType.Bioretention;

            string lower = bmpType.Trim().ToLowerInvariant().Replace(" ", "-");
            return lower switch
            {
                "wetpond" => BmpType.WetPond,
                "wet-pond" => BmpType.WetPond,
                "rain-garden" => BmpType.Bioretention,
                "rain_garden" => BmpType.Bioretention,
                "constructed_wetland" => "constructed-wetland",
                "infiltration" => "infiltration-basin",
                _ => lower,
            };
        }

        private static string NormalizeKey(string value)
        {
            return value.Trim().ToLowerInvariant()
                .Replace(" ", "-")
                .Replace("_", "-");
        }

        private static SoilProperties Clone(SoilProperties source)
        {
            return new SoilProperties
            {
                Key = source.Key,
                Name = source.Name,
                Series = source.Series,
                Region = source.Region,
                Texture = source.Texture,
                HydrologicSoilGroup = source.HydrologicSoilGroup,
                KFactor = source.KFactor,
                InfiltrationRateInPerHr = source.InfiltrationRateInPerHr,
                Drainage = source.Drainage,
            };
        }

        private static SoilProperties S(
            string key,
            string name,
            string series,
            string region,
            string texture,
            char hsg,
            double k,
            double fc,
            string drainage)
        {
            return new SoilProperties
            {
                Key = key,
                Name = name,
                Series = series,
                Region = region,
                Texture = texture,
                HydrologicSoilGroup = hsg,
                KFactor = k,
                InfiltrationRateInPerHr = fc,
                Drainage = drainage,
            };
        }

        private static Dictionary<string, SoilProperties> CreateSoilTable()
        {
            var soils = new[]
            {
                S("cecil-sandy-loam", "Cecil sandy loam", "Cecil", "Piedmont (NC, SC, GA, VA)", "sandy-loam", 'B', 0.24, 0.60, "well drained"),
                S("cecil-clay-loam", "Cecil clay loam", "Cecil", "Piedmont (NC, SC, GA, VA)", "clay-loam", 'C', 0.28, 0.20, "well drained"),
                S("pacolet-sandy-loam", "Pacolet sandy loam", "Pacolet", "Piedmont (NC, SC, VA)", "sandy-loam", 'B', 0.28, 0.50, "well drained"),
                S("madison-sandy-loam", "Madison sandy loam", "Madison", "Piedmont (NC, VA)", "sandy-loam", 'B', 0.24, 0.55, "well drained"),
                S("appling-sandy-loam", "Appling sandy loam", "Appling", "Piedmont (NC, SC, GA, VA)", "sandy-loam", 'B', 0.24, 0.60, "well drained"),
                S("wedowee-sandy-loam", "Wedowee sandy loam", "Wedowee", "Piedmont (NC, SC, GA, AL)", "sandy-loam", 'B', 0.24, 0.55, "well drained"),
                S("georgeville-silt-loam", "Georgeville silt loam", "Georgeville", "Piedmont (NC, SC, VA)", "silt-loam", 'B', 0.37, 0.35, "well drained"),
                S("herndon-silt-loam", "Herndon silt loam", "Herndon", "Piedmont (NC, SC, VA)", "silt-loam", 'B', 0.37, 0.30, "well drained"),
                S("iredell-loam", "Iredell loam", "Iredell", "Piedmont (NC, SC)", "clay-loam", 'D', 0.32, 0.05, "moderately well drained"),
                S("mecklenburg-loam", "Mecklenburg loam", "Mecklenburg", "Piedmont (NC, SC)", "clay-loam", 'C', 0.32, 0.15, "well drained"),
                S("norfolk-sandy-loam", "Norfolk sandy loam", "Norfolk", "Coastal Plain (NC, SC, VA, GA)", "sandy-loam", 'A', 0.17, 0.80, "well drained"),
                S("goldsboro-sandy-loam", "Goldsboro sandy loam", "Goldsboro", "Coastal Plain (NC, SC, VA)", "sandy-loam", 'B', 0.20, 0.45, "moderately well drained"),
                S("lynchburg-sandy-loam", "Lynchburg sandy loam", "Lynchburg", "Coastal Plain (NC, SC, VA)", "sandy-loam", 'C', 0.20, 0.15, "somewhat poorly drained"),
                S("rains-sandy-loam", "Rains sandy loam", "Rains", "Coastal Plain (NC, SC)", "sandy-loam", 'D', 0.17, 0.03, "poorly drained"),
                S("wagram-sand", "Wagram sand", "Wagram", "Coastal Plain (NC, SC)", "sand", 'A', 0.10, 1.50, "well drained"),
                S("hayesville-loam", "Hayesville loam", "Hayesville", "Blue Ridge (NC, SC, GA, VA)", "loam", 'B', 0.28, 0.40, "well drained"),
                S("evard-sandy-loam", "Evard sandy loam", "Evard", "Blue Ridge (NC, SC, GA)", "sandy-loam", 'B', 0.24, 0.50, "well drained"),
                S("davidson-clay-loam", "Davidson clay loam", "Davidson", "Piedmont (NC, TN, VA)", "clay-loam", 'C', 0.30, 0.12, "well drained"),
                S("chewacla-loam", "Chewacla loam", "Chewacla", "Piedmont (NC, SC)", "loam", 'B', 0.32, 0.35, "well drained"),
                S("cataula-sandy-loam", "Cataula sandy loam", "Cataula", "Piedmont (NC, SC, GA)", "sandy-loam", 'B', 0.26, 0.55, "well drained"),
                S("duplin-sandy-loam", "Duplin sandy loam", "Duplin", "Coastal Plain (NC, SC)", "sandy-loam", 'B', 0.18, 0.40, "moderately well drained"),
                S("cape-fear-loam", "Cape Fear loam", "Cape Fear", "Coastal Plain (NC, SC)", "loam", 'D', 0.20, 0.04, "poorly drained"),
                S("johnston-loam", "Johnston loam", "Johnston", "Coastal Plain (NC, SC, VA)", "loam", 'C', 0.22, 0.12, "somewhat poorly drained"),
                S("roanoke-loam", "Roanoke loam", "Roanoke", "Coastal Plain (NC, SC, VA)", "loam", 'D', 0.24, 0.03, "poorly drained"),
                S("platte-silt-loam", "Platte silt loam", "Platte", "Midwest (IA, NE)", "silt-loam", 'B', 0.42, 0.25, "well drained"),
                S("miami-silt-loam", "Miami silt loam", "Miami", "Midwest (IN, OH)", "silt-loam", 'B', 0.35, 0.28, "well drained"),
                S("drummer-silty-clay-loam", "Drummer silty clay loam", "Drummer", "Midwest (IL)", "silty-clay-loam", 'D', 0.28, 0.04, "poorly drained"),
                S("hagerstown-silt-loam", "Hagerstown silt loam", "Hagerstown", "Appalachian (PA, MD)", "silt-loam", 'B', 0.32, 0.30, "well drained"),
                S("frederick-silt-loam", "Frederick silt loam", "Frederick", "Appalachian (VA, WV)", "silt-loam", 'B', 0.30, 0.32, "well drained"),
                S("myatt-sandy-loam", "Myatt sandy loam", "Myatt", "Coastal Plain (SC, GA)", "sandy-loam", 'A', 0.15, 0.90, "well drained"),
                S("barnwell-sandy-loam", "Barnwell sandy loam", "Barnwell", "Coastal Plain (SC, GA)", "sandy-loam", 'B', 0.19, 0.42, "moderately well drained"),
                S("marlboro-sandy-loam", "Marlboro sandy loam", "Marlboro", "Coastal Plain (SC, NC)", "sandy-loam", 'C', 0.21, 0.14, "somewhat poorly drained"),
                S("buncombe-silt-loam", "Buncombe silt loam", "Buncombe", "Piedmont (NC, SC)", "silt-loam", 'B', 0.34, 0.32, "well drained"),
                S("cleveland-silt-loam", "Cleveland silt loam", "Cleveland", "Piedmont (NC, SC)", "silt-loam", 'B', 0.33, 0.30, "well drained"),
                S("rhodhiss-sandy-loam", "Rhodhiss sandy loam", "Rhodhiss", "Piedmont (NC)", "sandy-loam", 'B', 0.25, 0.52, "well drained"),
                S("enon-sandy-loam", "Enon sandy loam", "Enon", "Piedmont (NC, SC, VA)", "sandy-loam", 'B', 0.27, 0.48, "well drained"),
                S("wake-sandy-loam", "Wake sandy loam", "Wake", "Piedmont (NC)", "sandy-loam", 'B', 0.26, 0.50, "well drained"),
                S("davidson-sandy-clay-loam", "Davidson sandy clay loam", "Davidson", "Piedmont (NC, TN)", "sandy-clay-loam", 'C', 0.31, 0.10, "well drained"),
                S("catawba-sandy-loam", "Catawba sandy loam", "Catawba", "Piedmont (NC, SC)", "sandy-loam", 'B', 0.25, 0.53, "well drained"),
                S("orangeburg-sandy-loam", "Orangeburg sandy loam", "Orangeburg", "Coastal Plain (SC, GA, FL)", "sandy-loam", 'A', 0.16, 0.85, "well drained"),
                S("lucy-sandy-loam", "Lucy sandy loam", "Lucy", "Coastal Plain (SC, GA)", "sandy-loam", 'A', 0.14, 0.95, "well drained"),
                S("kalmia-sand", "Kalmia sand", "Kalmia", "Coastal Plain (SC, NC)", "sand", 'A', 0.11, 1.30, "excessively drained"),
                S("leon-sand", "Leon sand", "Leon", "Coastal Plain (FL, GA)", "sand", 'A', 0.08, 1.20, "excessively drained"),
                S("brookman-loam", "Brookman loam", "Brookman", "Coastal Plain (SC, GA)", "loam", 'B', 0.23, 0.35, "well drained"),
                S("generic-sand", "Sand (generic)", "(generic)", "All", "sand", 'A', 0.05, 1.20, "excessively drained"),
                S("generic-loamy-sand", "Loamy sand (generic)", "(generic)", "All", "loamy-sand", 'A', 0.12, 0.80, "well drained"),
                S("generic-sandy-loam", "Sandy loam (generic)", "(generic)", "All", "sandy-loam", 'B', 0.27, 0.45, "well drained"),
                S("generic-loam", "Loam (generic)", "(generic)", "All", "loam", 'B', 0.38, 0.25, "well drained"),
                S("generic-silt-loam", "Silt loam (generic)", "(generic)", "All", "silt-loam", 'B', 0.48, 0.20, "moderately well drained"),
                S("generic-clay-loam", "Clay loam (generic)", "(generic)", "All", "clay-loam", 'C', 0.37, 0.10, "moderately well drained"),
                S("generic-clay", "Clay (generic)", "(generic)", "All", "clay", 'D', 0.13, 0.03, "poorly drained"),
            };

            return soils.ToDictionary(s => s.Key, s => s, StringComparer.OrdinalIgnoreCase);
        }
    }
}