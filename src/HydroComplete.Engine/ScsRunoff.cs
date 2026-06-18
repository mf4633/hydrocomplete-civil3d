using System;
using System.Collections.Generic;

namespace HydroComplete.Engine
{
    /// <summary>
    /// SCS/TR-55 curve-number runoff: Ia = 0.2S, Q = (P-Ia)^2/(P-Ia+S), S = 1000/CN - 10.
    /// Supports single-storm depth, catchment composite, and incremental hyetograph steps.
    /// </summary>
    public static class ScsRunoff
    {
        /// <summary>Initial-abstraction ratio applied to maximum retention (TR-55).</summary>
        public const double InitialAbstractionRatio = 0.2;

        public sealed class RunoffIncrement
        {
            public int Index { get; set; }
            public double RainfallIn { get; set; }
            public double CumulativeRainfallIn { get; set; }
            public double CumulativeAbstractionIn { get; set; }
            public double CumulativeRunoffIn { get; set; }
            public double IncrementalRunoffIn { get; set; }
        }

        public sealed class IncrementalRunoffResult : TracedResult
        {
            public double CurveNumber { get; set; }
            public double MaxRetentionIn { get; set; }
            public double InitialAbstractionIn { get; set; }
            public double TotalRunoffIn { get; set; }
            public List<RunoffIncrement> Increments { get; } = new List<RunoffIncrement>();
        }

        public sealed class CatchmentRunoffResult : TracedResult
        {
            public string CatchmentName { get; set; } = "";
            public double AreaAcres { get; set; }
            public double CurveNumber { get; set; }
            public double RainfallInches { get; set; }
            public double InitialAbstractionInches { get; set; }
            public double PotentialRetentionInches { get; set; }
            public double RunoffDepthInches { get; set; }
            public double RunoffVolumeCf { get; set; }
            public double RunoffVolumeAcreFt { get; set; }
        }

        public sealed class CompositeRunoffResult : TracedResult
        {
            public double RainfallInches { get; set; }
            public double TotalAreaAcres { get; set; }
            public double WeightedCurveNumber { get; set; }
            public double CompositeRunoffDepthInches { get; set; }
            public double TotalRunoffVolumeCf { get; set; }
            public List<CatchmentRunoffResult> Catchments { get; } = new List<CatchmentRunoffResult>();
        }

        public static double PotentialRetentionInches(double curveNumber)
            => MaxRetentionFromCn(curveNumber);

        public static double MaxRetentionFromCn(double curveNumber)
        {
            if (curveNumber <= 0 || curveNumber > 100)
                throw new ArgumentOutOfRangeException(nameof(curveNumber), "CN must be in (0, 100].");
            return 1000.0 / curveNumber - 10.0;
        }

        public static double InitialAbstractionInches(double curveNumber)
            => InitialAbstractionFromCn(curveNumber);

        public static double InitialAbstractionFromCn(double curveNumber)
            => InitialAbstractionRatio * MaxRetentionFromCn(curveNumber);

        public static double RunoffDepthInches(double rainfallInches, double curveNumber)
            => CumulativeRunoffDepth(rainfallInches, curveNumber);

        public static double CumulativeRunoffDepth(double cumulativeRainfallIn, double curveNumber)
        {
            if (cumulativeRainfallIn < 0)
                throw new ArgumentOutOfRangeException(nameof(cumulativeRainfallIn));

            double s = MaxRetentionFromCn(curveNumber);
            double ia = InitialAbstractionRatio * s;
            if (cumulativeRainfallIn <= ia) return 0.0;

            double pe = cumulativeRainfallIn - ia;
            return pe * pe / (pe + s);
        }

        public static IncrementalRunoffResult ComputeIncremental(
            IReadOnlyList<double> rainfallIncrementsIn,
            double curveNumber)
        {
            if (rainfallIncrementsIn == null)
                throw new ArgumentNullException(nameof(rainfallIncrementsIn));

            double s = MaxRetentionFromCn(curveNumber);
            double ia = InitialAbstractionRatio * s;

            var result = new IncrementalRunoffResult
            {
                CurveNumber = curveNumber,
                MaxRetentionIn = s,
                InitialAbstractionIn = ia,
            };

            double cumulativeRain = 0.0;
            double prevCumulativeRunoff = 0.0;

            for (int i = 0; i < rainfallIncrementsIn.Count; i++)
            {
                double inc = rainfallIncrementsIn[i];
                if (inc < 0)
                    throw new ArgumentOutOfRangeException(nameof(rainfallIncrementsIn), "Rainfall increment must be >= 0.");

                cumulativeRain += inc;
                double cumulativeRunoff = CumulativeRunoffDepth(cumulativeRain, curveNumber);
                double incrementalRunoff = cumulativeRunoff - prevCumulativeRunoff;

                result.Increments.Add(new RunoffIncrement
                {
                    Index = i,
                    RainfallIn = inc,
                    CumulativeRainfallIn = cumulativeRain,
                    CumulativeAbstractionIn = Math.Min(cumulativeRain, ia),
                    CumulativeRunoffIn = cumulativeRunoff,
                    IncrementalRunoffIn = incrementalRunoff,
                });

                prevCumulativeRunoff = cumulativeRunoff;
            }

            result.TotalRunoffIn = prevCumulativeRunoff;
            result.Steps.Add(new CalcStep("S", s, "in", "1000/CN - 10"));
            result.Steps.Add(new CalcStep("Ia", ia, "in", "0.2*S"));
            result.Steps.Add(new CalcStep("Q_total", result.TotalRunoffIn, "in", "sum of incremental runoff"));
            return result;
        }

        public static double CurveNumberFromRunoffC(double runoffC)
        {
            if (runoffC < 0 || runoffC > 1)
                throw new ArgumentOutOfRangeException(nameof(runoffC), "C must be 0..1.");
            if (runoffC <= 0.05) return 55.0;

            double cn = 1000.0 / (10.0 + 17.67 * runoffC);
            return Math.Min(98.0, Math.Max(30.0, cn));
        }

        public static double ResolveCurveNumber(Catchment catchment)
        {
            if (catchment.CurveNumber > 0)
                return catchment.CurveNumber;
            return CurveNumberFromRunoffC(catchment.RunoffC);
        }

        public static double RunoffVolumeCf(double runoffDepthInches, double areaAcres)
            => runoffDepthInches * areaAcres * 3630.0;

        public static CatchmentRunoffResult ComputeCatchment(Catchment catchment, double rainfallInches)
        {
            if (catchment == null) throw new ArgumentNullException(nameof(catchment));
            if (rainfallInches < 0) throw new ArgumentOutOfRangeException(nameof(rainfallInches));

            double cn = ResolveCurveNumber(catchment);
            double s = PotentialRetentionInches(cn);
            double ia = 0.2 * s;
            double depth = RunoffDepthInches(rainfallInches, cn);
            double volumeCf = RunoffVolumeCf(depth, catchment.AreaAcres);

            var result = new CatchmentRunoffResult
            {
                CatchmentName = catchment.Name,
                AreaAcres = catchment.AreaAcres,
                CurveNumber = cn,
                RainfallInches = rainfallInches,
                InitialAbstractionInches = ia,
                PotentialRetentionInches = s,
                RunoffDepthInches = depth,
                RunoffVolumeCf = volumeCf,
                RunoffVolumeAcreFt = volumeCf / 43560.0,
            };

            result.Steps.Add(new CalcStep("CN", cn, "", catchment.CurveNumber > 0
                ? "catchment curve number"
                : $"estimated from C={catchment.RunoffC:0.###}"));
            result.Steps.Add(new CalcStep("S", s, "in", "1000/CN - 10"));
            result.Steps.Add(new CalcStep("Ia", ia, "in", "0.2*S"));
            result.Steps.Add(new CalcStep("P", rainfallInches, "in", "design rainfall"));
            result.Steps.Add(new CalcStep("Q_depth", depth, "in", "(P-Ia)^2/(P-Ia+S)"));
            result.Steps.Add(new CalcStep("Q_vol", volumeCf, "cf", "Q_depth*A*3630"));

            return result;
        }

        public static CompositeRunoffResult ComputeComposite(
            IEnumerable<Catchment> catchments,
            double rainfallInches)
        {
            if (catchments == null) throw new ArgumentNullException(nameof(catchments));
            if (rainfallInches < 0) throw new ArgumentOutOfRangeException(nameof(rainfallInches));

            var composite = new CompositeRunoffResult { RainfallInches = rainfallInches };
            double sumA = 0.0;
            double sumCna = 0.0;
            double sumQa = 0.0;

            foreach (Catchment cm in catchments)
            {
                CatchmentRunoffResult row = ComputeCatchment(cm, rainfallInches);
                composite.Catchments.Add(row);
                sumA += cm.AreaAcres;
                sumCna += row.CurveNumber * cm.AreaAcres;
                sumQa += row.RunoffDepthInches * cm.AreaAcres;
            }

            composite.TotalAreaAcres = sumA;
            composite.WeightedCurveNumber = sumA > 0 ? sumCna / sumA : 0.0;
            composite.CompositeRunoffDepthInches = sumA > 0 ? sumQa / sumA : 0.0;
            composite.TotalRunoffVolumeCf = RunoffVolumeCf(composite.CompositeRunoffDepthInches, sumA);

            composite.Steps.Add(new CalcStep("P", rainfallInches, "in", "design rainfall"));
            composite.Steps.Add(new CalcStep("CN_wtd", composite.WeightedCurveNumber, "", "area-weighted CN"));
            composite.Steps.Add(new CalcStep("Q_depth", composite.CompositeRunoffDepthInches, "in", "area-weighted depth"));
            composite.Steps.Add(new CalcStep("Q_vol", composite.TotalRunoffVolumeCf, "cf", "Q_depth*A*3630"));

            return composite;
        }
    }
}