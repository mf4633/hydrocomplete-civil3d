using System;
using System.Collections.Generic;
using System.Linq;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Variable-parameter Muskingum-Cunge channel routing (Cunge, 1969).
    /// Derives K and X from trapezoidal channel geometry at a reference discharge.
    /// </summary>
    public static class MuskingumCungeRouting
    {
        public const double Kn = 1.486;
        public const double G = 32.2;

        public sealed class ReachParameters
        {
            public double LengthFt { get; set; } = 5000.0;
            public double SlopeFtPerFt { get; set; } = 0.005;
            public double ManningN { get; set; } = 0.035;
            public double BottomWidthFt { get; set; } = 10.0;
            public double SideSlopeZ { get; set; } = 2.0;
            public double BankfullDepthFt { get; set; } = 10.0;
        }

        public sealed class DerivedParameters
        {
            public double ReferenceFlowCfs { get; set; }
            public double ReferenceDepthFt { get; set; }
            public double ReferenceVelocityFps { get; set; }
            public double CelerityFps { get; set; }
            public double KSeconds { get; set; }
            public double KHours { get; set; }
            public double X { get; set; }
            public double C1 { get; set; }
            public double C2 { get; set; }
            public double C3 { get; set; }
            public double TimestepHours { get; set; }
            public double Sum => C1 + C2 + C3;
        }

        public sealed class HydrographPoint
        {
            public double TimeHours { get; set; }
            public double InflowCfs { get; set; }
            public double OutflowCfs { get; set; }
        }

        public sealed class RoutingResult : TracedResult
        {
            public DerivedParameters Parameters { get; set; } = new DerivedParameters();
            public List<HydrographPoint> Points { get; } = new List<HydrographPoint>();
            public double PeakInflowCfs { get; set; }
            public double PeakOutflowCfs { get; set; }
            public double PeakReductionPercent { get; set; }
            public double TravelTimeHours { get; set; }
        }

        public static DerivedParameters DeriveParameters(
            IReadOnlyList<double> inflowCfs,
            ReachParameters reach,
            double dtHours)
        {
            if (inflowCfs == null || inflowCfs.Count == 0)
                throw new ArgumentOutOfRangeException(nameof(inflowCfs));
            if (reach == null) throw new ArgumentNullException(nameof(reach));
            if (dtHours <= 0) throw new ArgumentOutOfRangeException(nameof(dtHours));

            double qRef = Math.Max(inflowCfs.Max(), 0.01);
            var normal = ChannelHydraulics.NormalDepth(
                reach.BottomWidthFt,
                reach.SideSlopeZ,
                reach.ManningN,
                reach.SlopeFtPerFt,
                qRef);

            double dRef = Math.Min(normal.DepthFt, reach.BankfullDepthFt * 1.5);
            var geom = ChannelHydraulics.TrapezoidalGeometry(reach.BottomWidthFt, reach.SideSlopeZ, dRef);
            double vRef = geom.AreaFt2 > 0 ? qRef / geom.AreaFt2 : 0.0;
            double c = (5.0 / 3.0) * vRef;
            double kSeconds = c > 0 ? reach.LengthFt / c : 1.0;
            double kHours = kSeconds / 3600.0;

            double xRaw = 0.5 * (1.0 - qRef / (geom.TopWidthFt * reach.SlopeFtPerFt * c * reach.LengthFt));
            double x = Math.Max(0.0, Math.Min(0.5, xRaw));

            double dtSeconds = dtHours * 3600.0;
            double dtMin = 2.0 * kSeconds * x;
            double dtMax = 2.0 * kSeconds * (1.0 - x);
            double dtUsedSeconds = dtSeconds;
            if (dtUsedSeconds < dtMin && dtMin > 0) dtUsedSeconds = dtMin + 1.0;
            if (dtUsedSeconds > dtMax && dtMax > 0) dtUsedSeconds = Math.Max(dtMax - 1.0, dtMin + 1.0);
            if (dtUsedSeconds <= 0) dtUsedSeconds = dtSeconds;

            double denom = 2.0 * kSeconds * (1.0 - x) + dtUsedSeconds;
            double c1 = (dtUsedSeconds - 2.0 * kSeconds * x) / denom;
            double c2 = (dtUsedSeconds + 2.0 * kSeconds * x) / denom;
            double c3 = (2.0 * kSeconds * (1.0 - x) - dtUsedSeconds) / denom;

            return new DerivedParameters
            {
                ReferenceFlowCfs = qRef,
                ReferenceDepthFt = dRef,
                ReferenceVelocityFps = vRef,
                CelerityFps = c,
                KSeconds = kSeconds,
                KHours = kHours,
                X = x,
                C1 = c1,
                C2 = c2,
                C3 = c3,
                TimestepHours = dtUsedSeconds / 3600.0,
            };
        }

        public static RoutingResult Route(
            IReadOnlyList<double> inflowCfs,
            ReachParameters reach,
            double dtHours)
        {
            if (inflowCfs == null) throw new ArgumentNullException(nameof(inflowCfs));
            if (inflowCfs.Count == 0)
                throw new ArgumentOutOfRangeException(nameof(inflowCfs), "Inflow hydrograph must not be empty.");

            var derived = DeriveParameters(inflowCfs, reach, dtHours);
            var result = new RoutingResult { Parameters = derived };

            double oPrev = inflowCfs[0];
            result.Points.Add(new HydrographPoint
            {
                TimeHours = 0.0,
                InflowCfs = inflowCfs[0],
                OutflowCfs = oPrev,
            });

            for (int i = 1; i < inflowCfs.Count; i++)
            {
                double iCurr = inflowCfs[i];
                double iPrev = inflowCfs[i - 1];
                double oCurr = Math.Max(0.0, derived.C1 * iCurr + derived.C2 * iPrev + derived.C3 * oPrev);
                result.Points.Add(new HydrographPoint
                {
                    TimeHours = i * derived.TimestepHours,
                    InflowCfs = iCurr,
                    OutflowCfs = oCurr,
                });
                oPrev = oCurr;
            }

            int tailStep = inflowCfs.Count;
            int maxTail = 2000;
            while (oPrev > 0.01 && maxTail-- > 0)
            {
                double oCurr = Math.Max(0.0, derived.C3 * oPrev);
                result.Points.Add(new HydrographPoint
                {
                    TimeHours = tailStep * derived.TimestepHours,
                    InflowCfs = 0.0,
                    OutflowCfs = oCurr,
                });
                oPrev = oCurr;
                tailStep++;
            }

            result.PeakInflowCfs = inflowCfs.Max();
            result.PeakOutflowCfs = result.Points.Max(p => p.OutflowCfs);
            result.PeakReductionPercent = result.PeakInflowCfs > 0
                ? (1.0 - result.PeakOutflowCfs / result.PeakInflowCfs) * 100.0
                : 0.0;

            int peakInIdx = Array.IndexOf(inflowCfs.ToArray(), result.PeakInflowCfs);
            var peakOut = result.Points.OrderByDescending(p => p.OutflowCfs).First();
            result.TravelTimeHours = peakOut.TimeHours - peakInIdx * derived.TimestepHours;

            result.Steps.Add(new CalcStep("Q_ref", derived.ReferenceFlowCfs, "cfs", "peak inflow reference"));
            result.Steps.Add(new CalcStep("c", derived.CelerityFps, "ft/s", "(5/3)*V"));
            result.Steps.Add(new CalcStep("K", derived.KHours, "hr", "L/c"));
            result.Steps.Add(new CalcStep("X", derived.X, "-", "Muskingum weight"));
            result.Steps.Add(new CalcStep("C_sum", derived.Sum, "-", "C1+C2+C3"));
            return result;
        }
    }
}