using System;
using System.Collections.Generic;
using System.Linq;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Modified Universal Soil Loss Equation (MUSLE): single-storm sediment yield.
    /// Y = 95 × (Q × qp)^0.56 × K × LS × C × P (Williams 1975).
    /// </summary>
    public static class Musle
    {
        public sealed class StormYieldResult : TracedResult
        {
            public double SedimentYieldTons { get; set; }
            public double RunoffVolumeAcFt { get; set; }
            public double PeakFlowCfs { get; set; }
            public double Qqp { get; set; }
            public double HydrologicFactor { get; set; }
            public double K { get; set; }
            public double Ls { get; set; }
            public double C { get; set; }
            public double P { get; set; }
        }

        public sealed class StormSequenceResult : TracedResult
        {
            public IReadOnlyList<StormEventResult> Storms { get; set; } = Array.Empty<StormEventResult>();
            public double AnnualExpectedTons { get; set; }
            public double K { get; set; }
            public double Ls { get; set; }
            public double C { get; set; }
            public double P { get; set; }
        }

        public sealed class StormEventResult
        {
            public string ReturnPeriod { get; set; } = "";
            public double RainfallIn { get; set; }
            public double RunoffDepthIn { get; set; }
            public double RunoffVolumeAcFt { get; set; }
            public double PeakFlowCfs { get; set; }
            public double SedimentYieldTons { get; set; }
        }

        public sealed class WatershedInput
        {
            public double AreaAcres { get; set; } = 1.0;
            public double CurveNumber { get; set; } = 75.0;
            public double TimeOfConcentrationHr { get; set; } = 0.3;
            public double SlopeLengthFt { get; set; } = 200.0;
            public double SlopePercent { get; set; } = 5.0;
            public string SoilType { get; set; } = "loam";
            public string CoverType { get; set; } = "construction-site";
            public string PracticeType { get; set; } = "none";
            public double? KFactor { get; set; }
            public double? C { get; set; }
            public double? P { get; set; }
        }

        public static StormYieldResult SingleStorm(
            double runoffVolumeAcFt,
            double peakFlowCfs,
            double k,
            double ls,
            double c,
            double p)
        {
            if (runoffVolumeAcFt < 0) throw new ArgumentOutOfRangeException(nameof(runoffVolumeAcFt));
            if (peakFlowCfs < 0) throw new ArgumentOutOfRangeException(nameof(peakFlowCfs));

            double qqp = runoffVolumeAcFt * peakFlowCfs;
            double hydrologicFactor = qqp > 0 ? 95.0 * Math.Pow(qqp, 0.56) : 0.0;
            double sedimentYield = hydrologicFactor * k * ls * c * p;

            var result = new StormYieldResult
            {
                SedimentYieldTons = sedimentYield,
                RunoffVolumeAcFt = runoffVolumeAcFt,
                PeakFlowCfs = peakFlowCfs,
                Qqp = qqp,
                HydrologicFactor = hydrologicFactor,
                K = k,
                Ls = ls,
                C = c,
                P = p,
            };

            result.Steps.Add(new CalcStep("Q", runoffVolumeAcFt, "ac-ft", "Storm runoff volume"));
            result.Steps.Add(new CalcStep("qp", peakFlowCfs, "cfs", "Peak discharge"));
            result.Steps.Add(new CalcStep("Q*qp", qqp, "ac-ft*cfs", "Q*qp"));
            result.Steps.Add(new CalcStep("95*(Q*qp)^0.56", hydrologicFactor, "", $"95*({qqp:0.####})^0.56"));
            result.Steps.Add(new CalcStep("Y", sedimentYield, "tons",
                $"95*(Q*qp)^0.56*K*LS*C*P = {hydrologicFactor:0.##}*{k:0.###}*{ls:0.##}*{c:0.###}*{p:0.##}"));

            return result;
        }

        public static StormSequenceResult StormSequence(WatershedInput watershed, IReadOnlyDictionary<string, double> storms)
        {
            if (watershed == null) throw new ArgumentNullException(nameof(watershed));
            if (storms == null) throw new ArgumentNullException(nameof(storms));

            double k = watershed.KFactor
                ?? (RusleAnalysis.SoilErodibility.TryGetValue(watershed.SoilType, out double kv) ? kv : 0.38);
            double ls = RusleAnalysis.LsFactor(watershed.SlopeLengthFt, watershed.SlopePercent);
            double c = watershed.C
                ?? (RusleAnalysis.CoverManagement.TryGetValue(watershed.CoverType, out double cv) ? cv : 0.90);
            double p = watershed.P
                ?? (RusleAnalysis.SupportPractice.TryGetValue(watershed.PracticeType, out double pv) ? pv : 1.00);

            double s = 1000.0 / watershed.CurveNumber - 10.0;
            double ia = 0.2 * s;

            var entries = storms
                .Where(kv => !double.IsNaN(kv.Value) && !double.IsInfinity(kv.Value))
                .OrderBy(kv => ParseReturnPeriod(kv.Key))
                .ToList();

            var stormResults = new List<StormEventResult>();
            foreach (var entry in entries)
            {
                double rainfallIn = entry.Value;
                double runoffDepthIn = rainfallIn > ia
                    ? Math.Pow(rainfallIn - ia, 2) / (rainfallIn - ia + s)
                    : 0.0;
                double runoffVolumeAcFt = runoffDepthIn * watershed.AreaAcres / 12.0;

                double tc = Math.Max(0.1, Math.Min(10.0, watershed.TimeOfConcentrationHr));
                double logTc = Math.Log10(tc);
                const double c0 = 2.55323, c1 = -0.61512, c2 = -0.16403;
                double qu = Math.Pow(10.0, c0 + c1 * logTc + c2 * logTc * logTc);
                double areaSqMi = watershed.AreaAcres / 640.0;
                double qp = qu * areaSqMi * runoffDepthIn;

                double yieldTons = 0.0;
                if (runoffVolumeAcFt > 0 && qp > 0)
                    yieldTons = 95.0 * Math.Pow(runoffVolumeAcFt * qp, 0.56) * k * ls * c * p;

                stormResults.Add(new StormEventResult
                {
                    ReturnPeriod = entry.Key,
                    RainfallIn = rainfallIn,
                    RunoffDepthIn = runoffDepthIn,
                    RunoffVolumeAcFt = runoffVolumeAcFt,
                    PeakFlowCfs = qp,
                    SedimentYieldTons = yieldTons,
                });
            }

            double annualExpected = 0.0;
            for (int i = 0; i < stormResults.Count; i++)
            {
                int tCurrent = ParseReturnPeriod(stormResults[i].ReturnPeriod);
                int? tNext = i < stormResults.Count - 1
                    ? ParseReturnPeriod(stormResults[i + 1].ReturnPeriod)
                    : (int?)null;
                double pExceed = tNext.HasValue
                    ? 1.0 / tCurrent - 1.0 / tNext.Value
                    : 1.0 / tCurrent;
                annualExpected += pExceed * stormResults[i].SedimentYieldTons;
            }

            var result = new StormSequenceResult
            {
                Storms = stormResults,
                AnnualExpectedTons = annualExpected,
                K = k,
                Ls = ls,
                C = c,
                P = p,
            };

            result.Steps.Add(new CalcStep("K", k, "", "Soil erodibility"));
            result.Steps.Add(new CalcStep("LS", ls, "", "Slope length-steepness"));
            result.Steps.Add(new CalcStep("C", c, "", "Cover management"));
            result.Steps.Add(new CalcStep("P", p, "", "Support practice"));
            result.Steps.Add(new CalcStep("E[Y/yr]", annualExpected, "tons/yr", "Partial frequency integration over storm suite"));

            return result;
        }

        private static int ParseReturnPeriod(string key)
        {
            if (string.IsNullOrEmpty(key)) return 999;
            for (int i = 0; i < key.Length; i++)
            {
                if (char.IsDigit(key[i]))
                {
                    int start = i;
                    while (i < key.Length && char.IsDigit(key[i])) i++;
                    if (int.TryParse(key.Substring(start, i - start), out int value))
                        return value;
                }
            }
            return 999;
        }
    }
}