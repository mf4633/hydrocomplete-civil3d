using System;
using System.Collections.Generic;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Revised Universal Soil Loss Equation (RUSLE): A = R × K × LS × C × P.
    /// Soil loss A is in tons/acre/year. Based on USDA Agriculture Handbook No. 703.
    /// </summary>
    public static class RusleAnalysis
    {
        /// <summary>Rainfall-runoff erosivity R by region (isoerodent maps).</summary>
        public static readonly IReadOnlyDictionary<string, double> RainfallErosivity =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["charlotte-nc"] = 180,
                ["raleigh-nc"] = 165,
                ["asheville-nc"] = 140,
                ["atlanta-ga"] = 220,
                ["columbia-sc"] = 200,
                ["richmond-va"] = 150,
            };

        /// <summary>Soil erodibility K (tons/acre per unit R).</summary>
        public static readonly IReadOnlyDictionary<string, double> SoilErodibility =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["sand"] = 0.05,
                ["loamy-sand"] = 0.12,
                ["sandy-loam"] = 0.27,
                ["loam"] = 0.38,
                ["silt-loam"] = 0.48,
                ["silt"] = 0.60,
                ["sandy-clay-loam"] = 0.27,
                ["clay-loam"] = 0.37,
                ["silty-clay-loam"] = 0.43,
                ["sandy-clay"] = 0.14,
                ["silty-clay"] = 0.25,
                ["clay"] = 0.13,
            };

        /// <summary>Cover and management factor C.</summary>
        public static readonly IReadOnlyDictionary<string, double> CoverManagement =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["bare-soil"] = 1.00,
                ["construction-site"] = 0.90,
                ["temporary-seeding"] = 0.45,
                ["permanent-grass"] = 0.01,
                ["dense-grass"] = 0.003,
                ["mulch-straw"] = 0.06,
                ["mulch-wood"] = 0.05,
                ["erosion-blanket"] = 0.10,
                ["mature-forest"] = 0.001,
                ["cropland-conventional"] = 0.40,
                ["cropland-conservation"] = 0.15,
            };

        /// <summary>Support practice factor P.</summary>
        public static readonly IReadOnlyDictionary<string, double> SupportPractice =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["none"] = 1.00,
                ["contour-farming"] = 0.60,
                ["strip-cropping"] = 0.50,
                ["terracing"] = 0.15,
                ["sediment-basin"] = 0.50,
                ["silt-fence"] = 0.70,
                ["check-dam"] = 0.65,
            };

        public sealed class SoilLossResult : TracedResult
        {
            /// <summary>Annual soil loss, tons/acre/year.</summary>
            public double SoilLossPerAcreTonsYr { get; set; }

            /// <summary>Total annual soil loss over the site area, tons/year.</summary>
            public double TotalSoilLossTonsYr { get; set; }

            public double R { get; set; }
            public double K { get; set; }
            public double Ls { get; set; }
            public double C { get; set; }
            public double P { get; set; }
            public double AreaAcres { get; set; }
            public string KSource { get; set; } = "";
        }

        /// <summary>Inputs for a RUSLE site analysis.</summary>
        public sealed class SiteInput
        {
            public string Region { get; set; } = "charlotte-nc";
            public string SoilType { get; set; } = "loam";
            public string Cover { get; set; } = "construction-site";
            public string Practice { get; set; } = "none";
            public double SlopeLengthFt { get; set; } = 100.0;
            public double SlopePercent { get; set; } = 5.0;
            public double AreaAcres { get; set; } = 1.0;

            /// <summary>Optional site-specific K from SSURGO; overrides <see cref="SoilType"/> lookup.</summary>
            public double? KFactor { get; set; }
        }

        /// <summary>
        /// LS slope length-steepness factor from RUSLE methodology.
        /// </summary>
        public static double LsFactor(double slopeLengthFt, double slopePercent)
        {
            if (slopeLengthFt < 0) throw new ArgumentOutOfRangeException(nameof(slopeLengthFt));
            if (slopePercent < 0) throw new ArgumentOutOfRangeException(nameof(slopePercent));

            double m = slopePercent < 1 ? 0.2 :
                       slopePercent < 3 ? 0.3 :
                       slopePercent < 5 ? 0.4 : 0.5;

            double l = Math.Pow(slopeLengthFt / 72.6, m);

            double s = slopePercent < 9
                ? 10.8 * Math.Sin(Math.Atan(slopePercent / 100.0)) + 0.03
                : 16.8 * Math.Sin(Math.Atan(slopePercent / 100.0)) - 0.50;

            return l * s;
        }

        /// <summary>Annual soil loss A = R × K × LS × C × P.</summary>
        public static SoilLossResult SoilLoss(SiteInput site)
        {
            if (site == null) throw new ArgumentNullException(nameof(site));
            if (site.AreaAcres < 0) throw new ArgumentOutOfRangeException(nameof(site));

            double r = Lookup(RainfallErosivity, site.Region, 180);
            double k;
            string kSource;
            if (site.KFactor.HasValue)
            {
                k = site.KFactor.Value;
                kSource = "USDA SSURGO";
            }
            else
            {
                k = Lookup(SoilErodibility, site.SoilType, 0.38);
                kSource = $"lookup({site.SoilType})";
            }

            double ls = LsFactor(site.SlopeLengthFt, site.SlopePercent);
            double c = Lookup(CoverManagement, site.Cover, 0.90);
            double p = Lookup(SupportPractice, site.Practice, 1.00);

            double soilLossPerAcre = r * k * ls * c * p;
            double totalSoilLoss = soilLossPerAcre * site.AreaAcres;

            var result = new SoilLossResult
            {
                SoilLossPerAcreTonsYr = soilLossPerAcre,
                TotalSoilLossTonsYr = totalSoilLoss,
                R = r,
                K = k,
                Ls = ls,
                C = c,
                P = p,
                AreaAcres = site.AreaAcres,
                KSource = kSource,
            };

            result.Steps.Add(new CalcStep("R", r, "", $"Rainfall erosivity ({site.Region})"));
            result.Steps.Add(new CalcStep("K", k, "", $"Soil erodibility ({kSource})"));
            result.Steps.Add(new CalcStep("LS", ls, "", $"Slope length-steepness ({site.SlopeLengthFt:0.###} ft at {site.SlopePercent:0.###}%)"));
            result.Steps.Add(new CalcStep("C", c, "", $"Cover management ({site.Cover})"));
            result.Steps.Add(new CalcStep("P", p, "", $"Support practice ({site.Practice})"));
            result.Steps.Add(new CalcStep("A", soilLossPerAcre, "tons/ac/yr",
                $"R*K*LS*C*P = {r:0.###}*{k:0.###}*{ls:0.##}*{c:0.###}*{p:0.##}"));
            result.Steps.Add(new CalcStep("A_total", totalSoilLoss, "tons/yr",
                $"A*Area = {soilLossPerAcre:0.##}*{site.AreaAcres:0.###}"));

            return result;
        }

        private static double Lookup(IReadOnlyDictionary<string, double> table, string key, double fallback)
        {
            if (string.IsNullOrWhiteSpace(key)) return fallback;
            return table.TryGetValue(key, out double value) ? value : fallback;
        }
    }
}