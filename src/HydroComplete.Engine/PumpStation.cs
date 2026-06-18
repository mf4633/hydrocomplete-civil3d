using System;
using System.Collections.Generic;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Pump station duty-point check — system head vs pump curve (Hydraflow-style steady-state).
    /// </summary>
    public static class PumpStation
    {
        public sealed class CurvePoint
        {
            public double FlowCfs { get; set; }
            public double HeadFt { get; set; }
        }

        public sealed class DutyResult : TracedResult
        {
            public double DesignFlowCfs { get; set; }
            public double StaticHeadFt { get; set; }
            public double FrictionHeadFt { get; set; }
            public double SystemHeadFt { get; set; }
            public double PumpHeadFt { get; set; }
            public double HeadMarginFt { get; set; }
            public bool Ok { get; set; }
        }

        /// <summary>Linear interpolation of pump total dynamic head at design flow.</summary>
        public static double InterpolatePumpHead(IReadOnlyList<CurvePoint> curve, double flowCfs)
        {
            if (curve == null || curve.Count == 0) return 0;
            if (curve.Count == 1) return curve[0].HeadFt;

            var sorted = new List<CurvePoint>(curve);
            sorted.Sort((a, b) => a.FlowCfs.CompareTo(b.FlowCfs));

            if (flowCfs <= sorted[0].FlowCfs) return sorted[0].HeadFt;
            if (flowCfs >= sorted[sorted.Count - 1].FlowCfs) return sorted[sorted.Count - 1].HeadFt;

            for (int i = 1; i < sorted.Count; i++)
            {
                CurvePoint lo = sorted[i - 1];
                CurvePoint hi = sorted[i];
                if (flowCfs < lo.FlowCfs || flowCfs > hi.FlowCfs) continue;

                double span = hi.FlowCfs - lo.FlowCfs;
                if (span <= 0) return lo.HeadFt;
                double t = (flowCfs - lo.FlowCfs) / span;
                return lo.HeadFt + t * (hi.HeadFt - lo.HeadFt);
            }

            return sorted[sorted.Count - 1].HeadFt;
        }

        /// <summary>
        /// System head = static lift (discharge − suction invert) + Manning friction in force main.
        /// </summary>
        public static DutyResult CheckDuty(
            double designFlowCfs,
            double suctionInvertFt,
            double dischargeInvertFt,
            double forceMainLengthFt,
            double forceMainDiameterFt,
            double manningN,
            IReadOnlyList<CurvePoint> pumpCurve)
        {
            var result = new DutyResult { DesignFlowCfs = designFlowCfs };
            result.Steps.Add(new CalcStep("Design Q", designFlowCfs, "cfs", "Pump duty flow"));

            double staticHead = Math.Max(0, dischargeInvertFt - suctionInvertFt);
            result.StaticHeadFt = staticHead;
            result.Steps.Add(new CalcStep("Static head", staticHead, "ft", "Discharge invert − suction invert"));

            double friction = 0;
            if (forceMainLengthFt > 0 && forceMainDiameterFt > 0 && designFlowCfs > 0)
            {
                double area = Math.PI * forceMainDiameterFt * forceMainDiameterFt / 4.0;
                double velocity = designFlowCfs / area;
                double hydraulicRadius = forceMainDiameterFt / 4.0;
                friction = manningN * manningN * forceMainLengthFt * velocity * velocity
                    / (2.22 * Math.Pow(hydraulicRadius, 4.0 / 3.0));
                result.Steps.Add(new CalcStep("Force-main friction", friction, "ft",
                    "Manning n² L V² / (2.22 R^(4/3))"));
            }

            result.FrictionHeadFt = friction;
            result.SystemHeadFt = staticHead + friction;
            result.PumpHeadFt = InterpolatePumpHead(pumpCurve, designFlowCfs);
            result.HeadMarginFt = result.PumpHeadFt - result.SystemHeadFt;
            result.Ok = result.PumpHeadFt >= result.SystemHeadFt && designFlowCfs > 0;
            result.Steps.Add(new CalcStep("Pump head @ Q", result.PumpHeadFt, "ft", "Curve interpolation"));
            result.Steps.Add(new CalcStep("Margin", result.HeadMarginFt, "ft", "Pump head − system head"));

            return result;
        }

        /// <summary>Default single-duty pump curve (50 cfs @ 40 ft, 30 cfs @ 55 ft).</summary>
        public static IReadOnlyList<CurvePoint> DefaultCurve()
        {
            return new[]
            {
                new CurvePoint { FlowCfs = 0, HeadFt = 60 },
                new CurvePoint { FlowCfs = 30, HeadFt = 55 },
                new CurvePoint { FlowCfs = 50, HeadFt = 40 },
            };
        }
    }
}