using System;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Manning's equation for standard pipe-arch gravity conduits, US customary units.
    ///
    ///     Q = (1.486 / n) * A * R^(2/3) * S^(1/2)
    ///
    /// Geometry (AISI / NCSPA pipe-arch intrados, public-domain culvert references):
    ///   Span B = inside width at spring line (ft)
    ///   Rise H = inside height invert to crown (ft)
    ///   Arc radius R = B²/(16H) + H/2
    ///   Spring-line depth y_s = R - sqrt(R² - (B/2)²)
    ///
    /// Partial flow:
    ///   y ≤ y_s — circular segment on intrados (radius R, invert at circle low point)
    ///   y_s &lt; y ≤ H — arc segment plus vertical walls (width B) to water surface
    ///   y = H — full barrel includes flat crown (width B)
    /// </summary>
    public static class ArchConduit
    {
        /// <summary>Manning unit conversion constant for US units (ft, cfs).</summary>
        public const double Kn = 1.486;

        public sealed class CapacityResult : TracedResult
        {
            /// <summary>Full-barrel (just-full) capacity, cfs.</summary>
            public double FullFlowCfs { get; set; }

            /// <summary>Full-barrel velocity, ft/s.</summary>
            public double FullVelocityFps { get; set; }

            /// <summary>Peak open-channel capacity, cfs (scanned over depth).</summary>
            public double PeakFlowCfs { get; set; }
        }

        public sealed class NormalDepthResult : TracedResult
        {
            /// <summary>Computed normal depth, ft. Equals rise when surcharged.</summary>
            public double DepthFt { get; set; }

            /// <summary>Depth as a fraction of barrel rise (y/H).</summary>
            public double RelativeDepth { get; set; }

            /// <summary>Average velocity at normal depth, ft/s.</summary>
            public double VelocityFps { get; set; }

            /// <summary>True when design flow exceeds peak open-channel capacity.</summary>
            public bool Surcharged { get; set; }
        }

        /// <summary>
        /// Intrados arc radius from span and rise:
        /// R = B²/(16H) + H/2.
        /// </summary>
        public static double ArcRadiusFt(double spanFt, double riseFt)
        {
            ValidateSpanRise(spanFt, riseFt);
            return spanFt * spanFt / (16.0 * riseFt) + riseFt / 2.0;
        }

        /// <summary>Depth from invert to spring line (ft).</summary>
        public static double SpringLineDepthFt(double spanFt, double riseFt)
        {
            double r = ArcRadiusFt(spanFt, riseFt);
            double halfSpan = spanFt / 2.0;
            return r - Math.Sqrt(r * r - halfSpan * halfSpan);
        }

        /// <summary>
        /// Partial-flow area and hydraulic radius at depth <paramref name="depthFt"/> (ft).
        /// Returns (0, 0) when depth &lt;= 0.
        /// </summary>
        public static (double AreaFt2, double HydRadiusFt) PartialFlowGeometry(
            double spanFt, double riseFt, double depthFt)
        {
            ValidateSpanRise(spanFt, riseFt);
            if (depthFt <= 0) return (0.0, 0.0);
            if (depthFt > riseFt) depthFt = riseFt;

            double area = WettedAreaAtDepth(spanFt, riseFt, depthFt);
            double perimeter = WettedPerimeterAtDepth(spanFt, riseFt, depthFt);
            if (perimeter <= 0) return (0.0, 0.0);
            return (area, area / perimeter);
        }

        /// <summary>Flow (cfs) at depth <paramref name="depthFt"/> (ft) in a pipe arch.</summary>
        public static double FlowAtDepth(
            double spanFt, double riseFt, double depthFt, double n, double slope)
        {
            ValidateSpanRise(spanFt, riseFt);
            if (n <= 0) throw new ArgumentOutOfRangeException(nameof(n));
            if (slope <= 0) throw new ArgumentOutOfRangeException(nameof(slope));
            if (depthFt <= 0) return 0.0;

            var (area, r) = PartialFlowGeometry(spanFt, riseFt, depthFt);
            if (area <= 0 || r <= 0) return 0.0;
            return (Kn / n) * area * Math.Pow(r, 2.0 / 3.0) * Math.Sqrt(slope);
        }

        /// <summary>Just-full capacity for a pipe-arch <see cref="PipeSegment"/>.</summary>
        public static CapacityResult Capacity(PipeSegment pipe)
        {
            if (pipe == null) throw new ArgumentNullException(nameof(pipe));
            double span = ResolveSpan(pipe);
            double rise = ResolveRise(pipe);
            double n = pipe.ManningN;
            double s = pipe.Slope;
            if (s <= 0) throw new ArgumentOutOfRangeException(nameof(pipe), "Slope must be > 0.");

            double areaFull = WettedAreaAtDepth(span, rise, rise);
            double perimeterFull = WettedPerimeterAtDepth(span, rise, rise);
            double rFull = areaFull / perimeterFull;
            double qFull = (Kn / n) * areaFull * Math.Pow(rFull, 2.0 / 3.0) * Math.Sqrt(s);
            double vFull = qFull / areaFull;
            double qPeak = FindPeakFlow(span, rise, n, s);

            var result = new CapacityResult
            {
                FullFlowCfs = qFull,
                FullVelocityFps = vFull,
                PeakFlowCfs = qPeak,
            };
            result.Steps.Add(new CalcStep("B", span, "ft", "inside span at spring line"));
            result.Steps.Add(new CalcStep("H", rise, "ft", "inside rise invert-to-crown"));
            result.Steps.Add(new CalcStep("R_arc", ArcRadiusFt(span, rise), "ft", "B^2/(16H)+H/2"));
            result.Steps.Add(new CalcStep("y_s", SpringLineDepthFt(span, rise), "ft", "spring-line depth"));
            result.Steps.Add(new CalcStep("A_full", areaFull, "ft^2", "arc segment + vertical sides + crown"));
            result.Steps.Add(new CalcStep("P_full", perimeterFull, "ft", "wetted perimeter at y=H"));
            result.Steps.Add(new CalcStep("R_full", rFull, "ft", "A_full/P_full"));
            result.Steps.Add(new CalcStep("Q_full", qFull, "cfs", "(1.486/n)*A*R^(2/3)*S^(1/2)"));
            result.Steps.Add(new CalcStep("V_full", vFull, "ft/s", "Q_full/A_full"));
            result.Steps.Add(new CalcStep("Q_peak", qPeak, "cfs", "max Manning Q(y) for y in (0,H]"));
            return result;
        }

        /// <summary>
        /// Normal depth for a target flow via bisection on depth in (0, H].
        /// Flags surcharge when flow exceeds peak open-channel capacity.
        /// </summary>
        public static NormalDepthResult NormalDepth(PipeSegment pipe, double targetFlowCfs)
        {
            if (pipe == null) throw new ArgumentNullException(nameof(pipe));
            if (targetFlowCfs < 0) throw new ArgumentOutOfRangeException(nameof(targetFlowCfs));

            double span = ResolveSpan(pipe);
            double rise = ResolveRise(pipe);
            double n = pipe.ManningN;
            double s = pipe.Slope;
            if (s <= 0) throw new ArgumentOutOfRangeException(nameof(pipe), "Slope must be > 0.");

            var result = new NormalDepthResult();
            double qPeak = FindPeakFlow(span, rise, n, s);

            if (targetFlowCfs >= qPeak)
            {
                double areaFull = WettedAreaAtDepth(span, rise, rise);
                result.DepthFt = rise;
                result.RelativeDepth = 1.0;
                result.Surcharged = true;
                result.VelocityFps = targetFlowCfs / areaFull;
                result.Steps.Add(new CalcStep("Q_peak", qPeak, "cfs", "peak open-channel capacity"));
                result.Steps.Add(new CalcStep("Q_design", targetFlowCfs, "cfs", "exceeds Q_peak -> SURCHARGED"));
                return result;
            }

            double yPeak = FindPeakDepth(span, rise, n, s);
            double lo = 1e-6, hi = yPeak;
            for (int i = 0; i < 200; i++)
            {
                double mid = 0.5 * (lo + hi);
                double q = FlowAtDepth(span, rise, mid, n, s);
                if (q < targetFlowCfs) lo = mid; else hi = mid;
                if (hi - lo < 1e-9) break;
            }
            double y = 0.5 * (lo + hi);

            var (area, _) = PartialFlowGeometry(span, rise, y);
            double v = area > 0 ? targetFlowCfs / area : 0.0;

            result.DepthFt = y;
            result.RelativeDepth = y / rise;
            result.VelocityFps = v;
            result.Surcharged = false;
            result.Steps.Add(new CalcStep("Q_design", targetFlowCfs, "cfs", "input design flow"));
            result.Steps.Add(new CalcStep("y_n", y, "ft", "bisection on Manning Q(y)=Q_design"));
            result.Steps.Add(new CalcStep("y/H", y / rise, "-", "relative depth"));
            result.Steps.Add(new CalcStep("V", v, "ft/s", "Q_design/A(y_n)"));
            return result;
        }

        /// <summary>Resolves span from <see cref="PipeSegment.SpanFt"/> or <see cref="PipeSegment.WidthFt"/>.</summary>
        public static double ResolveSpan(PipeSegment pipe)
        {
            if (pipe == null) throw new ArgumentNullException(nameof(pipe));
            double span = pipe.SpanFt > 0 ? pipe.SpanFt : pipe.WidthFt;
            if (span <= 0)
                throw new ArgumentOutOfRangeException(nameof(pipe), "Arch span must be > 0.");
            return span;
        }

        /// <summary>Resolves rise from <see cref="PipeSegment.RiseFt"/> or <see cref="PipeSegment.HeightFt"/>.</summary>
        public static double ResolveRise(PipeSegment pipe)
        {
            if (pipe == null) throw new ArgumentNullException(nameof(pipe));
            double rise = pipe.RiseFt > 0 ? pipe.RiseFt : pipe.HeightFt;
            if (rise <= 0)
                throw new ArgumentOutOfRangeException(nameof(pipe), "Arch rise must be > 0.");
            ValidateSpanRise(span: ResolveSpan(pipe), rise);
            return rise;
        }

        private static double FindPeakFlow(double spanFt, double riseFt, double n, double slope)
            => FlowAtDepth(spanFt, riseFt, FindPeakDepth(spanFt, riseFt, n, slope), n, slope);

        private static double FindPeakDepth(double spanFt, double riseFt, double n, double slope)
        {
            double yPeak = riseFt;
            double qPeak = 0.0;
            const int samples = 80;
            for (int i = 1; i <= samples; i++)
            {
                double y = riseFt * i / samples;
                double q = FlowAtDepth(spanFt, riseFt, y, n, slope);
                if (q >= qPeak)
                {
                    qPeak = q;
                    yPeak = y;
                }
            }
            return yPeak;
        }

        private static double WettedAreaAtDepth(double spanFt, double riseFt, double depthFt)
        {
            double spring = SpringLineDepthFt(spanFt, riseFt);
            if (depthFt <= spring)
                return CircularSegmentArea(spanFt, riseFt, depthFt);

            double areaArc = CircularSegmentArea(spanFt, riseFt, spring);
            return areaArc + spanFt * (depthFt - spring);
        }

        private static double WettedPerimeterAtDepth(double spanFt, double riseFt, double depthFt)
        {
            double spring = SpringLineDepthFt(spanFt, riseFt);
            if (depthFt <= spring)
                return CircularSegmentPerimeter(spanFt, riseFt, depthFt);

            double perimeterArc = CircularSegmentPerimeter(spanFt, riseFt, spring);
            double vertical = 2.0 * (depthFt - spring);
            if (depthFt >= riseFt - 1e-12)
                return perimeterArc + vertical + spanFt;

            return perimeterArc + vertical;
        }

        /// <summary>
        /// Circular intrados segment area below depth y (invert at circle low point).
        /// Same relation as partial circular pipe with diameter 2R.
        /// </summary>
        private static double CircularSegmentArea(double spanFt, double riseFt, double depthFt)
        {
            if (depthFt <= 0) return 0.0;
            double r = ArcRadiusFt(spanFt, riseFt);
            double theta = 2.0 * Math.Acos(Math.Max(-1.0, Math.Min(1.0, 1.0 - depthFt / r)));
            return r * r / 2.0 * (theta - Math.Sin(theta));
        }

        private static double CircularSegmentPerimeter(double spanFt, double riseFt, double depthFt)
        {
            if (depthFt <= 0) return 0.0;
            double r = ArcRadiusFt(spanFt, riseFt);
            double theta = 2.0 * Math.Acos(Math.Max(-1.0, Math.Min(1.0, 1.0 - depthFt / r)));
            return r * theta;
        }

        private static void ValidateSpanRise(double span, double rise)
        {
            if (span <= 0) throw new ArgumentOutOfRangeException(nameof(span));
            if (rise <= 0) throw new ArgumentOutOfRangeException(nameof(rise));

            double r = span * span / (16.0 * rise) + rise / 2.0;
            if (r + 1e-9 < span / 2.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(span),
                    "Invalid pipe-arch geometry: arc radius must be >= span/2 (spring line must lie on intrados).");
            }
        }
    }
}