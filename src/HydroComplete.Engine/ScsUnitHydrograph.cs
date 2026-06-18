using System;
using System.Collections.Generic;

namespace HydroComplete.Engine
{
    /// <summary>
    /// NRCS/TR-55 synthetic unit hydrograph from the SCS dimensionless curve.
    /// Public-domain methods from NRCS NEH Part 630, Chapter 16 and TR-55.
    ///
    ///     Tl (hr) = 0.6 * Tc
    ///     Tp (hr) = D/2 + Tl
    ///     qp (cfs) = 484 * A / Tp   (1 in direct runoff, A in sq mi, Tp in hr)
    ///
    /// Default excess-rainfall duration D = 0.133 * Tc (hr) when not supplied.
    /// </summary>
    public static class ScsUnitHydrograph
    {
        /// <summary>TR-55 peak factor (cfs·hr per in·sq mi).</summary>
        public const double PeakFactor = 484.0;

        /// <summary>Lag time as a fraction of Tc: Tl = LagFactor * Tc.</summary>
        public const double LagFactor = 0.6;

        /// <summary>Default D/Tc ratio for excess rainfall duration (hr).</summary>
        public const double DefaultDurationFactor = 0.133;

        /// <summary>Standard hydrograph length as a multiple of Tp.</summary>
        public const double TotalDurationFactor = 5.0;

        /// <summary>Recommended time-step as a fraction of Tp (Δt = Tp/5).</summary>
        public const double DefaultTimeStepFactor = 0.2;

        /// <summary>NRCS dimensionless unit hydrograph (t/Tp, q/qp).</summary>
        public static readonly IReadOnlyList<(double TRatio, double QRatio)> DimensionlessCurve =
            new (double, double)[]
            {
                (0.0, 0.00), (0.1, 0.03), (0.2, 0.10), (0.3, 0.30), (0.4, 0.53),
                (0.5, 0.72), (0.6, 0.86), (0.7, 0.94), (0.8, 0.97), (0.9, 0.99),
                (1.0, 1.00), (1.1, 0.99), (1.2, 0.93), (1.3, 0.86), (1.4, 0.78),
                (1.5, 0.68), (1.6, 0.56), (1.7, 0.46), (1.8, 0.35), (1.9, 0.26),
                (2.0, 0.17), (2.2, 0.07), (2.4, 0.02), (2.6, 0.00),
            };

        public sealed class HydrographOrdinate
        {
            /// <summary>Elapsed time from start of excess rainfall, minutes.</summary>
            public double TimeMinutes { get; set; }

            /// <summary>Discharge ordinate, cfs (for 1 in direct runoff).</summary>
            public double FlowCfs { get; set; }

            /// <summary>Dimensionless time t/Tp.</summary>
            public double TRatio { get; set; }

            /// <summary>Dimensionless discharge q/qp.</summary>
            public double QRatio { get; set; }
        }

        public sealed class UnitHydrographResult : TracedResult
        {
            /// <summary>Drainage area used, acres.</summary>
            public double AreaAcres { get; set; }

            /// <summary>Time of concentration, minutes.</summary>
            public double TcMinutes { get; set; }

            /// <summary>Excess-rainfall duration, minutes.</summary>
            public double DurationMinutes { get; set; }

            /// <summary>Lag time, hours.</summary>
            public double LagHours { get; set; }

            /// <summary>Time to peak, hours.</summary>
            public double TimeToPeakHours { get; set; }

            /// <summary>Time to peak, minutes.</summary>
            public double TimeToPeakMinutes { get; set; }

            /// <summary>Peak discharge for 1 in direct runoff, cfs.</summary>
            public double PeakFlowCfs { get; set; }

            /// <summary>Time step between ordinates, minutes.</summary>
            public double TimeStepMinutes { get; set; }

            public List<HydrographOrdinate> Ordinates { get; } = new List<HydrographOrdinate>();
        }

        /// <summary>Convert acres to square miles.</summary>
        public static double AcresToSqMi(double areaAcres) => areaAcres / 640.0;

        /// <summary>Lag time from Tc: Tl = 0.6 * Tc (hours).</summary>
        public static double LagHours(double tcMinutes)
        {
            if (tcMinutes <= 0) throw new ArgumentOutOfRangeException(nameof(tcMinutes));
            return LagFactor * tcMinutes / 60.0;
        }

        /// <summary>
        /// Excess-rainfall duration in hours. Defaults to 0.133 * Tc when
        /// <paramref name="durationMinutes"/> is null.
        /// </summary>
        public static double DurationHours(double tcMinutes, double? durationMinutes = null)
        {
            if (tcMinutes <= 0) throw new ArgumentOutOfRangeException(nameof(tcMinutes));
            if (durationMinutes.HasValue)
            {
                if (durationMinutes.Value <= 0) throw new ArgumentOutOfRangeException(nameof(durationMinutes));
                return durationMinutes.Value / 60.0;
            }

            return DefaultDurationFactor * tcMinutes / 60.0;
        }

        /// <summary>
        /// Time to peak from Tc: Tp = D/2 + 0.6*Tc (hours).
        /// </summary>
        public static double TimeToPeakHours(double tcMinutes, double? durationMinutes = null)
        {
            double dHr = DurationHours(tcMinutes, durationMinutes);
            double tlHr = LagHours(tcMinutes);
            return dHr / 2.0 + tlHr;
        }

        /// <summary>
        /// Peak discharge for 1 in of direct runoff: qp = 484 * A / Tp.
        /// </summary>
        public static double PeakDischargeCfs(double areaAcres, double timeToPeakHours)
        {
            if (areaAcres <= 0) throw new ArgumentOutOfRangeException(nameof(areaAcres));
            if (timeToPeakHours <= 0) throw new ArgumentOutOfRangeException(nameof(timeToPeakHours));

            double areaSqMi = AcresToSqMi(areaAcres);
            return PeakFactor * areaSqMi / timeToPeakHours;
        }

        /// <summary>
        /// Dimensionless discharge q/qp at normalized time t/Tp (linear interpolation).
        /// </summary>
        public static double DimensionlessFlow(double tRatio)
        {
            if (tRatio < 0) throw new ArgumentOutOfRangeException(nameof(tRatio));

            var curve = DimensionlessCurve;
            if (tRatio >= curve[curve.Count - 1].TRatio)
                return 0.0;

            for (int i = 1; i < curve.Count; i++)
            {
                var (t0, q0) = curve[i - 1];
                var (t1, q1) = curve[i];
                if (tRatio <= t1)
                {
                    if (t1 <= t0) return q1;
                    double f = (tRatio - t0) / (t1 - t0);
                    return q0 + f * (q1 - q0);
                }
            }

            return 0.0;
        }

        /// <summary>
        /// Build a synthetic SCS unit hydrograph (1 in direct runoff) from Tc.
        /// </summary>
        public static UnitHydrographResult Generate(
            double areaAcres,
            double tcMinutes,
            double? durationMinutes = null,
            double? timeStepMinutes = null)
        {
            if (areaAcres <= 0) throw new ArgumentOutOfRangeException(nameof(areaAcres));
            if (tcMinutes <= 0) throw new ArgumentOutOfRangeException(nameof(tcMinutes));
            if (timeStepMinutes.HasValue && timeStepMinutes.Value <= 0)
                throw new ArgumentOutOfRangeException(nameof(timeStepMinutes));

            double dMin = durationMinutes ?? DefaultDurationFactor * tcMinutes;
            if (dMin <= 0) throw new ArgumentOutOfRangeException(nameof(durationMinutes));

            double tpHr = TimeToPeakHours(tcMinutes, dMin);
            double tpMin = tpHr * 60.0;
            double tlHr = LagHours(tcMinutes);
            double qp = PeakDischargeCfs(areaAcres, tpHr);
            double dtMin = timeStepMinutes ?? tpMin * DefaultTimeStepFactor;
            double totalMin = tpMin * TotalDurationFactor;

            var result = new UnitHydrographResult
            {
                AreaAcres = areaAcres,
                TcMinutes = tcMinutes,
                DurationMinutes = dMin,
                LagHours = tlHr,
                TimeToPeakHours = tpHr,
                TimeToPeakMinutes = tpMin,
                PeakFlowCfs = qp,
                TimeStepMinutes = dtMin,
            };

            result.Steps.Add(new CalcStep("Tc", tcMinutes, "min", "time of concentration"));
            result.Steps.Add(new CalcStep("D", dMin, "min", durationMinutes.HasValue
                ? "excess rainfall duration"
                : $"default {DefaultDurationFactor}*Tc"));
            result.Steps.Add(new CalcStep("Tl", tlHr, "hr", $"{LagFactor}*Tc"));
            result.Steps.Add(new CalcStep("Tp", tpHr, "hr", "D/2 + Tl"));
            result.Steps.Add(new CalcStep("qp", qp, "cfs", $"{PeakFactor}*A/Tp  (1 in runoff)"));

            for (double tMin = 0.0; tMin <= totalMin + 1e-9; tMin += dtMin)
            {
                double tRatio = tpMin > 0 ? tMin / tpMin : 0.0;
                double qRatio = DimensionlessFlow(tRatio);
                result.Ordinates.Add(new HydrographOrdinate
                {
                    TimeMinutes = tMin,
                    FlowCfs = qRatio * qp,
                    TRatio = tRatio,
                    QRatio = qRatio,
                });
            }

            return result;
        }
    }
}