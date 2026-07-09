using System;
using System.Collections.Generic;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Snyder (1938) synthetic unit hydrograph for ungaged basins.
    ///
    ///     t<sub>p</sub> (hr) = C<sub>t</sub> · (L · L<sub>c</sub>)<sup>0.3</sup>
    ///     q<sub>p</sub> (cfs/in) = C<sub>p</sub> · 640 · A<sub>mi²</sub> / t<sub>p</sub>
    ///
    /// Widths at 50% and 75% of peak height (hours): W<sub>50</sub> = 2.14·t<sub>p</sub>,
    /// W<sub>75</sub> = 1.37·t<sub>p</sub> (standard dimensionless-UH scaling).
    /// </summary>
    public static class SnyderUnitHydrograph
    {
        public const double DefaultCt = 1.8;
        public const double DefaultCp = 0.6;
        public const double ChannelLengthFactor = 1.5;
        public const double CentroidDistanceFactor = 0.5;

        public sealed class HydrographOrdinate
        {
            public double TimeHours { get; set; }
            public double FlowCfs { get; set; }
            public double RelativeTime { get; set; }
            public double RelativeFlow { get; set; }
        }

        public sealed class UnitHydrographResult : TracedResult
        {
            public double AreaAcres { get; set; }
            public double ChannelLengthMi { get; set; }
            public double CentroidDistanceMi { get; set; }
            public double Ct { get; set; }
            public double Cp { get; set; }
            public double LagHours { get; set; }
            public double TimeToPeakHours { get; set; }
            public double BaseTimeHours { get; set; }
            public double Width50Hours { get; set; }
            public double Width75Hours { get; set; }
            public double PeakFlowCfs { get; set; }
            public double TimeStepHours { get; set; }
            public List<HydrographOrdinate> Ordinates { get; } = new List<HydrographOrdinate>();
        }

        /// <summary>Estimate main-channel length (mi) from drainage area (ac).</summary>
        public static double EstimateChannelLengthMi(double areaAcres)
            => ChannelLengthFactor * Math.Pow(ScsUnitHydrograph.AcresToSqMi(areaAcres), 0.6);

        /// <summary>Snyder basin lag t<sub>p</sub> (hours).</summary>
        public static double LagHours(double channelLengthMi, double centroidDistanceMi, double ct = DefaultCt)
        {
            if (channelLengthMi <= 0) throw new ArgumentOutOfRangeException(nameof(channelLengthMi));
            if (centroidDistanceMi <= 0) throw new ArgumentOutOfRangeException(nameof(centroidDistanceMi));
            if (ct <= 0) throw new ArgumentOutOfRangeException(nameof(ct));
            return ct * Math.Pow(channelLengthMi * centroidDistanceMi, 0.3);
        }

        /// <summary>Peak discharge for 1 in direct runoff (cfs).</summary>
        public static double PeakDischargeCfs(double areaAcres, double lagHours, double cp = DefaultCp)
        {
            if (areaAcres <= 0) throw new ArgumentOutOfRangeException(nameof(areaAcres));
            if (lagHours <= 0) throw new ArgumentOutOfRangeException(nameof(lagHours));
            if (cp <= 0) throw new ArgumentOutOfRangeException(nameof(cp));

            double areaSqMi = ScsUnitHydrograph.AcresToSqMi(areaAcres);
            return cp * 640.0 * areaSqMi / lagHours;
        }

        /// <summary>Hydrograph width at 50% of peak height (hours).</summary>
        public static double Width50Hours(double lagHours) => 2.14 * lagHours;

        /// <summary>Hydrograph width at 75% of peak height (hours).</summary>
        public static double Width75Hours(double lagHours) => 1.37 * lagHours;

        /// <summary>Base time (hours) for triangular approximation.</summary>
        public static double BaseTimeHours(double lagHours) => Math.Max(5.0 * lagHours, 3.0);

        /// <summary>
        /// Build a Snyder synthetic unit hydrograph (1 in direct runoff).
        /// Uses a triangular shape rising to t<sub>p</sub> and receding to t<sub>b</sub>.
        /// </summary>
        public static UnitHydrographResult Generate(
            double areaAcres,
            double? channelLengthMi = null,
            double? centroidDistanceMi = null,
            double ct = DefaultCt,
            double cp = DefaultCp,
            double? timeStepHours = null)
        {
            if (areaAcres <= 0) throw new ArgumentOutOfRangeException(nameof(areaAcres));

            double lMi = channelLengthMi ?? EstimateChannelLengthMi(areaAcres);
            double lcMi = centroidDistanceMi ?? lMi * CentroidDistanceFactor;
            double tp = LagHours(lMi, lcMi, ct);
            double qp = PeakDischargeCfs(areaAcres, tp, cp);
            // Base time set so the triangular UH encloses exactly one inch of direct runoff:
            // V = 0.5*qp*tb must equal one unit of runoff over the area. With qp = 640*Cp*A/tp,
            // that gives tb = 2.0167*tp/Cp (the old max(5*tp,3) overstated the volume by ~49%
            // at Cp=0.6). Peak qp still occurs at tp, so peak magnitude and timing are unchanged.
            double tb = 2.0167 * tp / cp;
            double dt = timeStepHours ?? Math.Max(tp / 10.0, 0.05);
            double recessionHours = Math.Max(tb - tp, dt);

            var result = new UnitHydrographResult
            {
                AreaAcres = areaAcres,
                ChannelLengthMi = lMi,
                CentroidDistanceMi = lcMi,
                Ct = ct,
                Cp = cp,
                LagHours = tp,
                TimeToPeakHours = tp,
                BaseTimeHours = tb,
                Width50Hours = Width50Hours(tp),
                Width75Hours = Width75Hours(tp),
                PeakFlowCfs = qp,
                TimeStepHours = dt,
            };

            result.Steps.Add(new CalcStep("L", lMi, "mi", "main channel length"));
            result.Steps.Add(new CalcStep("Lc", lcMi, "mi", "distance centroid to outlet"));
            result.Steps.Add(new CalcStep("tp", tp, "hr", "Ct*(L*Lc)^0.3"));
            result.Steps.Add(new CalcStep("W50", result.Width50Hours, "hr", "2.14*tp"));
            result.Steps.Add(new CalcStep("W75", result.Width75Hours, "hr", "1.37*tp"));
            result.Steps.Add(new CalcStep("qp", qp, "cfs", "Cp*640*A/tp  (1 in runoff)"));

            for (double t = 0.0; t <= tb + 1e-9; t += dt)
            {
                double q;
                double rel;
                if (t <= tp)
                {
                    rel = tp > 0 ? t / tp : 0.0;
                    q = qp * rel;
                }
                else
                {
                    rel = recessionHours > 0 ? 1.0 - (t - tp) / recessionHours : 0.0;
                    q = qp * Math.Max(0.0, rel);
                }

                result.Ordinates.Add(new HydrographOrdinate
                {
                    TimeHours = t,
                    FlowCfs = q,
                    RelativeTime = tp > 0 ? t / tp : 0.0,
                    RelativeFlow = qp > 0 ? q / qp : 0.0,
                });
            }

            return result;
        }
    }
}