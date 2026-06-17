using System;
using System.Collections.Generic;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Time-of-concentration methods. Tc feeds the design intensity in the
    /// Rational method (shorter Tc -> higher intensity -> higher peak).
    /// </summary>
    public static class TimeOfConcentration
    {
        public sealed class TcResult : TracedResult
        {
            public double TcMinutes { get; set; }
        }

        /// <summary>
        /// Kirpich (1940), for small natural basins:
        ///     Tc (min) = 0.0078 * L^0.77 * S^(-0.385)
        /// L = flow length (ft), S = average slope (ft/ft).
        /// </summary>
        public static TcResult Kirpich(double lengthFt, double slope)
        {
            if (lengthFt <= 0) throw new ArgumentOutOfRangeException(nameof(lengthFt));
            if (slope <= 0) throw new ArgumentOutOfRangeException(nameof(slope));

            double tc = 0.0078 * Math.Pow(lengthFt, 0.77) * Math.Pow(slope, -0.385);
            var r = new TcResult { TcMinutes = tc };
            r.Steps.Add(new CalcStep("Tc", tc, "min", $"0.0078*L^0.77*S^-0.385  (L={lengthFt:0.#}ft, S={slope:0.####})"));
            return r;
        }

        /// <summary>One reach of the NRCS velocity method.</summary>
        public sealed class TravelReach
        {
            public string Name { get; set; } = "";
            /// <summary>Reach length, ft.</summary>
            public double LengthFt { get; set; }
            /// <summary>Average velocity in the reach, ft/s.</summary>
            public double VelocityFps { get; set; }
        }

        /// <summary>
        /// NRCS velocity method: total travel time is the sum of reach travel
        /// times, Tt = L / V, converted to minutes.
        /// </summary>
        public static TcResult FromReaches(IEnumerable<TravelReach> reaches)
        {
            if (reaches == null) throw new ArgumentNullException(nameof(reaches));

            double totalMin = 0.0;
            var r = new TcResult();
            foreach (var reach in reaches)
            {
                if (reach.LengthFt < 0) throw new ArgumentOutOfRangeException(nameof(reaches), $"Negative length in '{reach.Name}'.");
                if (reach.VelocityFps <= 0) throw new ArgumentOutOfRangeException(nameof(reaches), $"Velocity must be > 0 in '{reach.Name}'.");
                double minutes = reach.LengthFt / reach.VelocityFps / 60.0;
                totalMin += minutes;
                r.Steps.Add(new CalcStep($"Tt[{reach.Name}]", minutes, "min", $"L/V/60 = {reach.LengthFt:0.#}/{reach.VelocityFps:0.##}/60"));
            }
            r.TcMinutes = totalMin;
            r.Steps.Add(new CalcStep("Tc", totalMin, "min", "sum of reach travel times"));
            return r;
        }
    }
}
