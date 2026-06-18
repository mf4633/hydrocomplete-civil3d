using System;
using System.Collections.Generic;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Manning's equation for circular gravity pipes, US customary units.
    ///
    ///     Q = (1.486 / n) * A * R^(2/3) * S^(1/2)
    ///
    /// where A is flow area (ft^2), R is hydraulic radius (ft), S is slope (ft/ft).
    /// All public-domain; see e.g. FHWA HEC-22.
    /// </summary>
    public static class Manning
    {
        /// <summary>Manning unit conversion constant for US units (ft, cfs).</summary>
        public const double Kn = 1.486;

        public sealed class CapacityResult : TracedResult
        {
            /// <summary>Full-barrel capacity, cfs.</summary>
            public double FullFlowCfs { get; set; }

            /// <summary>Full-barrel (just-full) velocity, ft/s.</summary>
            public double FullVelocityFps { get; set; }

            /// <summary>Peak capacity (occurs near 0.94 D for circular pipes), cfs.</summary>
            public double PeakFlowCfs { get; set; }
        }

        public sealed class NormalDepthResult : TracedResult
        {
            /// <summary>Computed normal depth, ft. Equals diameter when surcharged.</summary>
            public double DepthFt { get; set; }

            /// <summary>Depth as a fraction of diameter (d/D).</summary>
            public double RelativeDepth { get; set; }

            /// <summary>Average velocity at normal depth, ft/s.</summary>
            public double VelocityFps { get; set; }

            /// <summary>True when the design flow exceeds the pipe's peak open-channel capacity.</summary>
            public bool Surcharged { get; set; }
        }

        /// <summary>
        /// Partial-flow area and hydraulic radius for a circular pipe at depth
        /// <paramref name="y"/> (ft). Returns (0, 0) when y &lt;= 0.
        /// </summary>
        public static (double AreaFt2, double HydRadiusFt) PartialFlowGeometry(double diameterFt, double y)
        {
            if (diameterFt <= 0) throw new ArgumentOutOfRangeException(nameof(diameterFt));
            if (y <= 0) return (0.0, 0.0);
            if (y > diameterFt) y = diameterFt;

            // Central angle subtended by the water surface (radians).
            double theta = 2.0 * Math.Acos(1.0 - 2.0 * y / diameterFt);
            double area = (diameterFt * diameterFt / 8.0) * (theta - Math.Sin(theta));
            double perimeter = diameterFt * theta / 2.0;
            if (perimeter <= 0) return (0.0, 0.0);
            return (area, area / perimeter);
        }

        /// <summary>Flow (cfs) carried by a circular pipe flowing at depth <paramref name="y"/> (ft).</summary>
        public static double FlowAtDepth(double diameterFt, double y, double n, double slope)
        {
            if (diameterFt <= 0) throw new ArgumentOutOfRangeException(nameof(diameterFt));
            if (n <= 0) throw new ArgumentOutOfRangeException(nameof(n));
            if (slope <= 0) throw new ArgumentOutOfRangeException(nameof(slope));
            if (y <= 0) return 0.0;

            var (area, r) = PartialFlowGeometry(diameterFt, y);
            if (area <= 0 || r <= 0) return 0.0;
            return (Kn / n) * area * Math.Pow(r, 2.0 / 3.0) * Math.Sqrt(slope);
        }

        /// <summary>
        /// Full-barrel capacity for a <see cref="PipeSegment"/>, dispatching by
        /// <see cref="PipeSegment.Shape"/> (circular, box, or arch).
        /// </summary>
        public static CapacityResult Capacity(PipeSegment pipe)
        {
            if (pipe == null) throw new ArgumentNullException(nameof(pipe));
            switch (pipe.Shape)
            {
                case PipeShape.Box:
                    return FromBoxCapacity(BoxConduit.Capacity(pipe));
                case PipeShape.Arch:
                    return FromArchCapacity(ArchConduit.Capacity(pipe));
                default:
                    return CapacityCircular(pipe);
            }
        }

        /// <summary>Full-barrel capacity and the slightly-higher peak capacity of a circular pipe.</summary>
        public static CapacityResult CapacityCircular(PipeSegment pipe)
        {
            if (pipe == null) throw new ArgumentNullException(nameof(pipe));
            double d = pipe.DiameterFt, n = pipe.ManningN, s = pipe.Slope;
            if (d <= 0) throw new ArgumentOutOfRangeException(nameof(pipe), "Diameter must be > 0.");
            if (s <= 0) throw new ArgumentOutOfRangeException(nameof(pipe), "Slope must be > 0.");

            double areaFull = Math.PI * d * d / 4.0;
            double rFull = d / 4.0; // hydraulic radius of a full circular pipe
            double qFull = (Kn / n) * areaFull * Math.Pow(rFull, 2.0 / 3.0) * Math.Sqrt(s);
            double vFull = qFull / areaFull;

            // Peak open-channel flow for a circle occurs at d/D ~= 0.938.
            double qPeak = FlowAtDepth(d, 0.938 * d, n, s);

            var r = new CapacityResult
            {
                FullFlowCfs = qFull,
                FullVelocityFps = vFull,
                PeakFlowCfs = qPeak,
            };
            r.Steps.Add(new CalcStep("A_full", areaFull, "ft^2", "pi*D^2/4"));
            r.Steps.Add(new CalcStep("R_full", rFull, "ft", "D/4"));
            r.Steps.Add(new CalcStep("Q_full", qFull, "cfs", "(1.486/n)*A*R^(2/3)*S^(1/2)"));
            r.Steps.Add(new CalcStep("V_full", vFull, "ft/s", "Q_full/A_full"));
            r.Steps.Add(new CalcStep("Q_peak", qPeak, "cfs", "Manning at d/D=0.938"));
            return r;
        }

        /// <summary>
        /// Normal depth for a target flow, dispatching by <see cref="PipeSegment.Shape"/>.
        /// </summary>
        public static NormalDepthResult NormalDepth(PipeSegment pipe, double targetFlowCfs)
        {
            if (pipe == null) throw new ArgumentNullException(nameof(pipe));
            if (targetFlowCfs < 0) throw new ArgumentOutOfRangeException(nameof(targetFlowCfs));
            switch (pipe.Shape)
            {
                case PipeShape.Box:
                    return FromBoxNormalDepth(BoxConduit.NormalDepth(pipe, targetFlowCfs));
                case PipeShape.Arch:
                    return FromArchNormalDepth(ArchConduit.NormalDepth(pipe, targetFlowCfs));
                default:
                    return NormalDepthCircular(pipe, targetFlowCfs);
            }
        }

        /// <summary>
        /// Normal depth for a target flow via bisection on depth in (0, D).
        /// Flags surcharge when the flow exceeds the pipe's peak open-channel capacity.
        /// </summary>
        public static NormalDepthResult NormalDepthCircular(PipeSegment pipe, double targetFlowCfs)
        {
            if (pipe == null) throw new ArgumentNullException(nameof(pipe));
            if (targetFlowCfs < 0) throw new ArgumentOutOfRangeException(nameof(targetFlowCfs));
            double d = pipe.DiameterFt, n = pipe.ManningN, s = pipe.Slope;

            var result = new NormalDepthResult();

            double qPeak = FlowAtDepth(d, 0.938 * d, n, s);
            if (targetFlowCfs >= qPeak)
            {
                result.DepthFt = d;
                result.RelativeDepth = 1.0;
                result.Surcharged = true;
                double areaFull = Math.PI * d * d / 4.0;
                result.VelocityFps = targetFlowCfs / areaFull;
                result.Steps.Add(new CalcStep("Q_peak", qPeak, "cfs", "peak open-channel capacity"));
                result.Steps.Add(new CalcStep("Q_design", targetFlowCfs, "cfs", "exceeds Q_peak -> SURCHARGED"));
                return result;
            }

            // Bisection: Manning flow increases monotonically with depth up to ~0.94D,
            // and the target is below the peak, so a unique root exists in (0, 0.94D].
            double lo = 1e-6, hi = 0.938 * d;
            for (int i = 0; i < 200; i++)
            {
                double mid = 0.5 * (lo + hi);
                double q = FlowAtDepth(d, mid, n, s);
                if (q < targetFlowCfs) lo = mid; else hi = mid;
                if (hi - lo < 1e-9) break;
            }
            double y = 0.5 * (lo + hi);

            // Velocity at that depth.
            var (area, _) = PartialFlowGeometry(d, y);
            double v = area > 0 ? targetFlowCfs / area : 0.0;

            result.DepthFt = y;
            result.RelativeDepth = y / d;
            result.VelocityFps = v;
            result.Surcharged = false;
            result.Steps.Add(new CalcStep("Q_design", targetFlowCfs, "cfs", "input peak flow"));
            result.Steps.Add(new CalcStep("d_n", y, "ft", "bisection on Manning Q(y)=Q_design"));
            result.Steps.Add(new CalcStep("d/D", y / d, "-", "relative depth"));
            result.Steps.Add(new CalcStep("V", v, "ft/s", "Q_design/A(d_n)"));
            return result;
        }

        private static CapacityResult FromBoxCapacity(BoxConduit.CapacityResult source)
            => CopyCapacity(source.FullFlowCfs, source.FullVelocityFps, source.PeakFlowCfs, source.Steps);

        private static CapacityResult FromArchCapacity(ArchConduit.CapacityResult source)
            => CopyCapacity(source.FullFlowCfs, source.FullVelocityFps, source.PeakFlowCfs, source.Steps);

        private static CapacityResult CopyCapacity(
            double fullFlowCfs, double fullVelocityFps, double peakFlowCfs, List<CalcStep> steps)
        {
            var result = new CapacityResult
            {
                FullFlowCfs = fullFlowCfs,
                FullVelocityFps = fullVelocityFps,
                PeakFlowCfs = peakFlowCfs,
            };
            result.Steps.AddRange(steps);
            return result;
        }

        private static NormalDepthResult FromBoxNormalDepth(BoxConduit.NormalDepthResult source)
            => CopyNormalDepth(source.DepthFt, source.RelativeDepth, source.VelocityFps, source.Surcharged, source.Steps);

        private static NormalDepthResult FromArchNormalDepth(ArchConduit.NormalDepthResult source)
            => CopyNormalDepth(source.DepthFt, source.RelativeDepth, source.VelocityFps, source.Surcharged, source.Steps);

        private static NormalDepthResult CopyNormalDepth(
            double depthFt, double relativeDepth, double velocityFps, bool surcharged, List<CalcStep> steps)
        {
            var result = new NormalDepthResult
            {
                DepthFt = depthFt,
                RelativeDepth = relativeDepth,
                VelocityFps = velocityFps,
                Surcharged = surcharged,
            };
            result.Steps.AddRange(steps);
            return result;
        }
    }
}
