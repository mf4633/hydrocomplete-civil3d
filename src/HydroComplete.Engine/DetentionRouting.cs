using System;
using System.Collections.Generic;
using System.Linq;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Detention pond routing via the storage indication method (Modified Puls).
    /// NRCS NEH Part 630, Chapter 17.
    /// </summary>
    public static class DetentionRouting
    {
        /// <summary>Default routing time step, hours.</summary>
        public const double DefaultTimestepHours = 0.1;

        /// <summary>Outflow convergence tolerance for drain continuation, cfs.</summary>
        public const double DrainStorageToleranceFt3 = 1.0;

        public sealed class HydrographPoint
        {
            /// <summary>Elapsed time, hours.</summary>
            public double TimeHours { get; set; }

            /// <summary>Discharge, cfs.</summary>
            public double FlowCfs { get; set; }
        }

        /// <summary>
        /// Combined storage-indication curve point for Modified Puls routing.
        /// </summary>
        public sealed class StorageIndicationPoint
        {
            public double ElevationFt { get; set; }
            public double StorageFt3 { get; set; }
            public double OutflowCfs { get; set; }
            public Dictionary<string, double> OutletFlowsCfs { get; } = new Dictionary<string, double>();
        }

        public sealed class RoutingOrdinate
        {
            public double TimeHours { get; set; }
            public double InflowCfs { get; set; }
            public double OutflowCfs { get; set; }
            public double StorageFt3 { get; set; }
            public double ElevationFt { get; set; }
            public Dictionary<string, double> OutletFlowsCfs { get; } = new Dictionary<string, double>();
        }

        public sealed class RoutingResult : TracedResult
        {
            public double PeakInflowCfs { get; set; }
            public double PeakOutflowCfs { get; set; }
            public double PeakStorageFt3 { get; set; }
            public double PeakElevationFt { get; set; }
            public double ReductionPercent { get; set; }
            public double TimestepHours { get; set; }

            public List<RoutingOrdinate> Ordinates { get; } = new List<RoutingOrdinate>();

            /// <summary>Per-outlet hydrographs keyed by outlet name.</summary>
            public Dictionary<string, List<HydrographPoint>> OutletHydrographs { get; } =
                new Dictionary<string, List<HydrographPoint>>();
        }

        /// <summary>
        /// Build a storage-indication curve from stage-storage and outlet definitions.
        /// </summary>
        public static List<StorageIndicationPoint> BuildStorageIndicationCurve(
            IReadOnlyList<StageStorage.StageStoragePoint> stageStorage,
            IReadOnlyList<OutletStructures.OutletDefinition> outlets,
            double? maxElevFt = null,
            double? elevStepFt = null)
        {
            if (stageStorage == null || stageStorage.Count < 2)
                throw new ArgumentException("Stage-storage table must have at least two points.");

            double minElev = stageStorage[0].ElevationFt;
            double maxElev = maxElevFt ?? stageStorage[stageStorage.Count - 1].ElevationFt * 1.2;
            double step = elevStepFt ?? Math.Max(0.1, (maxElev - minElev) / 64.0);
            if (step <= 0)
                throw new ArgumentOutOfRangeException(nameof(elevStepFt), "Elevation step must be > 0.");

            var curve = new List<StorageIndicationPoint>();

            for (double elev = minElev; elev <= maxElev + 1e-9; elev += step)
            {
                double storage = StageStorage.InterpolateStorage(elev, stageStorage);
                var outletFlows = new Dictionary<string, double>();
                double totalOutflow = 0.0;

                foreach (var outlet in outlets ?? Array.Empty<OutletStructures.OutletDefinition>())
                {
                    string name = string.IsNullOrWhiteSpace(outlet.Name)
                        ? outlet.Kind.ToString()
                        : outlet.Name;
                    double q = OutletStructures.DischargeAtElevation(outlet, elev);
                    outletFlows[name] = q;
                    totalOutflow += q;
                }

                var point = new StorageIndicationPoint
                {
                    ElevationFt = elev,
                    StorageFt3 = storage,
                    OutflowCfs = totalOutflow,
                };
                foreach (var kv in outletFlows)
                    point.OutletFlowsCfs[kv.Key] = kv.Value;
                curve.Add(point);
            }

            return curve;
        }

        /// <summary>
        /// Convenience: build a prismatic pond storage curve and route in one call.
        /// </summary>
        public static List<StorageIndicationPoint> BuildPrismaticStorageIndicationCurve(
            double maxStorageFt3,
            IReadOnlyList<OutletStructures.OutletDefinition> outlets,
            double avgDepthFt = 8.0)
        {
            if (maxStorageFt3 <= 0) throw new ArgumentOutOfRangeException(nameof(maxStorageFt3));
            if (avgDepthFt <= 0) throw new ArgumentOutOfRangeException(nameof(avgDepthFt));

            double surfaceArea = maxStorageFt3 / avgDepthFt;
            double maxElev = avgDepthFt * 1.5;

            var table = new List<StageStorage.StageStoragePoint>
            {
                new StageStorage.StageStoragePoint { ElevationFt = 0, AreaFt2 = surfaceArea, StorageFt3 = 0 },
                new StageStorage.StageStoragePoint
                {
                    ElevationFt = maxElev,
                    AreaFt2 = surfaceArea,
                    StorageFt3 = surfaceArea * maxElev,
                },
            };

            return BuildStorageIndicationCurve(table, outlets, maxElev, Math.Max(0.25, avgDepthFt / 32.0));
        }

        /// <summary>
        /// Route an inflow hydrograph through a detention pond storage-indication curve.
        /// </summary>
        public static RoutingResult Route(
            IReadOnlyList<HydrographPoint> inflowHydrograph,
            IReadOnlyList<StorageIndicationPoint> storageCurve,
            double timestepHours = DefaultTimestepHours)
        {
            if (storageCurve == null || storageCurve.Count < 2)
                throw new ArgumentException("Storage-indication curve must have at least two points.");
            if (timestepHours <= 0) throw new ArgumentOutOfRangeException(nameof(timestepHours));

            var result = new RoutingResult { TimestepHours = timestepHours };
            result.Steps.Add(new CalcStep("dt", timestepHours * 3600.0, "sec", "routing time step"));
            result.Steps.Add(new CalcStep(
                "method",
                1.0,
                "-",
                "Modified Puls: 2S2/dt + O2 = 2S1/dt - O1 + I1 + I2"));

            if (inflowHydrograph == null || inflowHydrograph.Count == 0)
            {
                result.Steps.Add(new CalcStep("peak_in", 0.0, "cfs", "empty inflow hydrograph"));
                return result;
            }

            double dtSeconds = timestepHours * 3600.0;
            var uniformInflow = ResampleInflow(inflowHydrograph, timestepHours);

            double s1 = 0.0;
            double o1 = 0.0;
            var routing = new List<RoutingOrdinate>();

            for (int i = 0; i < uniformInflow.Count; i++)
            {
                double i1 = i > 0 ? uniformInflow[i - 1].FlowCfs : 0.0;
                double i2 = uniformInflow[i].FlowCfs;
                double leftSide = (2.0 * s1 / dtSeconds) - o1 + (i1 + i2);

                var solved = SolveStorageIndication(leftSide, storageCurve, dtSeconds);
                var entry = new RoutingOrdinate
                {
                    TimeHours = uniformInflow[i].TimeHours,
                    InflowCfs = i2,
                    OutflowCfs = Math.Max(0.0, solved.OutflowCfs),
                    StorageFt3 = Math.Max(0.0, solved.StorageFt3),
                    ElevationFt = Math.Max(0.0, solved.ElevationFt),
                };

                foreach (var kv in solved.OutletFlowsCfs)
                    entry.OutletFlowsCfs[kv.Key] = Math.Max(0.0, kv.Value);

                routing.Add(entry);
                s1 = entry.StorageFt3;
                o1 = entry.OutflowCfs;
            }

            // Continue with zero inflow until pond drains
            int maxDrainSteps = 5000;
            while (s1 > DrainStorageToleranceFt3 && maxDrainSteps-- > 0)
            {
                double lastTime = routing[routing.Count - 1].TimeHours + timestepHours;
                double i1 = routing[routing.Count - 1].InflowCfs;
                double leftSide = (2.0 * s1 / dtSeconds) - o1 + i1;

                var solved = SolveStorageIndication(leftSide, storageCurve, dtSeconds);
                var entry = new RoutingOrdinate
                {
                    TimeHours = lastTime,
                    InflowCfs = 0.0,
                    OutflowCfs = Math.Max(0.0, solved.OutflowCfs),
                    StorageFt3 = Math.Max(0.0, solved.StorageFt3),
                    ElevationFt = Math.Max(0.0, solved.ElevationFt),
                };

                foreach (var kv in solved.OutletFlowsCfs)
                    entry.OutletFlowsCfs[kv.Key] = Math.Max(0.0, kv.Value);

                routing.Add(entry);
                s1 = entry.StorageFt3;
                o1 = entry.OutflowCfs;
            }

            result.Ordinates.AddRange(routing);

            double peakInflow = inflowHydrograph.Max(p => p.FlowCfs);
            double peakOutflow = routing.Max(p => p.OutflowCfs);
            double peakStorage = routing.Max(p => p.StorageFt3);
            double peakElevation = routing.Max(p => p.ElevationFt);
            double reduction = peakInflow > 0
                ? (1.0 - peakOutflow / peakInflow) * 100.0
                : 0.0;

            result.PeakInflowCfs = peakInflow;
            result.PeakOutflowCfs = peakOutflow;
            result.PeakStorageFt3 = peakStorage;
            result.PeakElevationFt = peakElevation;
            result.ReductionPercent = reduction;

            result.Steps.Add(new CalcStep("Q_in,peak", peakInflow, "cfs", "peak inflow"));
            result.Steps.Add(new CalcStep("Q_out,peak", peakOutflow, "cfs", "peak outflow"));
            result.Steps.Add(new CalcStep("S_max", peakStorage, "ft³", "maximum pond storage"));
            result.Steps.Add(new CalcStep("reduction", reduction, "%", "(Q_in-Q_out)/Q_in×100"));

            BuildOutletHydrographs(result);
            return result;
        }

        /// <summary>
        /// Generate an inflow hydrograph from SCS unit hydrograph ordinates scaled by runoff depth.
        /// </summary>
        public static List<HydrographPoint> InflowFromUnitHydrograph(
            ScsUnitHydrograph.UnitHydrographResult unitHydrograph,
            double runoffDepthInches)
        {
            if (unitHydrograph == null) throw new ArgumentNullException(nameof(unitHydrograph));

            return unitHydrograph.Ordinates
                .Select(o => new HydrographPoint
                {
                    TimeHours = o.TimeMinutes / 60.0,
                    FlowCfs = o.FlowCfs * runoffDepthInches,
                })
                .ToList();
        }

        /// <summary>Trapezoidal hydrograph volume, ft³.</summary>
        public static double HydrographVolumeFt3(IReadOnlyList<HydrographPoint> hydrograph, double timestepHours)
        {
            if (hydrograph == null || hydrograph.Count < 2) return 0.0;
            double dtSec = timestepHours * 3600.0;
            double volume = 0.0;

            for (int i = 1; i < hydrograph.Count; i++)
            {
                double qAvg = (hydrograph[i].FlowCfs + hydrograph[i - 1].FlowCfs) / 2.0;
                double dt = (hydrograph[i].TimeHours - hydrograph[i - 1].TimeHours) * 3600.0;
                if (dt <= 0) dt = dtSec;
                volume += qAvg * dt;
            }

            return volume;
        }

        /// <summary>
        /// Continuity check: inflow volume ≈ outflow volume + final storage (ft³).
        /// </summary>
        public static double ContinuityErrorPercent(RoutingResult routing)
        {
            if (routing.Ordinates.Count < 2) return 0.0;

            double inVol = 0.0;
            double outVol = 0.0;

            for (int i = 1; i < routing.Ordinates.Count; i++)
            {
                double dt = (routing.Ordinates[i].TimeHours - routing.Ordinates[i - 1].TimeHours) * 3600.0;
                inVol += (routing.Ordinates[i].InflowCfs + routing.Ordinates[i - 1].InflowCfs) / 2.0 * dt;
                outVol += (routing.Ordinates[i].OutflowCfs + routing.Ordinates[i - 1].OutflowCfs) / 2.0 * dt;
            }

            double finalStorage = routing.Ordinates[routing.Ordinates.Count - 1].StorageFt3;
            if (inVol <= 0) return 0.0;
            return Math.Abs(inVol - (outVol + finalStorage)) / inVol * 100.0;
        }

        internal static List<HydrographPoint> ResampleInflow(
            IReadOnlyList<HydrographPoint> inflow,
            double timestepHours)
        {
            double maxTime = inflow[inflow.Count - 1].TimeHours;
            var uniform = new List<HydrographPoint>();

            for (double t = 0.0; t <= maxTime + timestepHours; t += timestepHours)
            {
                double flow = InterpolateFlow(inflow, t);
                if (t > maxTime) flow = 0.0;
                uniform.Add(new HydrographPoint { TimeHours = t, FlowCfs = Math.Max(0.0, flow) });
            }

            return uniform;
        }

        internal static double InterpolateFlow(IReadOnlyList<HydrographPoint> hydrograph, double timeHours)
        {
            if (hydrograph.Count == 0) return 0.0;

            for (int j = 1; j < hydrograph.Count; j++)
            {
                if (hydrograph[j].TimeHours >= timeHours)
                {
                    double t0 = hydrograph[j - 1].TimeHours;
                    double t1 = hydrograph[j].TimeHours;
                    double f0 = hydrograph[j - 1].FlowCfs;
                    double f1 = hydrograph[j].FlowCfs;
                    if (t1 > t0)
                        return f0 + (f1 - f0) * (timeHours - t0) / (t1 - t0);
                    return f0;
                }
            }

            return 0.0;
        }

        internal static StorageIndicationPoint SolveStorageIndication(
            double leftSide,
            IReadOnlyList<StorageIndicationPoint> storageCurve,
            double dtSeconds)
        {
            bool hasOutletFlows = storageCurve[0].OutletFlowsCfs.Count > 0;

            for (int i = 1; i < storageCurve.Count; i++)
            {
                var point = storageCurve[i];
                var prev = storageCurve[i - 1];

                double indicator = (2.0 * point.StorageFt3 / dtSeconds) + point.OutflowCfs;
                double prevIndicator = (2.0 * prev.StorageFt3 / dtSeconds) + prev.OutflowCfs;

                if (indicator >= leftSide)
                {
                    double denom = indicator - prevIndicator;
                    double fraction = Math.Abs(denom) < 1e-12 ? 0.0 : (leftSide - prevIndicator) / denom;

                    var solved = new StorageIndicationPoint
                    {
                        StorageFt3 = prev.StorageFt3 + fraction * (point.StorageFt3 - prev.StorageFt3),
                        OutflowCfs = prev.OutflowCfs + fraction * (point.OutflowCfs - prev.OutflowCfs),
                        ElevationFt = prev.ElevationFt + fraction * (point.ElevationFt - prev.ElevationFt),
                    };
                    foreach (var kv in InterpolateOutletFlows(prev, point, fraction, hasOutletFlows))
                        solved.OutletFlowsCfs[kv.Key] = kv.Value;
                    return solved;
                }
            }

            // Extrapolate beyond curve
            int n = storageCurve.Count;
            var last = storageCurve[n - 1];
            var prevLast = storageCurve[n - 2];
            double lastIndicator = (2.0 * last.StorageFt3 / dtSeconds) + last.OutflowCfs;
            double prevLastIndicator = (2.0 * prevLast.StorageFt3 / dtSeconds) + prevLast.OutflowCfs;
            double slope = lastIndicator - prevLastIndicator;

            if (slope <= 0)
            {
                var atLast = new StorageIndicationPoint
                {
                    StorageFt3 = last.StorageFt3,
                    OutflowCfs = last.OutflowCfs,
                    ElevationFt = last.ElevationFt,
                };
                foreach (var kv in last.OutletFlowsCfs)
                    atLast.OutletFlowsCfs[kv.Key] = kv.Value;
                return atLast;
            }

            double frac = (leftSide - lastIndicator) / slope;
            var extrapolated = new StorageIndicationPoint
            {
                StorageFt3 = last.StorageFt3 + frac * (last.StorageFt3 - prevLast.StorageFt3),
                OutflowCfs = last.OutflowCfs + frac * (last.OutflowCfs - prevLast.OutflowCfs),
                ElevationFt = last.ElevationFt + frac * (last.ElevationFt - prevLast.ElevationFt),
            };
            foreach (var kv in InterpolateOutletFlows(prevLast, last, frac, hasOutletFlows))
                extrapolated.OutletFlowsCfs[kv.Key] = kv.Value;
            return extrapolated;
        }

        private static Dictionary<string, double> InterpolateOutletFlows(
            StorageIndicationPoint prev,
            StorageIndicationPoint curr,
            double fraction,
            bool hasOutletFlows)
        {
            var result = new Dictionary<string, double>();
            if (!hasOutletFlows) return result;

            foreach (var name in curr.OutletFlowsCfs.Keys)
            {
                prev.OutletFlowsCfs.TryGetValue(name, out double pv);
                curr.OutletFlowsCfs.TryGetValue(name, out double cv);
                result[name] = pv + fraction * (cv - pv);
            }

            return result;
        }

        private static void BuildOutletHydrographs(RoutingResult result)
        {
            if (result.Ordinates.Count == 0) return;

            var outletNames = result.Ordinates[0].OutletFlowsCfs.Keys.ToList();
            foreach (string name in outletNames)
            {
                var hydro = new List<HydrographPoint>();
                foreach (var ord in result.Ordinates)
                {
                    ord.OutletFlowsCfs.TryGetValue(name, out double q);
                    hydro.Add(new HydrographPoint
                    {
                        TimeHours = ord.TimeHours,
                        FlowCfs = Math.Max(0.0, q),
                    });
                }

                result.OutletHydrographs[name] = hydro;
            }
        }
    }
}