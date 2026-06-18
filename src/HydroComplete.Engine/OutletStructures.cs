using System;
using System.Collections.Generic;
using System.Linq;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Outlet structure hydraulics: orifice, sharp-crested weir, and riser compound
    /// rating curves for detention pond stage-discharge analysis.
    /// </summary>
    public static class OutletStructures
    {
        /// <summary>Gravitational acceleration, ft/s².</summary>
        public const double GravityFtPerSec2 = 32.2;

        public enum OutletKind
        {
            Orifice,
            SharpCrestedWeir,
            Riser,
        }

        public abstract class OutletDefinition
        {
            public string Name { get; set; } = "";
            public abstract OutletKind Kind { get; }
        }

        /// <summary>Circular orifice outlet (diameter in inches, invert in ft).</summary>
        public sealed class OrificeOutlet : OutletDefinition
        {
            public override OutletKind Kind => OutletKind.Orifice;

            /// <summary>Orifice diameter, inches.</summary>
            public double DiameterInches { get; set; } = 6.0;

            /// <summary>Discharge coefficient Cd (dimensionless).</summary>
            public double Cd { get; set; } = 0.6;

            /// <summary>Invert elevation, ft (datum = pond bottom).</summary>
            public double InvertElevFt { get; set; }
        }

        /// <summary>Sharp-crested rectangular weir.</summary>
        public sealed class WeirOutlet : OutletDefinition
        {
            public override OutletKind Kind => OutletKind.SharpCrestedWeir;

            /// <summary>Weir crest length, ft.</summary>
            public double LengthFt { get; set; } = 8.0;

            /// <summary>Weir coefficient Cw (ft^0.5/s).</summary>
            public double Cw { get; set; } = 3.0;

            /// <summary>Crest elevation, ft.</summary>
            public double CrestElevFt { get; set; }
        }

        /// <summary>
        /// Riser barrel: weir flow around perimeter at low head, orifice at high head
        /// (governing discharge is the lesser of the two).
        /// </summary>
        public sealed class RiserOutlet : OutletDefinition
        {
            public override OutletKind Kind => OutletKind.Riser;

            /// <summary>Riser barrel diameter, inches.</summary>
            public double DiameterInches { get; set; } = 12.0;

            public double Cd { get; set; } = 0.6;
            public double Cw { get; set; } = 2.75;

            /// <summary>Top-of-riser / weir crest elevation, ft.</summary>
            public double CrestElevFt { get; set; }
        }

        public sealed class OutletDischargePoint
        {
            public double ElevationFt { get; set; }
            public double TotalOutflowCfs { get; set; }
            public Dictionary<string, double> OutletFlowsCfs { get; } = new Dictionary<string, double>();
        }

        public sealed class MultiOutletRatingResult : TracedResult
        {
            public List<OutletDischargePoint> Points { get; } = new List<OutletDischargePoint>();
        }

        /// <summary>
        /// Orifice discharge: Q = Cd × A × sqrt(2gh).
        /// </summary>
        public static double OrificeDischargeCfs(double cd, double diameterInches, double headFt)
        {
            if (headFt <= 0) return 0.0;
            if (cd <= 0) throw new ArgumentOutOfRangeException(nameof(cd));
            if (diameterInches <= 0) throw new ArgumentOutOfRangeException(nameof(diameterInches));

            double diameterFt = diameterInches / 12.0;
            double areaFt2 = Math.PI * Math.Pow(diameterFt / 2.0, 2);
            return cd * areaFt2 * Math.Sqrt(2.0 * GravityFtPerSec2 * headFt);
        }

        /// <summary>
        /// Sharp-crested weir: Q = Cw × L × h^1.5.
        /// </summary>
        public static double SharpCrestedWeirDischargeCfs(double cw, double lengthFt, double headFt)
        {
            if (headFt <= 0) return 0.0;
            if (cw <= 0) throw new ArgumentOutOfRangeException(nameof(cw));
            if (lengthFt <= 0) throw new ArgumentOutOfRangeException(nameof(lengthFt));

            return cw * lengthFt * Math.Pow(headFt, 1.5);
        }

        /// <summary>
        /// Riser compound discharge: min(weir around perimeter, orifice through barrel).
        /// </summary>
        public static double RiserDischargeCfs(
            double cd,
            double cw,
            double diameterInches,
            double headFt)
        {
            if (headFt <= 0) return 0.0;

            double diameterFt = diameterInches / 12.0;
            double perimeterFt = Math.PI * diameterFt;
            double qWeir = SharpCrestedWeirDischargeCfs(cw, perimeterFt, headFt);

            double areaFt2 = Math.PI * Math.Pow(diameterFt / 2.0, 2);
            double qOrifice = cd * areaFt2 * Math.Sqrt(2.0 * GravityFtPerSec2 * headFt);

            return Math.Min(qWeir, qOrifice);
        }

        /// <summary>Discharge for a single outlet at pond water-surface elevation.</summary>
        public static double DischargeAtElevation(OutletDefinition outlet, double elevationFt)
        {
            if (outlet == null) throw new ArgumentNullException(nameof(outlet));

            switch (outlet)
            {
                case OrificeOutlet o:
                    double orificeHead = elevationFt - o.InvertElevFt;
                    return OrificeDischargeCfs(o.Cd, o.DiameterInches, orificeHead);

                case WeirOutlet w:
                    double weirHead = elevationFt - w.CrestElevFt;
                    return SharpCrestedWeirDischargeCfs(w.Cw, w.LengthFt, weirHead);

                case RiserOutlet r:
                    double riserHead = elevationFt - r.CrestElevFt;
                    return RiserDischargeCfs(r.Cd, r.Cw, r.DiameterInches, riserHead);

                default:
                    throw new ArgumentException($"Unsupported outlet type: {outlet.GetType().Name}");
            }
        }

        /// <summary>
        /// Build a multi-outlet stage-discharge rating curve from minimum to maximum elevation.
        /// </summary>
        public static MultiOutletRatingResult BuildRatingCurve(
            IReadOnlyList<OutletDefinition> outlets,
            double minElevFt,
            double maxElevFt,
            double elevStepFt)
        {
            if (outlets == null) throw new ArgumentNullException(nameof(outlets));
            if (maxElevFt < minElevFt) throw new ArgumentOutOfRangeException(nameof(maxElevFt));
            if (elevStepFt <= 0) throw new ArgumentOutOfRangeException(nameof(elevStepFt));

            var result = new MultiOutletRatingResult();
            result.Steps.Add(new CalcStep("elev_min", minElevFt, "ft", "rating curve lower bound"));
            result.Steps.Add(new CalcStep("elev_max", maxElevFt, "ft", "rating curve upper bound"));
            result.Steps.Add(new CalcStep("elev_step", elevStepFt, "ft", "elevation increment"));

            for (double elev = minElevFt; elev <= maxElevFt + 1e-9; elev += elevStepFt)
            {
                var point = new OutletDischargePoint { ElevationFt = elev };
                double total = 0.0;

                foreach (var outlet in outlets)
                {
                    string name = string.IsNullOrWhiteSpace(outlet.Name)
                        ? outlet.Kind.ToString()
                        : outlet.Name;

                    double q = DischargeAtElevation(outlet, elev);
                    point.OutletFlowsCfs[name] = q;
                    total += q;
                }

                point.TotalOutflowCfs = total;
                result.Points.Add(point);
            }

            return result;
        }

        /// <summary>Total discharge from all outlets at a given elevation.</summary>
        public static double TotalDischargeAtElevation(
            IReadOnlyList<OutletDefinition> outlets,
            double elevationFt)
        {
            return outlets.Sum(o => DischargeAtElevation(o, elevationFt));
        }
    }
}