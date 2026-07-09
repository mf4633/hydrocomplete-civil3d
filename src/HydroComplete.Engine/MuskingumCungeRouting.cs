using System;
using System.Collections.Generic;
using System.Linq;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Variable-parameter Muskingum-Cunge channel routing (Cunge, 1969).
    /// Derives K and X from trapezoidal channel geometry at a reference discharge.
    ///
    /// The Muskingum finite-difference recurrence is only valid when the coefficient
    /// timestep equals the timestep at which the recurrence is stepped. To keep the
    /// output on the SAME time axis as the input hydrograph (Δt = dtHours) while still
    /// satisfying the stability window [2KX, 2K(1-X)], each data interval is advanced in
    /// an integer number of equal sub-steps (Δτ = Δt / subSteps) with the inflow linearly
    /// interpolated across the interval. Output is recorded only at the data times.
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
            /// <summary>Data timestep (hours) — the axis output points are stamped on.</summary>
            public double TimestepHours { get; set; }
            /// <summary>Number of stable sub-steps the coefficients are computed for, per data interval.</summary>
            public int SubSteps { get; set; } = 1;
            /// <summary>Sub-step length (seconds) the C-coefficients were derived at (= dt/SubSteps).</summary>
            public double SubStepSeconds { get; set; }
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

            // Keep the recurrence on the data timestep by advancing each data interval in an
            // integer number of equal sub-steps, each within the stability window. dtMax = 2K(1-X)
            // is the largest stable step; pick the fewest sub-steps that bring Δτ <= dtMax. Because
            // X <= 0.5, dtMax >= dtMin = 2KX, so Δτ ~ dtMax also satisfies the lower bound.
            double dtMax = 2.0 * kSeconds * (1.0 - x);
            int subSteps = 1;
            if (dtMax > 0 && dtSeconds > dtMax)
                subSteps = (int)Math.Ceiling(dtSeconds / dtMax);
            if (subSteps < 1) subSteps = 1;
            double subDtSeconds = dtSeconds / subSteps;

            double denom = 2.0 * kSeconds * (1.0 - x) + subDtSeconds;
            double c1 = (subDtSeconds - 2.0 * kSeconds * x) / denom;
            double c2 = (subDtSeconds + 2.0 * kSeconds * x) / denom;
            double c3 = (2.0 * kSeconds * (1.0 - x) - subDtSeconds) / denom;

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
                TimestepHours = dtHours,
                SubSteps = subSteps,
                SubStepSeconds = subDtSeconds,
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

            int subSteps = Math.Max(1, derived.SubSteps);
            double c1 = derived.C1, c2 = derived.C2, c3 = derived.C3;

            // State carried across sub-steps: previous sub-step inflow and outflow.
            double iSubPrev = inflowCfs[0];
            double oSubPrev = inflowCfs[0];

            result.Points.Add(new HydrographPoint
            {
                TimeHours = 0.0,
                InflowCfs = inflowCfs[0],
                OutflowCfs = oSubPrev,
            });

            // Advance each data interval [i-1, i] with the inflow linearly interpolated across
            // subSteps sub-steps; record only the outflow at the data time i * dtHours.
            for (int i = 1; i < inflowCfs.Count; i++)
            {
                double iStart = inflowCfs[i - 1];
                double iEnd = inflowCfs[i];
                for (int s = 1; s <= subSteps; s++)
                {
                    double iSubCurr = iStart + (iEnd - iStart) * ((double)s / subSteps);
                    double oSubCurr = Math.Max(0.0, c1 * iSubCurr + c2 * iSubPrev + c3 * oSubPrev);
                    iSubPrev = iSubCurr;
                    oSubPrev = oSubCurr;
                }

                result.Points.Add(new HydrographPoint
                {
                    TimeHours = i * derived.TimestepHours,
                    InflowCfs = iEnd,
                    OutflowCfs = oSubPrev,
                });
            }

            // Recession tail: inflow held at zero, advanced on the same data timestep.
            int tailStep = inflowCfs.Count;
            int maxTail = 2000;
            while (oSubPrev > 0.01 && maxTail-- > 0)
            {
                for (int s = 0; s < subSteps; s++)
                {
                    double oSubCurr = Math.Max(0.0, c1 * 0.0 + c2 * iSubPrev + c3 * oSubPrev);
                    iSubPrev = 0.0;
                    oSubPrev = oSubCurr;
                }

                result.Points.Add(new HydrographPoint
                {
                    TimeHours = tailStep * derived.TimestepHours,
                    InflowCfs = 0.0,
                    OutflowCfs = oSubPrev,
                });
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
            result.Steps.Add(new CalcStep("subSteps", derived.SubSteps, "-", "stable sub-steps per Δt"));
            result.Steps.Add(new CalcStep("C_sum", derived.Sum, "-", "C1+C2+C3"));
            return result;
        }
    }
}
