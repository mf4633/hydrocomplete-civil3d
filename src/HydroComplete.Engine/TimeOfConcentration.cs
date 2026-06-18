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

        /// <summary>Surface type for TR-55 shallow concentrated flow (Figure 3-1).</summary>
        public enum ShallowSurfaceType
        {
            Unpaved,
            Paved,
        }

        /// <summary>One segment of an NRCS TR-55 segmented flow path.</summary>
        public sealed class TcSegment
        {
            public string Name { get; set; } = "";

            /// <summary>Segment type: sheet, shallow, or channel.</summary>
            public string Type { get; set; } = "sheet";

            /// <summary>Flow-path length, ft.</summary>
            public double LengthFt { get; set; }

            /// <summary>Land or channel slope, ft/ft.</summary>
            public double Slope { get; set; } = 0.05;

            /// <summary>Manning n for sheet or channel segments.</summary>
            public double ManningN { get; set; } = 0.40;

            /// <summary>2-year 24-hour rainfall P₂, inches (sheet flow).</summary>
            public double Rainfall2YearIn { get; set; } = 3.0;

            /// <summary>Shallow concentrated surface (paved / unpaved).</summary>
            public ShallowSurfaceType SurfaceType { get; set; } = ShallowSurfaceType.Unpaved;

            /// <summary>Channel bottom width, ft (channel segments).</summary>
            public double BottomWidthFt { get; set; } = 4.0;

            /// <summary>Channel side slope z:1 (channel segments).</summary>
            public double SideSlopeZ { get; set; } = 3.0;

            /// <summary>Channel flow depth, ft (channel segments).</summary>
            public double DepthFt { get; set; } = 1.0;
        }

        /// <summary>
        /// TR-55 sheet-flow travel time, Equation 3-3:
        ///     Tt (hr) = 0.007 * (nL)^0.8 / (P₂^0.5 * S^0.4)
        /// L is capped at 100 ft per TR-55.
        /// </summary>
        public static TcResult SheetFlow(
            double manningN,
            double lengthFt,
            double rainfall2YearIn,
            double slope)
        {
            if (manningN <= 0) throw new ArgumentOutOfRangeException(nameof(manningN));
            if (lengthFt <= 0) throw new ArgumentOutOfRangeException(nameof(lengthFt));
            if (rainfall2YearIn <= 0) throw new ArgumentOutOfRangeException(nameof(rainfall2YearIn));
            if (slope <= 0) throw new ArgumentOutOfRangeException(nameof(slope));

            double l = Math.Min(lengthFt, 100.0);
            double ttHr = 0.007 * Math.Pow(manningN * l, 0.8)
                / (Math.Pow(rainfall2YearIn, 0.5) * Math.Pow(slope, 0.4));

            var r = new TcResult { TcMinutes = ttHr * 60.0 };
            r.Steps.Add(new CalcStep(
                "Tt_sheet",
                ttHr,
                "hr",
                $"0.007*(nL)^0.8/(P2^0.5*S^0.4)  (L={l:0.#}ft, n={manningN:0.##}, P2={rainfall2YearIn:0.##}in)"));
            return r;
        }

        /// <summary>
        /// TR-55 shallow concentrated flow (Figure 3-1):
        ///     V = k * S^0.5,  Tt = L / (3600 * V)
        /// k = 16.1345 (unpaved) or 20.3282 (paved).
        /// </summary>
        public static TcResult ShallowConcentrated(
            double lengthFt,
            double slope,
            ShallowSurfaceType surfaceType = ShallowSurfaceType.Unpaved)
        {
            if (lengthFt <= 0) throw new ArgumentOutOfRangeException(nameof(lengthFt));
            if (slope <= 0) throw new ArgumentOutOfRangeException(nameof(slope));

            double k = surfaceType == ShallowSurfaceType.Paved ? 20.3282 : 16.1345;
            double velocity = k * Math.Pow(slope, 0.5);
            double ttHr = lengthFt / velocity / 3600.0;

            var r = new TcResult { TcMinutes = ttHr * 60.0 };
            r.Steps.Add(new CalcStep(
                "Tt_shallow",
                ttHr,
                "hr",
                $"L/(3600*k*S^0.5)  (L={lengthFt:0.#}ft, k={k:0.####}, S={slope:0.####})"));
            return r;
        }

        /// <summary>
        /// Composite Tc from TR-55 sheet, shallow concentrated, and channel segments.
        /// Channel segments use Manning velocity at the given depth.
        /// </summary>
        public static TcResult FromTr55Segments(IEnumerable<TcSegment> segments)
        {
            if (segments == null) throw new ArgumentNullException(nameof(segments));

            double totalMin = 0.0;
            var r = new TcResult();

            foreach (var seg in segments)
            {
                string type = (seg.Type ?? "sheet").Trim().ToLowerInvariant();
                TcResult segResult;

                if (type == "sheet")
                {
                    segResult = SheetFlow(seg.ManningN, seg.LengthFt, seg.Rainfall2YearIn, seg.Slope);
                }
                else if (type == "shallow")
                {
                    segResult = ShallowConcentrated(seg.LengthFt, seg.Slope, seg.SurfaceType);
                }
                else if (type == "channel")
                {
                    var flow = ChannelHydraulics.FlowAtDepth(
                        seg.BottomWidthFt,
                        seg.SideSlopeZ,
                        seg.DepthFt,
                        seg.ManningN,
                        seg.Slope);
                    if (flow.VelocityFps <= 0)
                        throw new ArgumentOutOfRangeException(nameof(segments), $"Zero velocity in channel segment '{seg.Name}'.");

                    double minutes = seg.LengthFt / flow.VelocityFps / 60.0;
                    segResult = new TcResult { TcMinutes = minutes };
                    segResult.Steps.Add(new CalcStep(
                        $"Tt[{seg.Name}]",
                        minutes,
                        "min",
                        $"L/V/60  (V={flow.VelocityFps:0.##} ft/s)"));
                }
                else
                {
                    throw new ArgumentOutOfRangeException(nameof(segments), $"Unknown segment type '{seg.Type}'.");
                }

                totalMin += segResult.TcMinutes;
                foreach (var step in segResult.Steps)
                    r.Steps.Add(step);
            }

            r.TcMinutes = totalMin;
            r.Steps.Add(new CalcStep("Tc", totalMin, "min", "sum of TR-55 segment travel times"));
            return r;
        }

        /// <summary>
        /// FAA airport-drainage Tc for a pipe or channel reach (Hydraflow-style).
        /// Overland FAA (LMNO / AC 150/5320-5): Tc = 1.8*(1.1-C)*L^0.5/(100*S)^(1/3) min.
        /// Enclosed reach with hydraulic radius: Tc = 1.8*L^0.5/(100*S)^(1/3)/HR^0.3 min.
        /// L = length (ft), S = slope (ft/ft), HR = hydraulic radius (ft).
        /// </summary>
        public static TcResult Faa(double lengthFt, double slope, double hydraulicRadiusFt)
        {
            if (lengthFt <= 0) throw new ArgumentOutOfRangeException(nameof(lengthFt));
            if (slope <= 0) throw new ArgumentOutOfRangeException(nameof(slope));
            if (hydraulicRadiusFt <= 0) throw new ArgumentOutOfRangeException(nameof(hydraulicRadiusFt));

            double slopeTerm = Math.Pow(100.0 * slope, 1.0 / 3.0);
            double hrTerm = Math.Pow(hydraulicRadiusFt, 0.3);
            double tc = 1.8 * Math.Pow(lengthFt, 0.5) / slopeTerm / hrTerm;

            var r = new TcResult { TcMinutes = tc };
            r.Steps.Add(new CalcStep(
                "Tc",
                tc,
                "min",
                $"FAA reach: 1.8*L^0.5/(100*S)^(1/3)/HR^0.3  " +
                $"(L={lengthFt:0.#}ft, S={slope:0.####}, HR={hydraulicRadiusFt:0.###}ft); " +
                $"overland FAA: 1.8*(1.1-C)*L^0.5/(100*S)^(1/3)"));
            return r;
        }
    }
}
