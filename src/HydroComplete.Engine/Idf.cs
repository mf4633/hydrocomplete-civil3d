using System;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Rainfall intensity from a fitted Intensity-Duration-Frequency (IDF) curve.
    ///
    /// Uses the common three-parameter form (Steel / DOT tables):
    ///     i = a / (t + b)^c
    /// where t is the storm duration in minutes (typically the time of
    /// concentration), and i is intensity in in/hr for a given return period.
    /// The (a, b, c) triple is specific to a location and return period and is
    /// supplied by the caller (in production, looked up from NOAA Atlas 14).
    /// </summary>
    public sealed class IdfCurve
    {
        public IdfCurve(double a, double b, double c, double minDurationMin = 5.0)
        {
            if (minDurationMin <= 0) throw new ArgumentOutOfRangeException(nameof(minDurationMin));
            A = a;
            B = b;
            C = c;
            MinDurationMin = minDurationMin;
        }

        public double A { get; }
        public double B { get; }
        public double C { get; }

        /// <summary>
        /// IDF curves are not valid below a few minutes; the duration is floored
        /// here so a tiny Tc cannot produce an unrealistic intensity.
        /// </summary>
        public double MinDurationMin { get; }

        public sealed class IntensityResult : TracedResult
        {
            public double IntensityInHr { get; set; }
            /// <summary>Duration actually used after applying the minimum floor, minutes.</summary>
            public double DurationMin { get; set; }
        }

        /// <summary>Design intensity (in/hr) for a storm duration in minutes.</summary>
        public IntensityResult Intensity(double durationMin)
        {
            if (durationMin < 0) throw new ArgumentOutOfRangeException(nameof(durationMin));
            double t = Math.Max(durationMin, MinDurationMin);
            double i = A / Math.Pow(t + B, C);

            var r = new IntensityResult { IntensityInHr = i, DurationMin = t };
            if (t > durationMin)
                r.Steps.Add(new CalcStep("t", t, "min", $"floored from {durationMin:0.#} to min duration {MinDurationMin:0.#}"));
            r.Steps.Add(new CalcStep("i", i, "in/hr", $"a/(t+b)^c = {A:0.##}/({t:0.#}+{B:0.##})^{C:0.###}"));
            return r;
        }
    }
}
