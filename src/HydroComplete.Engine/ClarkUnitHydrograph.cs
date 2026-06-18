using System;
using System.Collections.Generic;
using System.Linq;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Clark (1945) instantaneous unit hydrograph with time-area translation and
    /// linear reservoir storage coefficient R.
    ///
    ///     R = StorageFactor · T<sub>c</sub>
    ///     O<sub>i</sub> = C₁(I<sub>i</sub> + I<sub>i-1</sub>) + C₂·O<sub>i-1</sub>
    ///     C₁ = Δt / (2R + Δt),  C₂ = (2R - Δt) / (2R + Δt)
    /// </summary>
    public static class ClarkUnitHydrograph
    {
        public const double DefaultStorageFactor = 0.4;

        public sealed class HydrographOrdinate
        {
            public double TimeMinutes { get; set; }
            public double TranslatedFlowCfs { get; set; }
            public double FlowCfs { get; set; }
        }

        public sealed class UnitHydrographResult : TracedResult
        {
            public double AreaAcres { get; set; }
            public double TcMinutes { get; set; }
            public double StorageCoefficientMinutes { get; set; }
            public double TimestepMinutes { get; set; }
            public double PeakFlowCfs { get; set; }
            public double TimeToPeakMinutes { get; set; }
            public List<HydrographOrdinate> Ordinates { get; } = new List<HydrographOrdinate>();
        }

        /// <summary>Clark storage coefficient R (minutes).</summary>
        public static double StorageCoefficientMinutes(double tcMinutes, double storageFactor = DefaultStorageFactor)
        {
            if (tcMinutes <= 0) throw new ArgumentOutOfRangeException(nameof(tcMinutes));
            if (storageFactor <= 0) throw new ArgumentOutOfRangeException(nameof(storageFactor));
            return storageFactor * tcMinutes;
        }

        /// <summary>Normalized trapezoidal time-area histogram.</summary>
        public static IReadOnlyList<double> TimeAreaHistogram(int numSteps)
        {
            if (numSteps <= 0) throw new ArgumentOutOfRangeException(nameof(numSteps));

            var hist = new double[numSteps];
            for (int i = 0; i < numSteps; i++)
            {
                double ratio = (double)i / numSteps;
                hist[i] = ratio <= 0.5 ? 2.0 * ratio : 2.0 * (1.0 - ratio);
            }

            double sum = hist.Sum();
            if (sum <= 0) return hist;
            for (int i = 0; i < numSteps; i++)
                hist[i] /= sum;
            return hist;
        }

        /// <summary>
        /// Generate a Clark unit hydrograph (1 in direct runoff) and route through storage R.
        /// </summary>
        public static UnitHydrographResult Generate(
            double areaAcres,
            double tcMinutes,
            double timestepMinutes = 15.0,
            double storageFactor = DefaultStorageFactor,
            int? totalSteps = null)
        {
            if (areaAcres <= 0) throw new ArgumentOutOfRangeException(nameof(areaAcres));
            if (tcMinutes <= 0) throw new ArgumentOutOfRangeException(nameof(tcMinutes));
            if (timestepMinutes <= 0) throw new ArgumentOutOfRangeException(nameof(timestepMinutes));

            double rMin = StorageCoefficientMinutes(tcMinutes, storageFactor);
            int translationSteps = (int)Math.Ceiling(tcMinutes / timestepMinutes);
            int nSteps = totalSteps ?? Math.Max(96, translationSteps + (int)Math.Ceiling(5.0 * rMin / timestepMinutes));

            IReadOnlyList<double> timeArea = TimeAreaHistogram(translationSteps);
            double c1 = timestepMinutes / (2.0 * rMin + timestepMinutes);
            double c2 = (2.0 * rMin - timestepMinutes) / (2.0 * rMin + timestepMinutes);

            var translated = new double[nSteps];
            for (int i = 0; i < translationSteps && i < nSteps; i++)
                translated[i] = timeArea[i] * areaAcres;

            var routed = new double[nSteps];
            for (int i = 1; i < nSteps; i++)
            {
                routed[i] = c1 * (translated[i] + translated[i - 1]) + c2 * routed[i - 1];
            }

            var result = new UnitHydrographResult
            {
                AreaAcres = areaAcres,
                TcMinutes = tcMinutes,
                StorageCoefficientMinutes = rMin,
                TimestepMinutes = timestepMinutes,
            };

            result.Steps.Add(new CalcStep("Tc", tcMinutes, "min", "time of concentration"));
            result.Steps.Add(new CalcStep("R", rMin, "min", $"{storageFactor}*Tc"));
            result.Steps.Add(new CalcStep("C1", c1, "-", "dt/(2R+dt)"));
            result.Steps.Add(new CalcStep("C2", c2, "-", "(2R-dt)/(2R+dt)"));

            for (int i = 0; i < nSteps; i++)
            {
                result.Ordinates.Add(new HydrographOrdinate
                {
                    TimeMinutes = i * timestepMinutes,
                    TranslatedFlowCfs = translated[i],
                    FlowCfs = routed[i],
                });
            }

            var peak = result.Ordinates.OrderByDescending(o => o.FlowCfs).First();
            result.PeakFlowCfs = peak.FlowCfs;
            result.TimeToPeakMinutes = peak.TimeMinutes;
            result.Steps.Add(new CalcStep("qp", result.PeakFlowCfs, "cfs", "routed peak (1 in runoff)"));
            return result;
        }
    }
}