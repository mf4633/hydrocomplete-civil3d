using System;
using System.Collections.Generic;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Culvert headwater analysis (FHWA HDS-5) with inlet/outlet control and
    /// orifice / Manning discharge modes.
    /// </summary>
    public static class CulvertHydraulics
    {
        public const double G = 32.2;
        public const double Kn = 1.486;

        public enum ControlType
        {
            None,
            Inlet,
            Outlet,
        }

        public enum DischargeMode
        {
            Orifice,
            Manning,
        }

        public sealed class CulvertParameters
        {
            public double DiameterIn { get; set; } = 24.0;
            public double LengthFt { get; set; } = 100.0;
            public double SlopeFtPerFt { get; set; } = 0.01;
            public double ManningN { get; set; } = 0.013;
            public double EntranceLossKe { get; set; } = 0.5;
        }

        public sealed class HeadwaterResult : TracedResult
        {
            public double DischargeCfs { get; set; }
            public double HeadwaterFt { get; set; }
            public double HeadwaterInletFt { get; set; }
            public double HeadwaterOutletFt { get; set; }
            public ControlType Control { get; set; }
            public double VelocityFps { get; set; }
            public double DiameterFt { get; set; }
        }

        public sealed class RatingPoint
        {
            public double DischargeCfs { get; set; }
            public double HeadwaterFt { get; set; }
            public ControlType Control { get; set; }
        }

        /// <summary>Orifice flow: Q = C<sub>d</sub>·A·√(2gH).</summary>
        public static double OrificeFlowCfs(double headFt, double diameterFt, double dischargeCoeff = 0.6)
        {
            if (headFt < 0) throw new ArgumentOutOfRangeException(nameof(headFt));
            if (diameterFt <= 0) throw new ArgumentOutOfRangeException(nameof(diameterFt));

            double area = Math.PI * diameterFt * diameterFt / 4.0;
            return dischargeCoeff * area * Math.Sqrt(2.0 * G * headFt);
        }

        /// <summary>Full-barrel Manning flow for circular pipe.</summary>
        public static double ManningFullFlowCfs(
            double diameterFt,
            double slopeFtPerFt,
            double manningN)
        {
            if (diameterFt <= 0) throw new ArgumentOutOfRangeException(nameof(diameterFt));
            if (slopeFtPerFt < 0) throw new ArgumentOutOfRangeException(nameof(slopeFtPerFt));
            if (manningN <= 0) throw new ArgumentOutOfRangeException(nameof(manningN));

            double area = Math.PI * diameterFt * diameterFt / 4.0;
            double radius = diameterFt / 4.0;
            return (Kn / manningN) * area * Math.Pow(radius, 2.0 / 3.0) * Math.Sqrt(slopeFtPerFt);
        }

        /// <summary>
        /// Headwater for a given discharge under inlet and outlet control (FHWA HDS-5 simplified).
        /// </summary>
        public static HeadwaterResult Headwater(
            double dischargeCfs,
            CulvertParameters culvert,
            double tailwaterFt = 0.0)
        {
            if (dischargeCfs < 0) throw new ArgumentOutOfRangeException(nameof(dischargeCfs));
            if (culvert == null) throw new ArgumentNullException(nameof(culvert));

            double dFt = culvert.DiameterIn / 12.0;
            double l = culvert.LengthFt;
            double s = culvert.SlopeFtPerFt;
            double n = culvert.ManningN;
            double ke = culvert.EntranceLossKe;
            double area = Math.PI * dFt * dFt / 4.0;

            double hwInlet = 0.0;
            double hwOutlet = 0.0;
            double velocity = 0.0;

            if (dischargeCfs > 0.0 && area > 0.0)
            {
                velocity = dischargeCfs / area;
                double qOverAd05 = dischargeCfs / (area * Math.Pow(dFt, 0.5));

                const double ku = 0.0098;
                const double mu = 2.0;
                const double ksu = -0.5;
                const double cs = 0.0398;
                const double ys = 0.67;

                double hwUnsub = dFt * (1.0 + ku * Math.Pow(qOverAd05, mu) + ksu * s);
                double hwSub = dFt * (cs * Math.Pow(qOverAd05, 2.0) + ys - 0.5 * s);
                hwInlet = Math.Max(hwUnsub, hwSub);

                double r = dFt / 4.0;
                double frictionCoeff = 29.0 * n * n * l / Math.Pow(r, 4.0 / 3.0);
                double hLoss = (ke + 1.0 + frictionCoeff) * velocity * velocity / (2.0 * G);
                double dc = 0.467 * Math.Pow(
                    dischargeCfs * dischargeCfs / (G * Math.Pow(dFt, 5.0)), 0.1) * dFt;
                double ho = Math.Max(tailwaterFt, (Math.Min(dc, dFt) + dFt) / 2.0);
                hwOutlet = Math.Max(hLoss + ho - l * s, 0.0);
            }

            ControlType control = hwInlet >= hwOutlet ? ControlType.Inlet : ControlType.Outlet;
            double hw = Math.Max(hwInlet, hwOutlet);

            var result = new HeadwaterResult
            {
                DischargeCfs = dischargeCfs,
                HeadwaterFt = hw,
                HeadwaterInletFt = hwInlet,
                HeadwaterOutletFt = hwOutlet,
                Control = control,
                VelocityFps = velocity,
                DiameterFt = dFt,
            };

            result.Steps.Add(new CalcStep("D", dFt, "ft", "culvert diameter"));
            result.Steps.Add(new CalcStep("HW_inlet", hwInlet, "ft", "FHWA inlet control"));
            result.Steps.Add(new CalcStep("HW_outlet", hwOutlet, "ft", "FHWA outlet control"));
            result.Steps.Add(new CalcStep("HW", hw, "ft", "max(inlet,outlet)"));
            return result;
        }

        /// <summary>
        /// Discharge from headwater using the lesser of orifice and Manning full-flow capacity.
        /// </summary>
        public static double DischargeFromHeadwaterFt(
            double headwaterFt,
            CulvertParameters culvert,
            DischargeMode mode = DischargeMode.Orifice)
        {
            if (headwaterFt < 0) throw new ArgumentOutOfRangeException(nameof(headwaterFt));
            if (culvert == null) throw new ArgumentNullException(nameof(culvert));

            double dFt = culvert.DiameterIn / 12.0;
            if (headwaterFt <= 0.0) return 0.0;

            double qOrifice = OrificeFlowCfs(headwaterFt, dFt);
            double qManning = ManningFullFlowCfs(dFt, culvert.SlopeFtPerFt, culvert.ManningN);
            return mode == DischargeMode.Manning ? qManning : Math.Min(qOrifice, qManning);
        }

        /// <summary>Q vs HW rating curve for pond outlet / storage routing.</summary>
        public static List<RatingPoint> RatingCurve(
            CulvertParameters culvert,
            double tailwaterFt = 0.0,
            double maxDischargeCfs = 200.0,
            int pointCount = 51)
        {
            if (culvert == null) throw new ArgumentNullException(nameof(culvert));
            if (maxDischargeCfs < 0) throw new ArgumentOutOfRangeException(nameof(maxDischargeCfs));
            if (pointCount < 2) throw new ArgumentOutOfRangeException(nameof(pointCount));

            var curve = new List<RatingPoint> { new RatingPoint { DischargeCfs = 0.0, HeadwaterFt = 0.0, Control = ControlType.None } };
            double step = maxDischargeCfs / (pointCount - 1);
            for (int i = 1; i < pointCount; i++)
            {
                double q = i * step;
                HeadwaterResult hw = Headwater(q, culvert, tailwaterFt);
                curve.Add(new RatingPoint
                {
                    DischargeCfs = q,
                    HeadwaterFt = hw.HeadwaterFt,
                    Control = hw.Control,
                });
            }

            return curve;
        }
    }
}