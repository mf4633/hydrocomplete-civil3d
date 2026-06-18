using System;
using System.Collections.Generic;
using System.Linq;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Classic Muskingum channel routing (Chow, Maidment &amp; Mays, 1988).
    ///
    ///     O₂ = C₀·I₂ + C₁·I₁ + C₂·O₁
    ///     C₀ + C₁ + C₂ = 1
    /// </summary>
    public static class MuskingumRouting
    {
        public sealed class Coefficients
        {
            public double KHours { get; set; }
            public double X { get; set; }
            public double DtHours { get; set; }
            public double C0 { get; set; }
            public double C1 { get; set; }
            public double C2 { get; set; }
            public double Sum => C0 + C1 + C2;
        }

        public sealed class HydrographPoint
        {
            public double TimeHours { get; set; }
            public double InflowCfs { get; set; }
            public double OutflowCfs { get; set; }
        }

        public sealed class RoutingResult : TracedResult
        {
            public Coefficients Coefficients { get; set; } = new Coefficients();
            public List<HydrographPoint> Points { get; } = new List<HydrographPoint>();
            public double PeakInflowCfs { get; set; }
            public double PeakOutflowCfs { get; set; }
            public double InflowVolumeAcreFt { get; set; }
            public double OutflowVolumeAcreFt { get; set; }
        }

        public static Coefficients ComputeCoefficients(double kHours, double x, double dtHours)
        {
            if (kHours <= 0) throw new ArgumentOutOfRangeException(nameof(kHours));
            if (x < 0 || x > 0.5) throw new ArgumentOutOfRangeException(nameof(x), "X must be in [0, 0.5].");
            if (dtHours <= 0) throw new ArgumentOutOfRangeException(nameof(dtHours));

            double denom = 2.0 * kHours * (1.0 - x) + dtHours;
            double c0 = (-2.0 * kHours * x + dtHours) / denom;
            if (c0 < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(dtHours),
                    $"Timestep must satisfy dt >= 2*K*X ({2.0 * kHours * x:0.####} hr) for Muskingum stability.");
            }

            return new Coefficients
            {
                KHours = kHours,
                X = x,
                DtHours = dtHours,
                C0 = c0,
                C1 = (2.0 * kHours * x + dtHours) / denom,
                C2 = (2.0 * kHours * (1.0 - x) - dtHours) / denom,
            };
        }

        public static RoutingResult Route(
            IReadOnlyList<double> inflowCfs,
            double kHours,
            double x,
            double dtHours)
        {
            if (inflowCfs == null) throw new ArgumentNullException(nameof(inflowCfs));
            if (inflowCfs.Count == 0)
                throw new ArgumentOutOfRangeException(nameof(inflowCfs), "Inflow hydrograph must not be empty.");

            var coeffs = ComputeCoefficients(kHours, x, dtHours);
            var result = new RoutingResult { Coefficients = coeffs };

            double o1 = inflowCfs[0];

            for (int i = 0; i < inflowCfs.Count; i++)
            {
                double i1 = i > 0 ? inflowCfs[i - 1] : 0.0;
                double i2 = inflowCfs[i];
                double o2 = Math.Max(0.0, coeffs.C0 * i2 + coeffs.C1 * i1 + coeffs.C2 * o1);
                result.Points.Add(new HydrographPoint { TimeHours = i * dtHours, InflowCfs = i2, OutflowCfs = o2 });
                o1 = o2;
            }

            // Route zero inflow until outflow drains (mass balance / tail attenuation).
            int tailStep = inflowCfs.Count;
            int maxTail = 2000;
            while (o1 > 1e-6 && maxTail-- > 0)
            {
                double i1 = result.Points[result.Points.Count - 1].InflowCfs;
                double o2 = Math.Max(0.0, coeffs.C0 * 0.0 + coeffs.C1 * i1 + coeffs.C2 * o1);
                result.Points.Add(new HydrographPoint
                {
                    TimeHours = tailStep * dtHours,
                    InflowCfs = 0.0,
                    OutflowCfs = o2,
                });
                o1 = o2;
                tailStep++;
            }

            result.PeakInflowCfs = inflowCfs.Max();
            result.PeakOutflowCfs = result.Points.Max(p => p.OutflowCfs);
            result.InflowVolumeAcreFt = HydrographVolumeAcreFt(inflowCfs, dtHours);
            result.OutflowVolumeAcreFt = HydrographVolumeAcreFt(
                result.Points.Select(p => p.OutflowCfs).ToList(), dtHours);

            result.Steps.Add(new CalcStep("C_sum", coeffs.Sum, "-", "C0+C1+C2 (should be 1)"));
            return result;
        }

        public static double HydrographVolumeAcreFt(IReadOnlyList<double> flowsCfs, double dtHours)
        {
            if (flowsCfs == null || flowsCfs.Count == 0) return 0.0;
            if (dtHours <= 0) throw new ArgumentOutOfRangeException(nameof(dtHours));

            double volumeCf = 0.0;
            for (int i = 1; i < flowsCfs.Count; i++)
                volumeCf += 0.5 * (flowsCfs[i - 1] + flowsCfs[i]) * dtHours * 3600.0;

            return volumeCf / 43560.0;
        }
    }
}