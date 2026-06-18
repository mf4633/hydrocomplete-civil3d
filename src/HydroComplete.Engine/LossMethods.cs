using System;
using System.Collections.Generic;
using System.Linq;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Infiltration / loss methods that convert incremental rainfall hyetographs
    /// into excess (direct runoff) depths. Ports hc-refactored hydroCalculations.js
    /// loss dispatchers with timestep-wise accounting.
    /// </summary>
    public static class LossMethods
    {
        public enum LossMethodType
        {
            CurveNumber,
            GreenAmpt,
            Horton,
            InitialConstant,
            ConstantRate,
        }

        public sealed class GreenAmptParameters
        {
            /// <summary>Saturated hydraulic conductivity, in/hr.</summary>
            public double KsInPerHr { get; set; }

            /// <summary>Wetting-front suction head ψ, in.</summary>
            public double PsiIn { get; set; }

            /// <summary>Initial moisture deficit Δθ (dimensionless).</summary>
            public double MoistureDeficit { get; set; }
        }

        public sealed class HortonParameters
        {
            /// <summary>Initial infiltration capacity f₀, in/hr.</summary>
            public double F0InPerHr { get; set; }

            /// <summary>Final infiltration capacity f<sub>c</sub>, in/hr.</summary>
            public double FcInPerHr { get; set; }

            /// <summary>Decay constant k, 1/hr.</summary>
            public double KPerHr { get; set; }
        }

        public sealed class LossParameters
        {
            public LossMethodType Method { get; set; } = LossMethodType.CurveNumber;
            public double CurveNumber { get; set; } = 75.0;
            public double ConstantLossRateInPerHr { get; set; } = 0.15;
            public GreenAmptParameters GreenAmpt { get; set; } = GreenAmptForSoilGroup("B");
            public HortonParameters Horton { get; set; } = HortonForSoilGroup("B");
        }

        public sealed class ExcessRainfallIncrement
        {
            public int Index { get; set; }
            public double RainfallIn { get; set; }
            public double CumulativeRainfallIn { get; set; }
            public double LossIn { get; set; }
            public double CumulativeLossIn { get; set; }
            public double ExcessRainfallIn { get; set; }
            public double CumulativeExcessIn { get; set; }
        }

        public sealed class IncrementalLossResult : TracedResult
        {
            public LossMethodType Method { get; set; }
            public double TimestepHours { get; set; }
            public double TotalRainfallIn { get; set; }
            public double TotalLossIn { get; set; }
            public double TotalExcessIn { get; set; }
            public List<ExcessRainfallIncrement> Increments { get; } = new List<ExcessRainfallIncrement>();
        }

        public static GreenAmptParameters GreenAmptForSoilGroup(string soilGroup)
        {
            switch ((soilGroup ?? "B").ToUpperInvariant())
            {
                case "A":
                case "SANDY":
                    return new GreenAmptParameters { KsInPerHr = 4.74, PsiIn = 2.79, MoistureDeficit = 0.437 };
                case "C":
                    return new GreenAmptParameters { KsInPerHr = 0.60, PsiIn = 10.85, MoistureDeficit = 0.467 };
                case "D":
                case "CLAY":
                    return new GreenAmptParameters { KsInPerHr = 0.30, PsiIn = 12.97, MoistureDeficit = 0.481 };
                case "LOAMY":
                    return new GreenAmptParameters { KsInPerHr = 1.32, PsiIn = 8.74, MoistureDeficit = 0.453 };
                default:
                    return new GreenAmptParameters { KsInPerHr = 1.32, PsiIn = 8.74, MoistureDeficit = 0.453 };
            }
        }

        public static HortonParameters HortonForSoilGroup(string soilGroup)
        {
            switch ((soilGroup ?? "B").ToUpperInvariant())
            {
                case "A":
                    return new HortonParameters { F0InPerHr = 4.50, FcInPerHr = 0.50, KPerHr = 4.14 };
                case "C":
                    return new HortonParameters { F0InPerHr = 2.00, FcInPerHr = 0.10, KPerHr = 3.20 };
                case "D":
                    return new HortonParameters { F0InPerHr = 1.00, FcInPerHr = 0.05, KPerHr = 2.90 };
                default:
                    return new HortonParameters { F0InPerHr = 3.00, FcInPerHr = 0.30, KPerHr = 3.50 };
            }
        }

        /// <summary>
        /// Horton cumulative infiltration F(T) = f<sub>c</sub>T + (f₀-f<sub>c</sub>)/k·(1-e<sup>-kT</sup>).
        /// </summary>
        public static double HortonCumulativeInfiltrationInches(double timeHours, HortonParameters p)
        {
            if (timeHours < 0) throw new ArgumentOutOfRangeException(nameof(timeHours));
            if (p.KPerHr <= 0) throw new ArgumentOutOfRangeException(nameof(p.KPerHr));
            return p.FcInPerHr * timeHours
                + (p.F0InPerHr - p.FcInPerHr) / p.KPerHr * (1.0 - Math.Exp(-p.KPerHr * timeHours));
        }

        /// <summary>
        /// Single-storm Green-Ampt excess depth (simplified hc-refactored form).
        /// </summary>
        public static double GreenAmptExcessDepthInches(double rainfallIn, GreenAmptParameters p)
        {
            if (rainfallIn < 0) throw new ArgumentOutOfRangeException(nameof(rainfallIn));
            double intensity = rainfallIn > 0 ? rainfallIn : 1e-6;
            double f = p.KsInPerHr + p.PsiIn * p.MoistureDeficit * p.KsInPerHr / intensity;
            return Math.Max(0.0, rainfallIn - f);
        }

        /// <summary>
        /// Initial &amp; constant loss: subtract Ia = 0.2S, then constant f<sub>c</sub> over storm duration.
        /// </summary>
        public static double InitialConstantExcessDepthInches(
            double rainfallIn,
            double curveNumber,
            double constantLossInPerHr,
            double durationHours)
        {
            double s = ScsRunoff.MaxRetentionFromCn(curveNumber);
            double ia = ScsRunoff.InitialAbstractionRatio * s;
            double afterIa = Math.Max(0.0, rainfallIn - ia);
            return Math.Max(0.0, afterIa - constantLossInPerHr * durationHours);
        }

        /// <summary>Constant-rate loss: excess = P - f<sub>c</sub>·T.</summary>
        public static double ConstantRateExcessDepthInches(
            double rainfallIn,
            double constantLossInPerHr,
            double durationHours)
            => Math.Max(0.0, rainfallIn - constantLossInPerHr * durationHours);

        /// <summary>
        /// Convert incremental rainfall depths to incremental excess rainfall using the selected loss method.
        /// </summary>
        public static IncrementalLossResult ComputeIncremental(
            IReadOnlyList<double> rainfallIncrementsIn,
            double timestepHours,
            LossParameters parameters)
        {
            if (rainfallIncrementsIn == null) throw new ArgumentNullException(nameof(rainfallIncrementsIn));
            if (timestepHours <= 0) throw new ArgumentOutOfRangeException(nameof(timestepHours));
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));

            switch (parameters.Method)
            {
                case LossMethodType.CurveNumber:
                    return FromScsIncremental(rainfallIncrementsIn, timestepHours, parameters.CurveNumber);
                case LossMethodType.GreenAmpt:
                    return GreenAmptIncremental(rainfallIncrementsIn, timestepHours, parameters.GreenAmpt);
                case LossMethodType.Horton:
                    return HortonIncremental(rainfallIncrementsIn, timestepHours, parameters.Horton);
                case LossMethodType.InitialConstant:
                    return InitialConstantIncremental(
                        rainfallIncrementsIn, timestepHours, parameters.CurveNumber, parameters.ConstantLossRateInPerHr);
                case LossMethodType.ConstantRate:
                    return ConstantRateIncremental(
                        rainfallIncrementsIn, timestepHours, parameters.ConstantLossRateInPerHr);
                default:
                    throw new ArgumentOutOfRangeException(nameof(parameters.Method));
            }
        }

        private static IncrementalLossResult FromScsIncremental(
            IReadOnlyList<double> rainfallIncrementsIn,
            double timestepHours,
            double curveNumber)
        {
            var scs = ScsRunoff.ComputeIncremental(rainfallIncrementsIn, curveNumber);
            var result = new IncrementalLossResult
            {
                Method = LossMethodType.CurveNumber,
                TimestepHours = timestepHours,
                TotalRainfallIn = scs.Increments.Sum(x => x.RainfallIn),
                TotalExcessIn = scs.TotalRunoffIn,
            };

            double cumulativeLoss = 0.0;
            foreach (ScsRunoff.RunoffIncrement row in scs.Increments)
            {
                double loss = row.RainfallIn - row.IncrementalRunoffIn;
                cumulativeLoss += loss;
                result.Increments.Add(new ExcessRainfallIncrement
                {
                    Index = row.Index,
                    RainfallIn = row.RainfallIn,
                    CumulativeRainfallIn = row.CumulativeRainfallIn,
                    LossIn = loss,
                    CumulativeLossIn = cumulativeLoss,
                    ExcessRainfallIn = row.IncrementalRunoffIn,
                    CumulativeExcessIn = row.CumulativeRunoffIn,
                });
            }

            result.TotalLossIn = cumulativeLoss;
            result.Steps.Add(new CalcStep("CN", curveNumber, "", "SCS curve-number loss"));
            result.Steps.Add(new CalcStep("Q_total", result.TotalExcessIn, "in", "sum of incremental excess"));
            return result;
        }

        private static IncrementalLossResult GreenAmptIncremental(
            IReadOnlyList<double> rainfallIncrementsIn,
            double timestepHours,
            GreenAmptParameters p)
        {
            var result = new IncrementalLossResult
            {
                Method = LossMethodType.GreenAmpt,
                TimestepHours = timestepHours,
            };

            double cumulativeRain = 0.0;
            double cumulativeLoss = 0.0;
            double cumulativeExcess = 0.0;
            double cumulativeInfiltration = 0.0;

            for (int i = 0; i < rainfallIncrementsIn.Count; i++)
            {
                double inc = rainfallIncrementsIn[i];
                if (inc < 0) throw new ArgumentOutOfRangeException(nameof(rainfallIncrementsIn));

                cumulativeRain += inc;
                double intensityInPerHr = inc / timestepHours;

                double infiltrationCapacity;
                if (cumulativeInfiltration <= 1e-9)
                {
                    infiltrationCapacity = p.KsInPerHr
                        + p.PsiIn * p.MoistureDeficit * p.KsInPerHr / Math.Max(intensityInPerHr, 1e-6);
                }
                else
                {
                    infiltrationCapacity = p.KsInPerHr
                        * (1.0 + p.PsiIn * p.MoistureDeficit / cumulativeInfiltration);
                }

                double maxInfiltrationDepth = infiltrationCapacity * timestepHours;
                double infiltrationDepth = Math.Min(inc, maxInfiltrationDepth);
                cumulativeInfiltration += infiltrationDepth;

                double excessInc = Math.Max(0.0, inc - infiltrationDepth);
                cumulativeLoss += infiltrationDepth;
                cumulativeExcess += excessInc;

                result.Increments.Add(new ExcessRainfallIncrement
                {
                    Index = i,
                    RainfallIn = inc,
                    CumulativeRainfallIn = cumulativeRain,
                    LossIn = infiltrationDepth,
                    CumulativeLossIn = cumulativeLoss,
                    ExcessRainfallIn = excessInc,
                    CumulativeExcessIn = cumulativeExcess,
                });
            }

            result.TotalRainfallIn = cumulativeRain;
            result.TotalLossIn = cumulativeLoss;
            result.TotalExcessIn = cumulativeExcess;
            result.Steps.Add(new CalcStep("Ks", p.KsInPerHr, "in/hr", "Green-Ampt saturated conductivity"));
            result.Steps.Add(new CalcStep("Q_total", result.TotalExcessIn, "in", "sum of incremental excess"));
            return result;
        }

        private static IncrementalLossResult HortonIncremental(
            IReadOnlyList<double> rainfallIncrementsIn,
            double timestepHours,
            HortonParameters p)
        {
            var result = new IncrementalLossResult
            {
                Method = LossMethodType.Horton,
                TimestepHours = timestepHours,
            };

            double cumulativeRain = 0.0;
            double cumulativeLoss = 0.0;
            double cumulativeExcess = 0.0;
            double elapsedHours = 0.0;
            double prevCumulativeInfiltration = 0.0;

            for (int i = 0; i < rainfallIncrementsIn.Count; i++)
            {
                double inc = rainfallIncrementsIn[i];
                if (inc < 0) throw new ArgumentOutOfRangeException(nameof(rainfallIncrementsIn));

                cumulativeRain += inc;
                elapsedHours += timestepHours;
                double cumulativeInfiltration = HortonCumulativeInfiltrationInches(elapsedHours, p);
                double infiltrationDepth = Math.Min(inc, cumulativeInfiltration - prevCumulativeInfiltration);
                infiltrationDepth = Math.Max(0.0, infiltrationDepth);
                prevCumulativeInfiltration = cumulativeInfiltration;

                double excessInc = Math.Max(0.0, inc - infiltrationDepth);
                cumulativeLoss += infiltrationDepth;
                cumulativeExcess += excessInc;

                result.Increments.Add(new ExcessRainfallIncrement
                {
                    Index = i,
                    RainfallIn = inc,
                    CumulativeRainfallIn = cumulativeRain,
                    LossIn = infiltrationDepth,
                    CumulativeLossIn = cumulativeLoss,
                    ExcessRainfallIn = excessInc,
                    CumulativeExcessIn = cumulativeExcess,
                });
            }

            result.TotalRainfallIn = cumulativeRain;
            result.TotalLossIn = cumulativeLoss;
            result.TotalExcessIn = cumulativeExcess;
            result.Steps.Add(new CalcStep("f0", p.F0InPerHr, "in/hr", "Horton initial capacity"));
            result.Steps.Add(new CalcStep("fc", p.FcInPerHr, "in/hr", "Horton final capacity"));
            result.Steps.Add(new CalcStep("Q_total", result.TotalExcessIn, "in", "sum of incremental excess"));
            return result;
        }

        private static IncrementalLossResult InitialConstantIncremental(
            IReadOnlyList<double> rainfallIncrementsIn,
            double timestepHours,
            double curveNumber,
            double constantLossInPerHr)
        {
            var result = new IncrementalLossResult
            {
                Method = LossMethodType.InitialConstant,
                TimestepHours = timestepHours,
            };

            double s = ScsRunoff.MaxRetentionFromCn(curveNumber);
            double ia = ScsRunoff.InitialAbstractionRatio * s;
            double cumulativeRain = 0.0;
            double cumulativeLoss = 0.0;
            double cumulativeExcess = 0.0;
            double elapsedHours = 0.0;
            double prevTotalExcess = 0.0;

            for (int i = 0; i < rainfallIncrementsIn.Count; i++)
            {
                double inc = rainfallIncrementsIn[i];
                if (inc < 0) throw new ArgumentOutOfRangeException(nameof(rainfallIncrementsIn));

                cumulativeRain += inc;
                elapsedHours += timestepHours;
                double totalExcess = InitialConstantExcessDepthInches(
                    cumulativeRain, curveNumber, constantLossInPerHr, elapsedHours);
                double excessInc = Math.Max(0.0, totalExcess - prevTotalExcess);
                prevTotalExcess = totalExcess;

                double lossInc = inc - excessInc;
                cumulativeLoss += lossInc;
                cumulativeExcess += excessInc;

                result.Increments.Add(new ExcessRainfallIncrement
                {
                    Index = i,
                    RainfallIn = inc,
                    CumulativeRainfallIn = cumulativeRain,
                    LossIn = lossInc,
                    CumulativeLossIn = cumulativeLoss,
                    ExcessRainfallIn = excessInc,
                    CumulativeExcessIn = cumulativeExcess,
                });
            }

            result.TotalRainfallIn = cumulativeRain;
            result.TotalLossIn = cumulativeLoss;
            result.TotalExcessIn = cumulativeExcess;
            result.Steps.Add(new CalcStep("Ia", ia, "in", "0.2*S initial abstraction"));
            result.Steps.Add(new CalcStep("fc", constantLossInPerHr, "in/hr", "constant loss rate"));
            result.Steps.Add(new CalcStep("Q_total", result.TotalExcessIn, "in", "sum of incremental excess"));
            return result;
        }

        private static IncrementalLossResult ConstantRateIncremental(
            IReadOnlyList<double> rainfallIncrementsIn,
            double timestepHours,
            double constantLossInPerHr)
        {
            var result = new IncrementalLossResult
            {
                Method = LossMethodType.ConstantRate,
                TimestepHours = timestepHours,
            };

            double cumulativeRain = 0.0;
            double cumulativeLoss = 0.0;
            double cumulativeExcess = 0.0;
            double elapsedHours = 0.0;
            double prevTotalExcess = 0.0;

            for (int i = 0; i < rainfallIncrementsIn.Count; i++)
            {
                double inc = rainfallIncrementsIn[i];
                if (inc < 0) throw new ArgumentOutOfRangeException(nameof(rainfallIncrementsIn));

                cumulativeRain += inc;
                elapsedHours += timestepHours;
                double totalExcess = ConstantRateExcessDepthInches(
                    cumulativeRain, constantLossInPerHr, elapsedHours);
                double excessInc = Math.Max(0.0, totalExcess - prevTotalExcess);
                prevTotalExcess = totalExcess;

                double lossInc = inc - excessInc;
                cumulativeLoss += lossInc;
                cumulativeExcess += excessInc;

                result.Increments.Add(new ExcessRainfallIncrement
                {
                    Index = i,
                    RainfallIn = inc,
                    CumulativeRainfallIn = cumulativeRain,
                    LossIn = lossInc,
                    CumulativeLossIn = cumulativeLoss,
                    ExcessRainfallIn = excessInc,
                    CumulativeExcessIn = cumulativeExcess,
                });
            }

            result.TotalRainfallIn = cumulativeRain;
            result.TotalLossIn = cumulativeLoss;
            result.TotalExcessIn = cumulativeExcess;
            result.Steps.Add(new CalcStep("fc", constantLossInPerHr, "in/hr", "constant loss rate"));
            result.Steps.Add(new CalcStep("Q_total", result.TotalExcessIn, "in", "sum of incremental excess"));
            return result;
        }
    }
}