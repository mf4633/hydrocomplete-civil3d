using System;
using System.Collections.Generic;
using System.Linq;

namespace HydroComplete.Engine
{
    /// <summary>
    /// TR-20 style hydrograph generation: incremental excess rainfall convolved with a
    /// unit hydrograph. Ports HydraflowEngine.js convolveHydrograph / generateTR20Hydrograph.
    /// </summary>
    public static class HydrographConvolution
    {
        public sealed class HydrographOrdinate
        {
            public double TimeHours { get; set; }
            public double FlowCfs { get; set; }
        }

        public sealed class UnitHydrographInput
        {
            public double TimeHours { get; set; }
            public double FlowCfsPerIn { get; set; }
        }

        public sealed class ConvolutionResult : TracedResult
        {
            public double AreaAcres { get; set; }
            public double TimestepHours { get; set; }
            public double TotalExcessRainfallIn { get; set; }
            public double PeakFlowCfs { get; set; }
            public double TimeToPeakHours { get; set; }
            public double VolumeAcreFt { get; set; }
            public List<HydrographOrdinate> Ordinates { get; } = new List<HydrographOrdinate>();
        }

        public enum UnitHydrographMethod
        {
            Scs,
            Snyder,
            Clark,
        }

        /// <summary>Interpolate unit-hydrograph ordinate (cfs per in) at elapsed time (hours).</summary>
        public static double InterpolateUnitHydrograph(
            IReadOnlyList<UnitHydrographInput> ordinates,
            double elapsedHours)
        {
            if (ordinates == null || ordinates.Count == 0) return 0.0;
            if (elapsedHours < ordinates[0].TimeHours) return 0.0;
            if (elapsedHours >= ordinates[ordinates.Count - 1].TimeHours) return 0.0;

            for (int i = 0; i < ordinates.Count - 1; i++)
            {
                double t1 = ordinates[i].TimeHours;
                double t2 = ordinates[i + 1].TimeHours;
                if (elapsedHours >= t1 && elapsedHours <= t2)
                {
                    double q1 = ordinates[i].FlowCfsPerIn;
                    double q2 = ordinates[i + 1].FlowCfsPerIn;
                    double w = t2 > t1 ? (elapsedHours - t1) / (t2 - t1) : 0.0;
                    return q1 + w * (q2 - q1);
                }
            }

            return 0.0;
        }

        /// <summary>
        /// Discrete convolution: Q(t) = Σ ΔQ<sub>j</sub> · u(t - t<sub>j</sub>).
        /// </summary>
        public static ConvolutionResult Convolve(
            IReadOnlyList<double> excessRainfallIn,
            double excessStartTimeHours,
            double timestepHours,
            IReadOnlyList<UnitHydrographInput> unitHydroOrdinates,
            double areaAcres)
        {
            if (excessRainfallIn == null) throw new ArgumentNullException(nameof(excessRainfallIn));
            if (unitHydroOrdinates == null || unitHydroOrdinates.Count == 0)
                throw new ArgumentOutOfRangeException(nameof(unitHydroOrdinates));
            if (timestepHours <= 0) throw new ArgumentOutOfRangeException(nameof(timestepHours));
            if (areaAcres <= 0) throw new ArgumentOutOfRangeException(nameof(areaAcres));

            double maxUhTime = unitHydroOrdinates[unitHydroOrdinates.Count - 1].TimeHours;
            double stormEnd = excessStartTimeHours + excessRainfallIn.Count * timestepHours;
            double maxTime = stormEnd + maxUhTime;
            double dt = timestepHours;
            int outSteps = (int)Math.Ceiling(maxTime / dt) + 1;
            var flows = new double[outSteps];

            for (int j = 0; j < excessRainfallIn.Count; j++)
            {
                double excess = excessRainfallIn[j];
                if (excess <= 0.0) continue;

                double startTime = excessStartTimeHours + j * timestepHours;
                for (int k = 0; k < unitHydroOrdinates.Count - 1; k++)
                {
                    double uhTime = unitHydroOrdinates[k].TimeHours;
                    int outIdx = (int)Math.Round((startTime + uhTime) / dt);
                    if (outIdx >= 0 && outIdx < outSteps)
                        flows[outIdx] += excess * unitHydroOrdinates[k].FlowCfsPerIn;
                }
            }

            var result = new ConvolutionResult
            {
                AreaAcres = areaAcres,
                TimestepHours = dt,
                TotalExcessRainfallIn = excessRainfallIn.Sum(),
            };

            for (int i = 0; i < outSteps; i++)
            {
                double t = i * dt;
                double q = Math.Max(0.0, flows[i]);
                if (q > 0.001 || t < 1.0)
                {
                    result.Ordinates.Add(new HydrographOrdinate { TimeHours = t, FlowCfs = q });
                }
            }

            if (result.Ordinates.Count == 0)
                result.Ordinates.Add(new HydrographOrdinate { TimeHours = 0.0, FlowCfs = 0.0 });

            var peak = result.Ordinates.OrderByDescending(o => o.FlowCfs).First();
            result.PeakFlowCfs = peak.FlowCfs;
            result.TimeToPeakHours = peak.TimeHours;
            result.VolumeAcreFt = MuskingumRouting.HydrographVolumeAcreFt(
                result.Ordinates.Select(o => o.FlowCfs).ToList(), dt);

            result.Steps.Add(new CalcStep("sum_dQ", result.TotalExcessRainfallIn, "in", "total excess rainfall"));
            result.Steps.Add(new CalcStep("Q_peak", result.PeakFlowCfs, "cfs", "convolved peak"));
            result.Steps.Add(new CalcStep("t_peak", result.TimeToPeakHours, "hr", "time to peak"));
            return result;
        }

        /// <summary>Build unit-hydrograph ordinates from area, Tc, and selected method.</summary>
        public static List<UnitHydrographInput> BuildUnitHydrograph(
            UnitHydrographMethod method,
            double areaAcres,
            double tcMinutes,
            double timestepHours)
        {
            switch (method)
            {
                case UnitHydrographMethod.Snyder:
                    return SnyderUnitHydrograph.Generate(areaAcres, timeStepHours: timestepHours)
                        .Ordinates.Select(o => new UnitHydrographInput
                        {
                            TimeHours = o.TimeHours,
                            FlowCfsPerIn = o.FlowCfs,
                        }).ToList();

                case UnitHydrographMethod.Clark:
                    return ClarkUnitHydrograph.Generate(areaAcres, tcMinutes, timestepMinutes: timestepHours * 60.0)
                        .Ordinates.Select(o => new UnitHydrographInput
                        {
                            TimeHours = o.TimeMinutes / 60.0,
                            FlowCfsPerIn = o.FlowCfs,
                        }).ToList();

                default:
                    return ScsUnitHydrograph.Generate(areaAcres, tcMinutes, timeStepMinutes: timestepHours * 60.0)
                        .Ordinates.Select(o => new UnitHydrographInput
                        {
                            TimeHours = o.TimeMinutes / 60.0,
                            FlowCfsPerIn = o.FlowCfs,
                        }).ToList();
            }
        }

        /// <summary>
        /// Full TR-20 pipeline: Type II hyetograph → loss → SCS UH → convolution.
        /// </summary>
        public static ConvolutionResult GenerateTr20Hydrograph(
            double areaAcres,
            double curveNumber,
            double tcMinutes,
            double totalRainfallIn,
            double timestepHours = 0.1,
            UnitHydrographMethod unitHydroMethod = UnitHydrographMethod.Scs,
            LossMethods.LossMethodType lossMethod = LossMethods.LossMethodType.CurveNumber)
        {
            if (curveNumber <= 0 || curveNumber > 100)
                throw new ArgumentOutOfRangeException(nameof(curveNumber));
            if (totalRainfallIn < 0) throw new ArgumentOutOfRangeException(nameof(totalRainfallIn));

            var hyetograph = StormHyetograph.TypeIIUniform(totalRainfallIn, 24.0, timestepHours);
            var rainfall = hyetograph.Increments.Select(x => x.DepthIn).ToList();

            var lossParams = new LossMethods.LossParameters
            {
                Method = lossMethod,
                CurveNumber = curveNumber,
            };
            LossMethods.IncrementalLossResult losses =
                LossMethods.ComputeIncremental(rainfall, timestepHours, lossParams);

            var excess = losses.Increments.Select(x => x.ExcessRainfallIn).ToList();
            List<UnitHydrographInput> uh = BuildUnitHydrograph(unitHydroMethod, areaAcres, tcMinutes, timestepHours);

            ConvolutionResult result = Convolve(excess, 0.0, timestepHours, uh, areaAcres);
            result.Steps.Insert(0, new CalcStep("CN", curveNumber, "", "curve number"));
            result.Steps.Insert(1, new CalcStep("P", totalRainfallIn, "in", "24-hr design storm"));
            result.Steps.Insert(2, new CalcStep("loss", (double)lossMethod, "-", lossMethod.ToString()));
            return result;
        }
    }
}