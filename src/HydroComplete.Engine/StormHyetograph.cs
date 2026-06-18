using System;
using System.Collections.Generic;
using System.Linq;

namespace HydroComplete.Engine
{
    /// <summary>NRCS 24-hour rainfall distributions (Type II default).</summary>
    public static class StormHyetograph
    {
        public sealed class DistributionIncrement
        {
            public double StartTimeHours { get; set; }
            public double EndTimeHours { get; set; }
            public double Fraction { get; set; }
            public double IntensityPerHour { get; set; }
            public double DepthIn { get; set; }
        }

        public sealed class HyetographResult : TracedResult
        {
            public string DistributionName { get; set; } = "";
            public double TotalDepthIn { get; set; }
            public double DurationHours { get; set; }
            public List<DistributionIncrement> Increments { get; } = new List<DistributionIncrement>();
        }

        public static IReadOnlyList<(double Hour, double Fraction)> TypeIICumulativeFractions { get; } =
            new (double, double)[]
            {
                (0.0, 0.000), (2.0, 0.022), (4.0, 0.048), (6.0, 0.080), (7.0, 0.098),
                (8.0, 0.120), (8.5, 0.133), (9.0, 0.147), (9.5, 0.163), (9.75, 0.172),
                (10.0, 0.181), (10.5, 0.204), (11.0, 0.235), (11.5, 0.283), (11.75, 0.357),
                (12.0, 0.663), (12.5, 0.735), (13.0, 0.772), (13.5, 0.799), (14.0, 0.820),
                (16.0, 0.880), (20.0, 0.952), (24.0, 1.000),
            };

        public static HyetographResult TypeII(double totalDepthIn = 1.0, double durationHours = 24.0)
        {
            if (totalDepthIn < 0) throw new ArgumentOutOfRangeException(nameof(totalDepthIn));
            if (durationHours <= 0) throw new ArgumentOutOfRangeException(nameof(durationHours));
            return BuildFromCumulative(TypeIICumulativeFractions, "NRCS Type II", totalDepthIn, durationHours);
        }

        public static HyetographResult TypeIIUniform(
            double totalDepthIn,
            double durationHours = 24.0,
            double timestepHours = 0.1)
        {
            if (timestepHours <= 0) throw new ArgumentOutOfRangeException(nameof(timestepHours));

            var result = new HyetographResult
            {
                DistributionName = "NRCS Type II (uniform timestep)",
                TotalDepthIn = totalDepthIn,
                DurationHours = durationHours,
            };

            int nSteps = (int)Math.Ceiling(durationHours / timestepHours);
            double prevFrac = 0.0;

            for (int i = 1; i <= nSteps; i++)
            {
                double t = Math.Min(i * timestepHours, durationHours);
                double cumFrac = InterpolateCumulativeFraction(TypeIICumulativeFractions, t, durationHours);
                double incFrac = cumFrac - prevFrac;

                result.Increments.Add(new DistributionIncrement
                {
                    StartTimeHours = t - timestepHours,
                    EndTimeHours = t,
                    Fraction = incFrac,
                    IntensityPerHour = incFrac / timestepHours,
                    DepthIn = incFrac * totalDepthIn,
                });
                prevFrac = cumFrac;
            }

            result.Steps.Add(new CalcStep("sum_fractions", result.Increments.Sum(x => x.Fraction), "-", "incremental fractions"));
            return result;
        }

        private static HyetographResult BuildFromCumulative(
            IReadOnlyList<(double Hour, double Fraction)> cumulative,
            string name,
            double totalDepthIn,
            double durationHours)
        {
            var result = new HyetographResult
            {
                DistributionName = name,
                TotalDepthIn = totalDepthIn,
                DurationHours = durationHours,
            };

            for (int i = 1; i < cumulative.Count; i++)
            {
                double startHr = cumulative[i - 1].Hour;
                double endHr = cumulative[i].Hour;
                double frac = cumulative[i].Fraction - cumulative[i - 1].Fraction;
                double duration = endHr - startHr;

                result.Increments.Add(new DistributionIncrement
                {
                    StartTimeHours = startHr,
                    EndTimeHours = endHr,
                    Fraction = frac,
                    IntensityPerHour = duration > 0 ? frac / duration : 0.0,
                    DepthIn = frac * totalDepthIn,
                });
            }

            result.Steps.Add(new CalcStep("sum_fractions", result.Increments.Sum(x => x.Fraction), "-", "incremental fractions"));
            return result;
        }

        private static double InterpolateCumulativeFraction(
            IReadOnlyList<(double Hour, double Fraction)> cumulative,
            double timeHours,
            double durationHours)
        {
            double t = Math.Max(0.0, Math.Min(timeHours, durationHours));
            if (t <= cumulative[0].Hour) return 0.0;
            if (t >= cumulative[cumulative.Count - 1].Hour) return 1.0;

            for (int i = 1; i < cumulative.Count; i++)
            {
                if (t <= cumulative[i].Hour)
                {
                    double t0 = cumulative[i - 1].Hour;
                    double t1 = cumulative[i].Hour;
                    double f0 = cumulative[i - 1].Fraction;
                    double f1 = cumulative[i].Fraction;
                    double w = t1 > t0 ? (t - t0) / (t1 - t0) : 0.0;
                    return f0 + w * (f1 - f0);
                }
            }

            return 1.0;
        }
    }
}