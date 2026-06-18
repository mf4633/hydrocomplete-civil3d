using System;
using System.Collections.Generic;

namespace HydroComplete.Engine
{
    /// <summary>
    /// SEDCAD4 sediment/erosion facade. Civil 3D commands use <see cref="Rusle"/>; full site
    /// analysis uses <see cref="Rusle.SoilLoss"/>, <see cref="Musle.SingleStorm"/>, and BMP classes.
    /// </summary>
    public static class SedimentEngine
    {
        public sealed class RusleResult : TracedResult
        {
            public string Name { get; set; } = "";
            public double AreaAcres { get; set; }
            public double RFactor { get; set; }
            public double KFactor { get; set; }
            public double LSFactor { get; set; }
            public double CFactor { get; set; }
            public double PFactor { get; set; }
            public double SoilLossTonsPerAcYr { get; set; }
            public string RiskLevel { get; set; } = "Low";
        }

        public sealed class MusleResult : TracedResult
        {
            public double RunoffVolumeAcFt { get; set; }
            public double PeakFlowCfs { get; set; }
            public double EventSoilLossTons { get; set; }
        }

        /// <summary>
        /// RUSLE: A = R × K × LS × C × P (tons/acre/year). LS uses SEDCAD4 <see cref="Rusle.LsFactor"/>.
        /// </summary>
        public static RusleResult Rusle(
            double areaAcres,
            double slopePercent,
            double lengthFt,
            double runoffC,
            double rFactor,
            double kFactor = 0.32,
            double pFactor = 1.0,
            string name = "")
        {
            if (areaAcres < 0) throw new ArgumentOutOfRangeException(nameof(areaAcres));
            if (slopePercent < 0) throw new ArgumentOutOfRangeException(nameof(slopePercent));
            if (runoffC < 0 || runoffC > 1) throw new ArgumentOutOfRangeException(nameof(runoffC));

            double ls = RusleAnalysis.LsFactor(Math.Max(lengthFt, 10.0), slopePercent);
            double c = Math.Min(1.0, Math.Max(0.001, runoffC));
            double a = rFactor * kFactor * ls * c * pFactor;

            var result = new RusleResult
            {
                Name = name,
                AreaAcres = areaAcres,
                RFactor = rFactor,
                KFactor = kFactor,
                LSFactor = ls,
                CFactor = c,
                PFactor = pFactor,
                SoilLossTonsPerAcYr = a,
                RiskLevel = ClassifyRisk(a),
            };

            result.Steps.Add(new CalcStep("R", rFactor, "", "rainfall erosivity"));
            result.Steps.Add(new CalcStep("K", kFactor, "", "soil erodibility"));
            result.Steps.Add(new CalcStep("LS", ls, "", "slope length/steepness (SEDCAD4)"));
            result.Steps.Add(new CalcStep("C", c, "", "cover factor (from runoff C)"));
            result.Steps.Add(new CalcStep("P", pFactor, "", "support practice"));
            result.Steps.Add(new CalcStep("A", a, "tons/ac/yr", "R*K*LS*C*P"));

            return result;
        }

        /// <summary>
        /// MUSLE event yield: Y = 95 × (Q × qp)^0.56 × K × LS × C × P (Williams 1975).
        /// </summary>
        public static MusleResult MusleEvent(
            double runoffVolumeAcFt,
            double peakFlowCfs,
            double slopePercent,
            double lengthFt,
            double runoffC,
            double kFactor = 0.32,
            double pFactor = 1.0)
        {
            if (peakFlowCfs < 0) throw new ArgumentOutOfRangeException(nameof(peakFlowCfs));
            if (runoffVolumeAcFt < 0) throw new ArgumentOutOfRangeException(nameof(runoffVolumeAcFt));

            double ls = RusleAnalysis.LsFactor(Math.Max(lengthFt, 10.0), slopePercent);
            double c = Math.Min(1.0, Math.Max(0.001, runoffC));
            var musle = Musle.SingleStorm(runoffVolumeAcFt, peakFlowCfs, kFactor, ls, c, pFactor);

            var result = new MusleResult
            {
                RunoffVolumeAcFt = musle.RunoffVolumeAcFt,
                PeakFlowCfs = musle.PeakFlowCfs,
                EventSoilLossTons = musle.SedimentYieldTons,
            };
            foreach (CalcStep step in musle.Steps)
                result.Steps.Add(step);

            return result;
        }

        /// <summary>Area-weighted average soil loss over catchments.</summary>
        public static double WeightedAverageSoilLoss(IEnumerable<RusleResult> results)
        {
            double sumAa = 0.0;
            double sumA = 0.0;
            foreach (RusleResult r in results)
            {
                sumAa += r.SoilLossTonsPerAcYr * r.AreaAcres;
                sumA += r.AreaAcres;
            }

            return sumA > 0 ? sumAa / sumA : 0.0;
        }

        private static string ClassifyRisk(double soilLossTonsPerAcYr)
        {
            if (soilLossTonsPerAcYr > 10.0) return "High";
            if (soilLossTonsPerAcYr > 5.0) return "Moderate";
            return "Low";
        }
    }
}