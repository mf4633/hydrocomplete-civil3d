using System;
using System.Collections.Generic;
using System.Linq;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Series detention pond routing with iterative downstream tailwater (Gauss-Seidel).
    /// Modified Puls storage indication with backwater-adjusted outlet ratings.
    /// </summary>
    public static class PondChainRouting
    {
        public const int DefaultMaxIterations = 15;
        public const double DefaultToleranceFt = 0.1;
        public const double DefaultDampingFactor = 0.7;

        public sealed class PondDefinition
        {
            public List<DetentionRouting.StorageIndicationPoint> StorageCurve { get; set; } =
                new List<DetentionRouting.StorageIndicationPoint>();

            public IReadOnlyList<OutletStructures.OutletDefinition> Outlets { get; set; } =
                Array.Empty<OutletStructures.OutletDefinition>();

            public double TimestepHours { get; set; } = DetentionRouting.DefaultTimestepHours;
        }

        public sealed class PondChainOptions
        {
            public int MaxIterations { get; set; } = DefaultMaxIterations;
            public double ToleranceFt { get; set; } = DefaultToleranceFt;
            public double DampingFactor { get; set; } = DefaultDampingFactor;
        }

        public sealed class PondChainPondResult
        {
            public int Index { get; set; }
            public double PeakInflowCfs { get; set; }
            public double PeakOutflowCfs { get; set; }
            public double PeakStorageFt3 { get; set; }
            public double PeakElevationFt { get; set; }
            public double ReductionPercent { get; set; }
            public double TailwaterFt { get; set; }
            public DetentionRouting.RoutingResult Routing { get; set; } = null!;
        }

        public sealed class PondChainResult : TracedResult
        {
            public int PondCount { get; set; }
            public bool Converged { get; set; }
            public int Iterations { get; set; }
            public List<double> Tailwaters { get; } = new List<double>();
            public List<PondChainPondResult> PondResults { get; } = new List<PondChainPondResult>();
        }

        /// <summary>
        /// Route an inflow hydrograph through a chain of ponds with cascading tailwater iteration.
        /// </summary>
        public static PondChainResult Route(
            IReadOnlyList<DetentionRouting.HydrographPoint> inflowHydrograph,
            IReadOnlyList<PondDefinition> ponds,
            PondChainOptions? options = null)
        {
            if (inflowHydrograph == null) throw new ArgumentNullException(nameof(inflowHydrograph));
            if (ponds == null) throw new ArgumentNullException(nameof(ponds));

            options ??= new PondChainOptions();
            int n = ponds.Count;

            var result = new PondChainResult { PondCount = n };

            if (n == 0)
            {
                result.Converged = true;
                result.Iterations = 0;
                return result;
            }

            if (n == 1)
            {
                PondDefinition pond = ponds[0];
                DetentionRouting.RoutingResult routing = DetentionRouting.Route(
                    inflowHydrograph,
                    pond.StorageCurve,
                    pond.TimestepHours);

                result.PondResults.Add(ToPondResult(0, routing, tailwaterFt: 0.0));
                result.Converged = true;
                result.Iterations = 0;
                AddSummarySteps(result, routing.PeakOutflowCfs, true, 0);
                return result;
            }

            var tailwaters = new double[n];
            var pondResults = new DetentionRouting.RoutingResult?[n];
            bool converged = false;
            int actualIter = 0;

            for (int iter = 0; iter < options.MaxIterations; iter++)
            {
                actualIter = iter + 1;
                double maxChange = 0.0;

                for (int i = 0; i < n; i++)
                {
                    IReadOnlyList<DetentionRouting.HydrographPoint> inflow = i == 0
                        ? inflowHydrograph
                        : OutflowHydrograph(pondResults[i - 1]!);

                    double tailwater = i < n - 1 ? tailwaters[i + 1] : 0.0;
                    List<DetentionRouting.StorageIndicationPoint> curve = ApplyTailwater(
                        ponds[i].StorageCurve,
                        ponds[i].Outlets,
                        tailwater);

                    pondResults[i] = DetentionRouting.Route(inflow, curve, ponds[i].TimestepHours);
                }

                for (int i = n - 1; i >= 1; i--)
                {
                    double newTw = pondResults[i]!.PeakElevationFt;
                    double oldTw = tailwaters[i];
                    tailwaters[i] = oldTw + options.DampingFactor * (newTw - oldTw);
                    maxChange = Math.Max(maxChange, Math.Abs(newTw - oldTw));
                }

                if (maxChange < options.ToleranceFt)
                {
                    converged = true;
                    break;
                }
            }

            result.Converged = converged;
            result.Iterations = actualIter;

            for (int i = 1; i < n; i++)
                result.Tailwaters.Add(tailwaters[i]);

            for (int i = 0; i < n; i++)
            {
                double tw = i < n - 1 ? tailwaters[i + 1] : 0.0;
                result.PondResults.Add(ToPondResult(i, pondResults[i]!, tw));
            }

            double finalPeak = pondResults[n - 1]!.PeakOutflowCfs;
            AddSummarySteps(result, finalPeak, converged, actualIter, options.DampingFactor);
            return result;
        }

        /// <summary>
        /// Recompute stage-discharge with effective head reduced by downstream tailwater.
        /// </summary>
        public static List<DetentionRouting.StorageIndicationPoint> ApplyTailwater(
            IReadOnlyList<DetentionRouting.StorageIndicationPoint> baseCurve,
            IReadOnlyList<OutletStructures.OutletDefinition> outlets,
            double tailwaterFt)
        {
            if (baseCurve == null) throw new ArgumentNullException(nameof(baseCurve));
            if (tailwaterFt <= 0.0)
                return baseCurve.Select(ClonePoint).ToList();

            var adjusted = new List<DetentionRouting.StorageIndicationPoint>(baseCurve.Count);

            foreach (DetentionRouting.StorageIndicationPoint pt in baseCurve)
            {
                double totalQ = 0.0;
                var outletFlows = new Dictionary<string, double>();

                foreach (OutletStructures.OutletDefinition outlet in outlets ?? Array.Empty<OutletStructures.OutletDefinition>())
                {
                    string name = OutletName(outlet);
                    double referenceElev = ReferenceElevation(outlet);
                    double q = 0.0;

                    if (pt.ElevationFt > referenceElev)
                    {
                        double freeHead = pt.ElevationFt - referenceElev;
                        double effHead = Math.Max(0.0, freeHead - tailwaterFt);
                        q = DischargeWithHead(outlet, effHead, freeHead, pt, name);
                    }

                    outletFlows[name] = Math.Max(0.0, q);
                    totalQ += outletFlows[name];
                }

                if (outlets == null || outlets.Count == 0)
                {
                    totalQ = pt.OutflowCfs;
                    foreach (KeyValuePair<string, double> kv in pt.OutletFlowsCfs)
                        outletFlows[kv.Key] = kv.Value;
                }

                var newPt = new DetentionRouting.StorageIndicationPoint
                {
                    ElevationFt = pt.ElevationFt,
                    StorageFt3 = pt.StorageFt3,
                    OutflowCfs = totalQ,
                };

                foreach (KeyValuePair<string, double> kv in outletFlows)
                    newPt.OutletFlowsCfs[kv.Key] = kv.Value;

                adjusted.Add(newPt);
            }

            return adjusted;
        }

        private static PondChainPondResult ToPondResult(
            int index,
            DetentionRouting.RoutingResult routing,
            double tailwaterFt)
        {
            return new PondChainPondResult
            {
                Index = index,
                PeakInflowCfs = routing.PeakInflowCfs,
                PeakOutflowCfs = routing.PeakOutflowCfs,
                PeakStorageFt3 = routing.PeakStorageFt3,
                PeakElevationFt = routing.PeakElevationFt,
                ReductionPercent = routing.ReductionPercent,
                TailwaterFt = tailwaterFt,
                Routing = routing,
            };
        }

        private static List<DetentionRouting.HydrographPoint> OutflowHydrograph(
            DetentionRouting.RoutingResult routing)
        {
            return routing.Ordinates
                .Select(o => new DetentionRouting.HydrographPoint
                {
                    TimeHours = o.TimeHours,
                    FlowCfs = o.OutflowCfs,
                })
                .ToList();
        }

        private static DetentionRouting.StorageIndicationPoint ClonePoint(
            DetentionRouting.StorageIndicationPoint pt)
        {
            var clone = new DetentionRouting.StorageIndicationPoint
            {
                ElevationFt = pt.ElevationFt,
                StorageFt3 = pt.StorageFt3,
                OutflowCfs = pt.OutflowCfs,
            };

            foreach (KeyValuePair<string, double> kv in pt.OutletFlowsCfs)
                clone.OutletFlowsCfs[kv.Key] = kv.Value;

            return clone;
        }

        private static string OutletName(OutletStructures.OutletDefinition outlet)
        {
            return string.IsNullOrWhiteSpace(outlet.Name)
                ? outlet.Kind.ToString()
                : outlet.Name;
        }

        private static double ReferenceElevation(OutletStructures.OutletDefinition outlet)
        {
            return outlet switch
            {
                OutletStructures.OrificeOutlet o => o.InvertElevFt,
                OutletStructures.WeirOutlet w => w.CrestElevFt,
                OutletStructures.RiserOutlet r => r.CrestElevFt,
                _ => 0.0,
            };
        }

        private static double DischargeWithHead(
            OutletStructures.OutletDefinition outlet,
            double effectiveHeadFt,
            double freeHeadFt,
            DetentionRouting.StorageIndicationPoint point,
            string outletName)
        {
            if (effectiveHeadFt <= 0.0)
                return 0.0;

            switch (outlet)
            {
                case OutletStructures.OrificeOutlet o:
                    return OutletStructures.OrificeDischargeCfs(
                        o.Cd, o.DiameterInches, effectiveHeadFt);

                case OutletStructures.WeirOutlet w:
                    return OutletStructures.SharpCrestedWeirDischargeCfs(
                        w.Cw, w.LengthFt, effectiveHeadFt);

                case OutletStructures.RiserOutlet r:
                    return OutletStructures.RiserDischargeCfs(
                        r.Cd, r.Cw, r.DiameterInches, effectiveHeadFt);

                default:
                    point.OutletFlowsCfs.TryGetValue(outletName, out double baseQ);
                    double scale = freeHeadFt > 0.01 ? effectiveHeadFt / freeHeadFt : 0.0;
                    return baseQ * scale;
            }
        }

        private static void AddSummarySteps(
            PondChainResult result,
            double finalPeakCfs,
            bool converged,
            int iterations,
            double damping = DefaultDampingFactor)
        {
            result.Steps.Add(new CalcStep("pond_count", result.PondCount, "",
                "ponds in series"));
            result.Steps.Add(new CalcStep("damping", damping, "",
                "Gauss-Seidel tailwater damping factor"));
            result.Steps.Add(new CalcStep("iterations", iterations, "",
                "tailwater convergence iterations"));
            result.Steps.Add(new CalcStep("converged", converged ? 1.0 : 0.0, "",
                converged ? "tailwater converged" : "tailwater not converged"));
            result.Steps.Add(new CalcStep("Q_final", finalPeakCfs, "cfs",
                "chain final peak outflow"));

            for (int i = 0; i < result.Tailwaters.Count; i++)
            {
                result.Steps.Add(new CalcStep($"TW_{i + 1}", result.Tailwaters[i], "ft",
                    $"tailwater pond {i} → {i + 1}"));
            }
        }
    }
}