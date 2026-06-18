using System;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Manning's equation for rectangular box (closed-bottom) gravity conduits, US customary units.
    ///
    ///     Q = (1.486 / n) * A * R^(2/3) * S^(1/2)
    ///
    /// Partial flow: A = B*y, P = B + 2*y, R = A/P (vertical sides, horizontal invert).
    /// Unlike circular pipes, peak open-channel capacity occurs at just-full depth (y = H).
    /// </summary>
    public static class BoxConduit
    {
        /// <summary>Manning unit conversion constant for US units (ft, cfs).</summary>
        public const double Kn = 1.486;

        public sealed class CapacityResult : TracedResult
        {
            /// <summary>Full-barrel (just-full) capacity, cfs.</summary>
            public double FullFlowCfs { get; set; }

            /// <summary>Full-barrel velocity, ft/s.</summary>
            public double FullVelocityFps { get; set; }

            /// <summary>
            /// Peak open-channel capacity, cfs. For rectangular boxes this equals
            /// <see cref="FullFlowCfs"/> (no supra-full peak like circular pipes).
            /// </summary>
            public double PeakFlowCfs { get; set; }
        }

        public sealed class NormalDepthResult : TracedResult
        {
            /// <summary>Computed normal depth, ft. Equals height when surcharged.</summary>
            public double DepthFt { get; set; }

            /// <summary>Depth as a fraction of barrel height (y/H).</summary>
            public double RelativeDepth { get; set; }

            /// <summary>Average velocity at normal depth, ft/s.</summary>
            public double VelocityFps { get; set; }

            /// <summary>True when design flow exceeds just-full open-channel capacity.</summary>
            public bool Surcharged { get; set; }
        }

        /// <summary>
        /// Partial-flow area and hydraulic radius for a rectangular box at depth
        /// <paramref name="depthFt"/> (ft). Returns (0, 0) when depth &lt;= 0.
        /// </summary>
        public static (double AreaFt2, double HydRadiusFt) PartialFlowGeometry(
            double widthFt, double heightFt, double depthFt)
        {
            if (widthFt <= 0) throw new ArgumentOutOfRangeException(nameof(widthFt));
            if (heightFt <= 0) throw new ArgumentOutOfRangeException(nameof(heightFt));
            if (depthFt <= 0) return (0.0, 0.0);
            if (depthFt > heightFt) depthFt = heightFt;

            double area = widthFt * depthFt;
            double perimeter = widthFt + 2.0 * depthFt;
            if (perimeter <= 0) return (0.0, 0.0);
            return (area, area / perimeter);
        }

        /// <summary>Flow (cfs) at depth <paramref name="depthFt"/> (ft) in a rectangular box.</summary>
        public static double FlowAtDepth(
            double widthFt, double heightFt, double depthFt, double n, double slope)
        {
            if (widthFt <= 0) throw new ArgumentOutOfRangeException(nameof(widthFt));
            if (heightFt <= 0) throw new ArgumentOutOfRangeException(nameof(heightFt));
            if (n <= 0) throw new ArgumentOutOfRangeException(nameof(n));
            if (slope <= 0) throw new ArgumentOutOfRangeException(nameof(slope));
            if (depthFt <= 0) return 0.0;

            var (area, r) = PartialFlowGeometry(widthFt, heightFt, depthFt);
            if (area <= 0 || r <= 0) return 0.0;
            return (Kn / n) * area * Math.Pow(r, 2.0 / 3.0) * Math.Sqrt(slope);
        }

        /// <summary>Just-full capacity for a box <see cref="PipeSegment"/>.</summary>
        public static CapacityResult Capacity(PipeSegment pipe)
        {
            if (pipe == null) throw new ArgumentNullException(nameof(pipe));
            ValidateBoxPipe(pipe);

            double w = pipe.WidthFt, h = pipe.HeightFt, n = pipe.ManningN, s = pipe.Slope;
            double areaFull = w * h;
            double perimeterFull = w + 2.0 * h;
            double rFull = areaFull / perimeterFull;
            double qFull = (Kn / n) * areaFull * Math.Pow(rFull, 2.0 / 3.0) * Math.Sqrt(s);
            double vFull = qFull / areaFull;

            var result = new CapacityResult
            {
                FullFlowCfs = qFull,
                FullVelocityFps = vFull,
                PeakFlowCfs = qFull,
            };
            result.Steps.Add(new CalcStep("A_full", areaFull, "ft^2", "B*H"));
            result.Steps.Add(new CalcStep("P_full", perimeterFull, "ft", "B+2H"));
            result.Steps.Add(new CalcStep("R_full", rFull, "ft", "A_full/P_full"));
            result.Steps.Add(new CalcStep("Q_full", qFull, "cfs", "(1.486/n)*A*R^(2/3)*S^(1/2)"));
            result.Steps.Add(new CalcStep("V_full", vFull, "ft/s", "Q_full/A_full"));
            result.Steps.Add(new CalcStep("Q_peak", qFull, "cfs", "rectangular box: peak at y=H"));
            return result;
        }

        /// <summary>
        /// Normal depth for a target flow via bisection on depth in (0, H].
        /// Flags surcharge when flow exceeds just-full capacity.
        /// </summary>
        public static NormalDepthResult NormalDepth(PipeSegment pipe, double targetFlowCfs)
        {
            if (pipe == null) throw new ArgumentNullException(nameof(pipe));
            if (targetFlowCfs < 0) throw new ArgumentOutOfRangeException(nameof(targetFlowCfs));
            ValidateBoxPipe(pipe);

            double w = pipe.WidthFt, h = pipe.HeightFt, n = pipe.ManningN, s = pipe.Slope;
            var result = new NormalDepthResult();

            double qFull = FlowAtDepth(w, h, h, n, s);
            if (targetFlowCfs >= qFull)
            {
                result.DepthFt = h;
                result.RelativeDepth = 1.0;
                result.Surcharged = true;
                double areaFull = w * h;
                result.VelocityFps = targetFlowCfs / areaFull;
                result.Steps.Add(new CalcStep("Q_full", qFull, "cfs", "just-full open-channel capacity"));
                result.Steps.Add(new CalcStep("Q_design", targetFlowCfs, "cfs", "exceeds Q_full -> SURCHARGED"));
                return result;
            }

            // Manning flow increases monotonically with depth up to y=H for rectangular boxes.
            double lo = 1e-6, hi = h;
            for (int i = 0; i < 200; i++)
            {
                double mid = 0.5 * (lo + hi);
                double q = FlowAtDepth(w, h, mid, n, s);
                if (q < targetFlowCfs) lo = mid; else hi = mid;
                if (hi - lo < 1e-9) break;
            }
            double y = 0.5 * (lo + hi);

            var (area, _) = PartialFlowGeometry(w, h, y);
            double v = area > 0 ? targetFlowCfs / area : 0.0;

            result.DepthFt = y;
            result.RelativeDepth = y / h;
            result.VelocityFps = v;
            result.Surcharged = false;
            result.Steps.Add(new CalcStep("Q_design", targetFlowCfs, "cfs", "input design flow"));
            result.Steps.Add(new CalcStep("y_n", y, "ft", "bisection on Manning Q(y)=Q_design"));
            result.Steps.Add(new CalcStep("y/H", y / h, "-", "relative depth"));
            result.Steps.Add(new CalcStep("V", v, "ft/s", "Q_design/A(y_n)"));
            return result;
        }

        private static void ValidateBoxPipe(PipeSegment pipe)
        {
            if (pipe.WidthFt <= 0)
                throw new ArgumentOutOfRangeException(nameof(pipe), "Box width must be > 0.");
            if (pipe.HeightFt <= 0)
                throw new ArgumentOutOfRangeException(nameof(pipe), "Box height must be > 0.");
            if (pipe.Slope <= 0)
                throw new ArgumentOutOfRangeException(nameof(pipe), "Slope must be > 0.");
        }
    }
}