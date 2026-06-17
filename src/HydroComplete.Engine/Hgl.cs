using System;
using System.Collections.Generic;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Steady HGL / EGL stepping for open-channel network reaches via inverted Manning.
    ///
    ///     S_f = [ n * Q / (1.486 * A * R^(2/3)) ]^2
    ///     h_f = S_f * L
    ///     ΔEGL = h_f + (Vh_up - Vh_down)
    ///
    /// US customary units (ft, cfs). Public-domain; see e.g. Chow, FHWA HEC-22.
    /// </summary>
    public static class Hgl
    {
        public sealed class FrictionHeadLossResult : TracedResult
        {
            /// <summary>Friction head loss over the reach, ft.</summary>
            public double HfFt { get; set; }
        }

        public sealed class EnergyGradeLineStepResult : TracedResult
        {
            /// <summary>Energy grade line drop over the reach, ft.</summary>
            public double DeltaEglFt { get; set; }
        }

        /// <summary>
        /// Manning friction head loss over a reach length.
        /// </summary>
        public static FrictionHeadLossResult ManningFrictionHeadLoss(
            double qCfs, double n, double areaFt2, double hydRadiusFt, double lengthFt)
        {
            if (qCfs < 0) throw new ArgumentOutOfRangeException(nameof(qCfs), "Q must be >= 0.");
            if (n <= 0) throw new ArgumentOutOfRangeException(nameof(n), "n must be > 0.");
            if (areaFt2 <= 0) throw new ArgumentOutOfRangeException(nameof(areaFt2), "Area must be > 0.");
            if (hydRadiusFt <= 0) throw new ArgumentOutOfRangeException(nameof(hydRadiusFt), "R must be > 0.");
            if (lengthFt <= 0) throw new ArgumentOutOfRangeException(nameof(lengthFt), "Length must be > 0.");

            double sf = Math.Pow(n * qCfs / (Manning.Kn * areaFt2 * Math.Pow(hydRadiusFt, 2.0 / 3.0)), 2.0);
            double hf = sf * lengthFt;

            var r = new FrictionHeadLossResult { HfFt = hf };
            r.Steps.Add(new CalcStep("S_f", sf, "ft/ft", "[n*Q/(1.486*A*R^(2/3))]^2"));
            r.Steps.Add(new CalcStep("h_f", hf, "ft", "S_f*L"));
            return r;
        }

        /// <summary>
        /// Full energy grade line step: friction loss plus velocity-head change.
        /// </summary>
        public static EnergyGradeLineStepResult EnergyGradeLineStep(
            double qCfs, double n, double areaFt2, double hydRadiusFt, double lengthFt,
            double velHeadUpFt = 0.0, double velHeadDownFt = 0.0)
        {
            var friction = ManningFrictionHeadLoss(qCfs, n, areaFt2, hydRadiusFt, lengthFt);
            double deltaVh = velHeadUpFt - velHeadDownFt;
            double deltaEgl = friction.HfFt + deltaVh;

            var r = new EnergyGradeLineStepResult { DeltaEglFt = deltaEgl };
            foreach (var step in friction.Steps)
                r.Steps.Add(step);
            r.Steps.Add(new CalcStep("ΔVh", deltaVh, "ft", "Vh_up - Vh_down"));
            r.Steps.Add(new CalcStep("ΔEGL", deltaEgl, "ft", "h_f + ΔVh"));
            return r;
        }

        /// <summary>
        /// Steady (uniform-flow per reach) HGL/EGL profile stepping downstream from
        /// <paramref name="startHglFt"/>.
        /// </summary>
        public static List<HglProfilePoint> SteadyNetworkHglProfile(
            IReadOnlyList<NetworkReach> reaches, double startHglFt)
        {
            if (reaches == null) throw new ArgumentNullException(nameof(reaches));

            var profile = new List<HglProfilePoint>();
            if (reaches.Count == 0)
                return profile;

            double cumLengthFt = 0.0;
            double hglFt = startHglFt;
            double eglFt = startHglFt;

            for (int idx = 0; idx < reaches.Count; idx++)
            {
                NetworkReach reach = reaches[idx];
                if (reach == null) throw new ArgumentException("Reach must not be null.", nameof(reaches));

                double lengthFt = reach.LengthFt;
                double n = reach.ManningN;
                double areaFt2 = reach.AreaFt2;
                double hydRadiusFt = reach.HydRadiusFt;
                double flowCfs = reach.FlowCfs;
                double velHeadUpFt = reach.VelHeadUpFt;
                double velHeadDownFt = reach.VelHeadDownFt;

                if (lengthFt <= 0) throw new ArgumentOutOfRangeException(nameof(reaches), "Length must be > 0.");
                if (n <= 0) throw new ArgumentOutOfRangeException(nameof(reaches), "Manning n must be > 0.");
                if (areaFt2 <= 0) throw new ArgumentOutOfRangeException(nameof(reaches), "Area must be > 0.");
                if (hydRadiusFt <= 0) throw new ArgumentOutOfRangeException(nameof(reaches), "Hydraulic radius must be > 0.");
                if (flowCfs < 0) throw new ArgumentOutOfRangeException(nameof(reaches), "Flow must be >= 0.");

                var friction = ManningFrictionHeadLoss(flowCfs, n, areaFt2, hydRadiusFt, lengthFt);
                var eglStep = EnergyGradeLineStep(
                    flowCfs, n, areaFt2, hydRadiusFt, lengthFt, velHeadUpFt, velHeadDownFt);

                hglFt -= friction.HfFt;
                eglFt -= eglStep.DeltaEglFt;
                cumLengthFt += lengthFt;

                var point = new HglProfilePoint
                {
                    ReachIndex = idx,
                    CumLengthFt = cumLengthFt,
                    HglFt = hglFt,
                    EglFt = eglFt,
                    HfFt = friction.HfFt,
                    DeltaEglFt = eglStep.DeltaEglFt,
                };

                if (!string.IsNullOrEmpty(reach.Name))
                    point.Steps.Add(new CalcStep("reach", idx, "-", reach.Name));

                foreach (var step in friction.Steps)
                    point.Steps.Add(step);
                foreach (var step in eglStep.Steps)
                {
                    if (step.Label == "h_f" || step.Label == "S_f")
                        continue;
                    point.Steps.Add(step);
                }
                point.Steps.Add(new CalcStep("HGL", hglFt, "ft", "HGL_up - h_f"));
                point.Steps.Add(new CalcStep("EGL", eglFt, "ft", "EGL_up - ΔEGL"));

                profile.Add(point);
            }

            return profile;
        }
    }
}