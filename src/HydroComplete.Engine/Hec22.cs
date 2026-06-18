using System;

namespace HydroComplete.Engine
{
    /// <summary>
    /// HEC-22 minor (local) head losses: h_m = K * V²/(2g).
    /// Public-domain; FHWA HEC-22 (2009) storm drain design.
    /// </summary>
    public static class Hec22
    {
        /// <summary>Gravitational acceleration, ft/s².</summary>
        public const double G_Fps2 = 32.174;

        /// <summary>Default manhole junction loss coefficient (straight-through), HEC-22.</summary>
        public const double DefaultManholeK = 0.15;

        /// <summary>Typical projecting entrance loss coefficient.</summary>
        public const double DefaultEntranceK = 0.5;

        /// <summary>Exit loss: velocity head fully dissipated.</summary>
        public const double DefaultExitK = 1.0;

        /// <summary>HEC-22 Eq. 7-5 bend-loss K at 0° deflection (straight pipe run).</summary>
        public const double BendLossK0Deg = 0.0;

        /// <summary>HEC-22 Eq. 7-5 bend-loss K at 45° deflection (0.0033 × 45).</summary>
        public const double BendLossK45Deg = 0.1485;

        /// <summary>HEC-22 Eq. 7-5 bend-loss K at 90° deflection (0.0033 × 90).</summary>
        public const double BendLossK90Deg = 0.297;

        public sealed class MinorLossResult : TracedResult
        {
            public double HeadLossFt { get; set; }
            public double VelocityHeadFt { get; set; }
            public double LossCoefficient { get; set; }
        }

        public static double VelocityFps(double qCfs, double areaFt2)
        {
            if (qCfs < 0) throw new ArgumentOutOfRangeException(nameof(qCfs));
            if (areaFt2 <= 0) throw new ArgumentOutOfRangeException(nameof(areaFt2));
            return qCfs / areaFt2;
        }

        public static double VelocityHeadFt(double velocityFps)
        {
            if (velocityFps < 0) throw new ArgumentOutOfRangeException(nameof(velocityFps));
            return velocityFps * velocityFps / (2.0 * G_Fps2);
        }

        public static double VelocityHeadFromFlow(double qCfs, double areaFt2)
            => VelocityHeadFt(VelocityFps(qCfs, areaFt2));

        /// <summary>Minor head loss h_m = K * Vh.</summary>
        public static MinorLossResult MinorHeadLoss(double lossCoefficient, double velocityHeadFt)
        {
            if (lossCoefficient < 0) throw new ArgumentOutOfRangeException(nameof(lossCoefficient));
            if (velocityHeadFt < 0) throw new ArgumentOutOfRangeException(nameof(velocityHeadFt));

            double hm = lossCoefficient * velocityHeadFt;
            var r = new MinorLossResult
            {
                HeadLossFt = hm,
                VelocityHeadFt = velocityHeadFt,
                LossCoefficient = lossCoefficient,
            };
            r.Steps.Add(new CalcStep("Vh", velocityHeadFt, "ft", "V²/(2g)"));
            r.Steps.Add(new CalcStep("K", lossCoefficient, "-", "loss coefficient"));
            r.Steps.Add(new CalcStep("h_m", hm, "ft", "K*Vh"));
            return r;
        }

        /// <summary>Minor head loss from flow and area: h_m = K * V²/(2g).</summary>
        public static MinorLossResult MinorHeadLossFromFlow(double lossCoefficient, double qCfs, double areaFt2)
        {
            double vh = VelocityHeadFromFlow(qCfs, areaFt2);
            return MinorHeadLoss(lossCoefficient, vh);
        }

        /// <summary>
        /// Bend/deflection loss coefficient K for pipe-run curvature (HEC-22 Eq. 7-5: K = 0.0033·θ).
        /// Tabulated at 0°, 45°, and 90°; linear interpolation between anchors.
        /// </summary>
        /// <param name="deflectionDegrees">Angle of curvature, degrees (0 = straight).</param>
        public static double BendLossK(double deflectionDegrees)
        {
            if (deflectionDegrees < 0)
                throw new ArgumentOutOfRangeException(nameof(deflectionDegrees));

            if (deflectionDegrees <= 0)
                return BendLossK0Deg;
            if (deflectionDegrees >= 90)
                return BendLossK90Deg + (BendLossK90Deg - BendLossK45Deg) / 45.0 * (deflectionDegrees - 90.0);

            if (deflectionDegrees <= 45)
                return BendLossK0Deg + (BendLossK45Deg - BendLossK0Deg) * (deflectionDegrees / 45.0);

            return BendLossK45Deg + (BendLossK90Deg - BendLossK45Deg) * ((deflectionDegrees - 45.0) / 45.0);
        }
    }
}