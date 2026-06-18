using System;
using System.Collections.Generic;
using System.Linq;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Particle settling (Stokes/Rubey), USDA 7-bin PSD, and Camp (1946) basin trap efficiency.
    /// </summary>
    public static class SedimentSettling
    {
        /// <summary>USDA particle-size bin with representative diameter (mm).</summary>
        public sealed class ParticleBin
        {
            public ParticleBin(string key, string label, double dRepMm)
            {
                Key = key;
                Label = label;
                DRepMm = dRepMm;
            }

            public string Key { get; }
            public string Label { get; }
            public double DRepMm { get; }
        }

        /// <summary>USDA 7-bin PSD definitions (Gee &amp; Bauder 1986).</summary>
        public static readonly IReadOnlyList<ParticleBin> UsdaBins = new[]
        {
            new ParticleBin("clay", "Clay", 0.0008),
            new ParticleBin("silt", "Silt", 0.01),
            new ParticleBin("sandVFS", "Very Fine Sand", 0.075),
            new ParticleBin("sandFS", "Fine Sand", 0.158),
            new ParticleBin("sandMS", "Medium Sand", 0.354),
            new ParticleBin("sandCS", "Coarse Sand", 0.707),
            new ParticleBin("sandVCS", "Very Coarse Sand", 1.414),
        };

        /// <summary>Seven-bin particle-size distribution (fractions 0..1).</summary>
        public sealed class SevenBinPsd
        {
            public double Clay { get; set; }
            public double Silt { get; set; }
            public double SandVfs { get; set; }
            public double SandFs { get; set; }
            public double SandMs { get; set; }
            public double SandCs { get; set; }
            public double SandVcs { get; set; }
            public string Source { get; set; } = "";

            public double Fraction(string key)
            {
                switch (key)
                {
                    case "clay": return Clay;
                    case "silt": return Silt;
                    case "sandVFS": return SandVfs;
                    case "sandFS": return SandFs;
                    case "sandMS": return SandMs;
                    case "sandCS": return SandCs;
                    case "sandVCS": return SandVcs;
                    default: return 0.0;
                }
            }
        }

        /// <summary>Coarse 3-bin PSD (clay/silt/sand fractions 0..1).</summary>
        public sealed class ThreeBinPsd
        {
            public double Clay { get; set; }
            public double Silt { get; set; }
            public double Sand { get; set; }
        }

        public sealed class SettlingVelocityResult
        {
            public double VsFps { get; set; }
            public double VsMps { get; set; }
            public string Regime { get; set; } = "";
            public double ReynoldsNumber { get; set; }
        }

        public sealed class CampBinResult
        {
            public string Key { get; set; } = "";
            public string Label { get; set; } = "";
            public double DRepMm { get; set; }
            public double Fraction { get; set; }
            public double VsFps { get; set; }
            public string Regime { get; set; } = "";
            public double Eta { get; set; }
            public double Contribution { get; set; }
        }

        public sealed class CampEfficiencyResult : TracedResult
        {
            public double OverallEfficiencyPct { get; set; }
            public double OverflowVelocityFps { get; set; }
            public IReadOnlyList<CampBinResult> Bins { get; set; } = Array.Empty<CampBinResult>();
            public string PsdSource { get; set; } = "";
        }

        public sealed class WeightedTrapResult : TracedResult
        {
            public string StructureName { get; set; } = "";
            public double OverallEfficiencyPct { get; set; }
            public double ClayRemovalPct { get; set; }
            public double SiltRemovalPct { get; set; }
            public double SandRemovalPct { get; set; }
        }

        /// <summary>Site PSD percentages (0–100) for 7-bin builder.</summary>
        public sealed class SitePsdInput
        {
            public double PctClay { get; set; }
            public double PctSilt { get; set; }
            public double PctSand { get; set; }
            public double? PctSandVcs { get; set; }
            public double? PctSandCs { get; set; }
            public double? PctSandMs { get; set; }
            public double? PctSandFs { get; set; }
            public double? PctSandVfs { get; set; }
        }

        /// <summary>
        /// Settling velocity for a spherical particle (Stokes laminar or Rubey transitional).
        /// </summary>
        public static SettlingVelocityResult SettlingVelocity(
            double diameterMm,
            double waterTempC = 20.0,
            double specificGravity = 2.65)
        {
            if (diameterMm <= 0) throw new ArgumentOutOfRangeException(nameof(diameterMm));
            if (specificGravity <= 1.0) throw new ArgumentOutOfRangeException(nameof(specificGravity));

            double dM = diameterMm / 1000.0;
            const double g = 9.81;
            const double rhoW = 1000.0;
            double rhoS = specificGravity * rhoW;

            double nu = WaterKinematicViscosity(waterTempC);
            double mu = nu * rhoW;

            double vsStokes = g * (rhoS - rhoW) * dM * dM / (18.0 * mu);
            double reStokes = vsStokes * dM / nu;

            double vs;
            string regime;
            if (reStokes <= 0.5)
            {
                vs = vsStokes;
                regime = "Stokes (laminar)";
            }
            else
            {
                double sgm1 = specificGravity - 1.0;
                double term = 36.0 * nu * nu / (g * Math.Pow(dM, 3) * sgm1);
                double f = Math.Sqrt(2.0 / 3.0 + term) - Math.Sqrt(term);
                vs = f * Math.Sqrt(g * dM * sgm1);
                regime = "Rubey (transitional)";
            }

            double re = vs * dM / nu;
            return new SettlingVelocityResult
            {
                VsMps = vs,
                VsFps = vs * 3.28084,
                Regime = regime,
                ReynoldsNumber = re,
            };
        }

        /// <summary>Build USDA 7-bin PSD from site percentages (SSURGO or Kilmer-Alexander defaults).</summary>
        public static SevenBinPsd BuildSevenBinPsd(SitePsdInput site)
        {
            if (site == null) throw new ArgumentNullException(nameof(site));

            double clay = site.PctClay / 100.0;
            double silt = site.PctSilt / 100.0;
            double sand = site.PctSand / 100.0;

            double rawVcs = (site.PctSandVcs ?? 0) / 100.0;
            double rawCs = (site.PctSandCs ?? 0) / 100.0;
            double rawMs = (site.PctSandMs ?? 0) / 100.0;
            double rawFs = (site.PctSandFs ?? 0) / 100.0;
            double rawVfs = (site.PctSandVfs ?? 0) / 100.0;
            double rawSum = rawVcs + rawCs + rawMs + rawFs + rawVfs;
            bool useSsurgO = rawSum > 0 && sand > 0;

            double sandVcs, sandCs, sandMs, sandFs, sandVfs;
            if (useSsurgO)
            {
                double k = sand / rawSum;
                sandVcs = rawVcs * k;
                sandCs = rawCs * k;
                sandMs = rawMs * k;
                sandFs = rawFs * k;
                sandVfs = rawVfs * k;
            }
            else
            {
                sandVfs = sand * 0.15;
                sandFs = sand * 0.25;
                sandMs = sand * 0.30;
                sandCs = sand * 0.20;
                sandVcs = sand * 0.10;
            }

            return new SevenBinPsd
            {
                Clay = clay,
                Silt = silt,
                SandVfs = sandVfs,
                SandFs = sandFs,
                SandMs = sandMs,
                SandCs = sandCs,
                SandVcs = sandVcs,
                Source = useSsurgO ? "SSURGO sand subclasses" : "Kilmer-Alexander defaults",
            };
        }

        /// <summary>
        /// Camp (1946) ideal basin trap efficiency: η(d) = min(1, Vs/Vo), weighted by PSD fractions.
        /// </summary>
        public static CampEfficiencyResult CampEfficiency(
            double designFlowCfs,
            double surfaceAreaSf,
            SevenBinPsd psd,
            double waterTempC = 20.0,
            double specificGravity = 2.65)
        {
            if (designFlowCfs < 0) throw new ArgumentOutOfRangeException(nameof(designFlowCfs));
            if (surfaceAreaSf <= 0) throw new ArgumentOutOfRangeException(nameof(surfaceAreaSf));
            if (psd == null) throw new ArgumentNullException(nameof(psd));

            double voFps = designFlowCfs / surfaceAreaSf;
            var bins = new List<CampBinResult>();
            double overall = 0.0;

            foreach (ParticleBin bin in UsdaBins)
            {
                double fraction = psd.Fraction(bin.Key);
                var sv = SettlingVelocity(bin.DRepMm, waterTempC, specificGravity);
                double eta = voFps > 0 ? Math.Min(1.0, sv.VsFps / voFps) : 0.0;
                double contribution = fraction * eta;
                overall += contribution;

                bins.Add(new CampBinResult
                {
                    Key = bin.Key,
                    Label = bin.Label,
                    DRepMm = bin.DRepMm,
                    Fraction = fraction,
                    VsFps = sv.VsFps,
                    Regime = sv.Regime,
                    Eta = eta,
                    Contribution = contribution,
                });
            }

            var result = new CampEfficiencyResult
            {
                OverallEfficiencyPct = overall * 100.0,
                OverflowVelocityFps = voFps,
                Bins = bins,
                PsdSource = psd.Source,
            };

            result.Steps.Add(new CalcStep("Vo", voFps, "ft/s", $"Q/As = {designFlowCfs:0.####}/{surfaceAreaSf:0.####}"));
            foreach (CampBinResult b in bins.Where(b => b.Fraction > 0))
            {
                result.Steps.Add(new CalcStep($"Vs({b.Key})", b.VsFps, "ft/s",
                    $"{b.Label} d={b.DRepMm:0.####} mm ({b.Regime})"));
                result.Steps.Add(new CalcStep($"eta({b.Key})", b.Eta * 100.0, "%",
                    $"min(1, Vs/Vo) f={b.Fraction:0.###}"));
            }
            result.Steps.Add(new CalcStep("eta_total", result.OverallEfficiencyPct, "%", "sum(f_i*eta_i)"));

            return result;
        }

        /// <summary>Weighted 3-bin trap efficiency for a BMP structure type.</summary>
        public static WeightedTrapResult WeightedTrapEfficiency(string structureKey, ThreeBinPsd psd)
        {
            if (psd == null) throw new ArgumentNullException(nameof(psd));
            if (!SedimentBmpCatalog.TryGet(structureKey, out SedimentBmpCatalog.BmpDefinition bmp))
                throw new ArgumentException($"Unknown BMP type '{structureKey}'.", nameof(structureKey));

            double eClay = bmp.TrappingEfficiency.Clay;
            double eSilt = bmp.TrappingEfficiency.Silt;
            double eSand = bmp.TrappingEfficiency.Sand;
            double overall = psd.Clay * eClay + psd.Silt * eSilt + psd.Sand * eSand;

            var result = new WeightedTrapResult
            {
                StructureName = bmp.Name,
                OverallEfficiencyPct = overall * 100.0,
                ClayRemovalPct = eClay * 100.0,
                SiltRemovalPct = eSilt * 100.0,
                SandRemovalPct = eSand * 100.0,
            };

            result.Steps.Add(new CalcStep("E", result.OverallEfficiencyPct, "%",
                $"f_clay*eta_clay + f_silt*eta_silt + f_sand*eta_sand = " +
                $"({psd.Clay:0.##})({eClay * 100:0}%) + ({psd.Silt:0.##})({eSilt * 100:0}%) + ({psd.Sand:0.##})({eSand * 100:0}%)"));

            return result;
        }

        private static double WaterKinematicViscosity(double waterTempC)
        {
            if (waterTempC <= 5.5) return 1.519e-6;
            if (waterTempC <= 10.5) return 1.307e-6;
            if (waterTempC <= 17.5) return 1.139e-6;
            if (waterTempC <= 22.5) return 1.004e-6;
            return 0.893e-6;
        }
    }
}