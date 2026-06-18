using System;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Open-channel hydraulics for trapezoidal sections (US customary units).
    ///
    ///     Q = (1.486 / n) * A * R^(2/3) * S^(1/2)
    ///     Critical: Q^2 / g = A^3 / T
    ///
    /// Normal depth is solved by bisection; mirrors hc-refactored calc/index.js
    /// and HydraulicEngine trapezoidal routines.
    /// </summary>
    public static class ChannelHydraulics
    {
        /// <summary>Manning constant for US units (ft, cfs).</summary>
        public const double Kn = 1.486;

        /// <summary>Gravitational acceleration, ft/s².</summary>
        public const double G = 32.174;

        public sealed class GeometryResult
        {
            public double DepthFt { get; set; }
            public double AreaFt2 { get; set; }
            public double WettedPerimeterFt { get; set; }
            public double HydRadiusFt { get; set; }
            public double TopWidthFt { get; set; }
        }

        public sealed class FlowResult : TracedResult
        {
            public double FlowCfs { get; set; }
            public double VelocityFps { get; set; }
            public GeometryResult Geometry { get; set; } = new GeometryResult();
        }

        public sealed class NormalDepthResult : TracedResult
        {
            public double DepthFt { get; set; }
            public double FlowCfs { get; set; }
            public double VelocityFps { get; set; }
            public double FroudeNumber { get; set; }
            public string FlowRegime { get; set; } = "";
            public GeometryResult Geometry { get; set; } = new GeometryResult();
        }

        public sealed class CriticalDepthResult : TracedResult
        {
            public double DepthFt { get; set; }
            public double VelocityFps { get; set; }
            public double FroudeNumber { get; set; }
            public GeometryResult Geometry { get; set; } = new GeometryResult();
        }

        /// <summary>Trapezoidal cross-section geometry at depth y (ft).</summary>
        public static GeometryResult TrapezoidalGeometry(
            double bottomWidthFt,
            double sideSlopeZ,
            double depthFt)
        {
            if (bottomWidthFt < 0) throw new ArgumentOutOfRangeException(nameof(bottomWidthFt));
            if (sideSlopeZ < 0) throw new ArgumentOutOfRangeException(nameof(sideSlopeZ));
            if (depthFt < 0) throw new ArgumentOutOfRangeException(nameof(depthFt));

            double y = depthFt;
            double area = (bottomWidthFt + sideSlopeZ * y) * y;
            double perimeter = bottomWidthFt + 2.0 * y * Math.Sqrt(1.0 + sideSlopeZ * sideSlopeZ);
            double topWidth = bottomWidthFt + 2.0 * sideSlopeZ * y;
            double hydRadius = perimeter > 0 ? area / perimeter : 0.0;

            return new GeometryResult
            {
                DepthFt = y,
                AreaFt2 = area,
                WettedPerimeterFt = perimeter,
                HydRadiusFt = hydRadius,
                TopWidthFt = topWidth,
            };
        }

        /// <summary>Manning discharge (cfs) at a known flow depth in a trapezoidal channel.</summary>
        public static FlowResult FlowAtDepth(
            double bottomWidthFt,
            double sideSlopeZ,
            double depthFt,
            double manningN,
            double slopeFtPerFt)
        {
            ValidateManningInputs(bottomWidthFt, sideSlopeZ, depthFt, manningN, slopeFtPerFt);

            var geom = TrapezoidalGeometry(bottomWidthFt, sideSlopeZ, depthFt);
            double q = 0.0;
            double v = 0.0;

            if (geom.AreaFt2 > 0 && geom.HydRadiusFt > 0)
            {
                q = (Kn / manningN) * geom.AreaFt2
                    * Math.Pow(geom.HydRadiusFt, 2.0 / 3.0)
                    * Math.Sqrt(slopeFtPerFt);
                v = q / geom.AreaFt2;
            }

            var result = new FlowResult
            {
                FlowCfs = q,
                VelocityFps = v,
                Geometry = geom,
            };
            result.Steps.Add(new CalcStep("Q", q, "cfs", "(1.486/n)*A*R^(2/3)*S^(1/2)"));
            return result;
        }

        /// <summary>
        /// Normal depth for a target discharge via bisection on Manning's equation.
        /// </summary>
        public static NormalDepthResult NormalDepth(
            double bottomWidthFt,
            double sideSlopeZ,
            double manningN,
            double slopeFtPerFt,
            double targetFlowCfs)
        {
            if (targetFlowCfs < 0) throw new ArgumentOutOfRangeException(nameof(targetFlowCfs));
            ValidateManningInputs(bottomWidthFt, sideSlopeZ, 0.001, manningN, slopeFtPerFt);

            double yLo = 0.0001;
            double yHi = 100.0;

            while (FlowAtDepth(bottomWidthFt, sideSlopeZ, yHi, manningN, slopeFtPerFt).FlowCfs < targetFlowCfs
                   && yHi < 500.0)
            {
                yHi *= 2.0;
            }

            for (int i = 0; i < 60; i++)
            {
                double yMid = 0.5 * (yLo + yHi);
                double qMid = FlowAtDepth(bottomWidthFt, sideSlopeZ, yMid, manningN, slopeFtPerFt).FlowCfs;
                if (qMid > targetFlowCfs)
                    yHi = yMid;
                else
                    yLo = yMid;
            }

            double yn = 0.5 * (yLo + yHi);
            var flow = FlowAtDepth(bottomWidthFt, sideSlopeZ, yn, manningN, slopeFtPerFt);
            double hydraulicDepth = flow.Geometry.TopWidthFt > 0
                ? flow.Geometry.AreaFt2 / flow.Geometry.TopWidthFt
                : 0.0;
            double fr = hydraulicDepth > 0 ? flow.VelocityFps / Math.Sqrt(G * hydraulicDepth) : 0.0;
            string regime = fr < 1.0 ? "subcritical" : fr > 1.0 ? "supercritical" : "critical";

            var result = new NormalDepthResult
            {
                DepthFt = yn,
                FlowCfs = flow.FlowCfs,
                VelocityFps = flow.VelocityFps,
                FroudeNumber = fr,
                FlowRegime = regime,
                Geometry = flow.Geometry,
            };
            result.Steps.Add(new CalcStep("Q_target", targetFlowCfs, "cfs", "design discharge"));
            result.Steps.Add(new CalcStep("y_n", yn, "ft", "bisection on Manning Q(y)=Q_target"));
            result.Steps.Add(new CalcStep("Fr", fr, "-", "V/sqrt(g*A/T)"));
            return result;
        }

        /// <summary>
        /// Critical depth for trapezoidal channel: Q²/g = A³/T (bisection).
        /// </summary>
        public static CriticalDepthResult CriticalDepth(
            double bottomWidthFt,
            double sideSlopeZ,
            double flowCfs)
        {
            if (bottomWidthFt < 0) throw new ArgumentOutOfRangeException(nameof(bottomWidthFt));
            if (sideSlopeZ < 0) throw new ArgumentOutOfRangeException(nameof(sideSlopeZ));
            if (flowCfs < 0) throw new ArgumentOutOfRangeException(nameof(flowCfs));

            double target = flowCfs * flowCfs / G;
            double yLo = 0.0001;
            double yHi = 50.0;

            while (CriticalFunction(bottomWidthFt, sideSlopeZ, yHi) < target && yHi < 500.0)
                yHi *= 2.0;

            for (int i = 0; i < 100; i++)
            {
                double yMid = 0.5 * (yLo + yHi);
                double fMid = CriticalFunction(bottomWidthFt, sideSlopeZ, yMid);
                if (fMid > target)
                    yHi = yMid;
                else
                    yLo = yMid;
            }

            double yc = 0.5 * (yLo + yHi);
            var geom = TrapezoidalGeometry(bottomWidthFt, sideSlopeZ, yc);
            double v = geom.AreaFt2 > 0 ? flowCfs / geom.AreaFt2 : 0.0;
            double hydraulicDepth = geom.TopWidthFt > 0 ? geom.AreaFt2 / geom.TopWidthFt : 0.0;
            double fr = hydraulicDepth > 0 ? v / Math.Sqrt(G * hydraulicDepth) : 0.0;

            var result = new CriticalDepthResult
            {
                DepthFt = yc,
                VelocityFps = v,
                FroudeNumber = fr,
                Geometry = geom,
            };
            result.Steps.Add(new CalcStep("Q", flowCfs, "cfs", "design discharge"));
            result.Steps.Add(new CalcStep("y_c", yc, "ft", "bisection on Q^2/g = A^3/T"));
            result.Steps.Add(new CalcStep("Fr", fr, "-", "should be ~1 at critical"));
            return result;
        }

        private static double CriticalFunction(double b, double z, double y)
        {
            var geom = TrapezoidalGeometry(b, z, y);
            if (geom.TopWidthFt <= 0) return 0.0;
            return geom.AreaFt2 * geom.AreaFt2 * geom.AreaFt2 / geom.TopWidthFt;
        }

        private static void ValidateManningInputs(
            double bottomWidthFt,
            double sideSlopeZ,
            double depthFt,
            double manningN,
            double slopeFtPerFt)
        {
            if (bottomWidthFt < 0) throw new ArgumentOutOfRangeException(nameof(bottomWidthFt));
            if (sideSlopeZ < 0) throw new ArgumentOutOfRangeException(nameof(sideSlopeZ));
            if (depthFt <= 0) throw new ArgumentOutOfRangeException(nameof(depthFt));
            if (manningN <= 0) throw new ArgumentOutOfRangeException(nameof(manningN));
            if (slopeFtPerFt < 0) throw new ArgumentOutOfRangeException(nameof(slopeFtPerFt));
        }
    }
}