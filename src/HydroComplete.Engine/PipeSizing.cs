using System;
using System.Collections.Generic;
using System.Linq;

namespace HydroComplete.Engine
{
    /// <summary>Outcome of sizing a pipe for a known discharge and slope.</summary>
    public enum SizeOutcome
    {
        /// <summary>Smallest catalog pipe that meets all criteria.</summary>
        Sized,

        /// <summary>Current diameter already meets criteria.</summary>
        Adequate,

        /// <summary>No catalog pipe satisfies the criteria at this slope.</summary>
        NoSolution,
    }

    /// <summary>Agency-style limits for standard-pipe sizing.</summary>
    public sealed class DesignCriteria
    {
        /// <summary>Minimum design velocity, ft/s. Pipes below this are rejected.</summary>
        public double MinVelocity { get; set; } = 2.0;

        /// <summary>Maximum design velocity, ft/s. Pipes above this are rejected.</summary>
        public double MaxVelocity { get; set; } = 10.0;

        /// <summary>Maximum design flow as a fraction of just-full Manning capacity.</summary>
        public double MaxPctFull { get; set; } = 0.85;

        /// <summary>When true, reject diameters where design Q exceeds open-channel capacity.</summary>
        public bool RequireOpenChannel { get; set; } = true;

        /// <summary>Ascending catalog of trial diameters, ft.</summary>
        public IReadOnlyList<double> StandardDiametersFt { get; set; } = StandardCatalogDiametersFt();

        /// <summary>Typical municipal / DOT storm trunk defaults.</summary>
        public static DesignCriteria Municipal() => new DesignCriteria();

        /// <summary>Slightly relaxed criteria for laterals (allows higher % full).</summary>
        public static DesignCriteria Lateral() =>
            new DesignCriteria { MaxPctFull = 0.95 };

        /// <summary>
        /// Standard reinforced-concrete pipe diameters (inches) used in US storm design,
        /// from 12" through 72".
        /// </summary>
        public static readonly int[] StandardCatalogInches =
        {
            12, 15, 18, 21, 24, 27, 30, 33, 36, 42, 48, 54, 60, 66, 72,
        };

        /// <summary>Default ascending catalog in feet.</summary>
        public static double[] StandardCatalogDiametersFt() =>
            StandardCatalogInches.Select(InchesToFt).ToArray();

        /// <summary>Convert a catalog diameter from inches to feet.</summary>
        public static double InchesToFt(int diameterInches) => diameterInches / 12.0;
    }

    /// <summary>Result of sizing one pipe cross-section.</summary>
    public sealed class PipeSizeResult
    {
        /// <summary>Recommended diameter, ft (equals current when adequate).</summary>
        public double RecommendedDiameterFt { get; set; }

        /// <summary>Average velocity at design flow, ft/s.</summary>
        public double VelocityFps { get; set; }

        /// <summary>Design flow as a fraction of just-full Manning capacity.</summary>
        public double PctFull { get; set; }

        /// <summary>True when design Q exceeds peak open-channel capacity.</summary>
        public bool Surcharged { get; set; }

        public SizeOutcome Outcome { get; set; }
    }

    /// <summary>
    /// Hydraflow-style standard-pipe sizing — smallest catalog pipe that carries
    /// design flow within velocity and capacity limits.
    /// </summary>
    public static class PipeSizing
    {
        private const double Tol = 1e-9;

        /// <summary>
        /// Recommend the smallest standard pipe for <paramref name="qCfs"/> on
        /// <paramref name="slope"/>, or confirm <paramref name="currentDiameterFt"/>
        /// is adequate.
        /// </summary>
        public static PipeSizeResult SizePipe(
            double qCfs,
            double slope,
            double n,
            double currentDiameterFt,
            DesignCriteria? criteria = null)
        {
            criteria ??= DesignCriteria.Municipal();

            if (TryEvaluateDiameter(qCfs, slope, n, currentDiameterFt, criteria, out var adequate))
            {
                return new PipeSizeResult
                {
                    RecommendedDiameterFt = currentDiameterFt,
                    VelocityFps = adequate.VelocityFps,
                    PctFull = adequate.PctFull,
                    Surcharged = adequate.Surcharged,
                    Outcome = SizeOutcome.Adequate,
                };
            }

            PipeSizeResult sized = SizePipeForFlow(qCfs, slope, n, criteria);
            return new PipeSizeResult
            {
                RecommendedDiameterFt = sized.RecommendedDiameterFt,
                VelocityFps = sized.VelocityFps,
                PctFull = sized.PctFull,
                Surcharged = sized.Surcharged,
                Outcome = sized.Outcome,
            };
        }

        /// <summary>Pick the smallest catalog diameter that carries <paramref name="qCfs"/>.</summary>
        public static PipeSizeResult SizePipeForFlow(
            double qCfs,
            double slope,
            double n,
            DesignCriteria? criteria = null)
        {
            criteria ??= DesignCriteria.Municipal();

            foreach (double d in criteria.StandardDiametersFt)
            {
                if (TryEvaluateDiameter(qCfs, slope, n, d, criteria, out var result))
                {
                    return new PipeSizeResult
                    {
                        RecommendedDiameterFt = d,
                        VelocityFps = result.VelocityFps,
                        PctFull = result.PctFull,
                        Surcharged = result.Surcharged,
                        Outcome = SizeOutcome.Sized,
                    };
                }
            }

            // No solution — report largest catalog hydraulics for diagnostics.
            double largest = criteria.StandardDiametersFt.Count > 0
                ? criteria.StandardDiametersFt[criteria.StandardDiametersFt.Count - 1]
                : 0.0;
            var diag = ComputeHydraulics(qCfs, slope, n, largest);
            return new PipeSizeResult
            {
                RecommendedDiameterFt = largest,
                VelocityFps = diag.VelocityFps,
                PctFull = diag.PctFull,
                Surcharged = diag.Surcharged,
                Outcome = SizeOutcome.NoSolution,
            };
        }

        /// <summary>Format a diameter in inches for command-line tables.</summary>
        public static string FormatDiameterIn(double diameterFt) =>
            $"{(int)Math.Round(diameterFt * 12.0)}\"";

        private static bool TryEvaluateDiameter(
            double qCfs,
            double slope,
            double n,
            double diameterFt,
            DesignCriteria criteria,
            out (double VelocityFps, double PctFull, bool Surcharged) result)
        {
            result = default;
            if (diameterFt <= 0.0 || qCfs < 0.0 || slope <= 0.0 || n <= 0.0)
                return false;

            var pipe = new PipeSegment
            {
                DiameterFt = diameterFt,
                Slope = slope,
                ManningN = n,
            };

            var capacity = Manning.Capacity(pipe);
            if (criteria.RequireOpenChannel && qCfs > capacity.PeakFlowCfs + Tol)
                return false;

            var nd = Manning.NormalDepth(pipe, qCfs);
            if (criteria.RequireOpenChannel && nd.Surcharged)
                return false;

            double area;
            if (nd.Surcharged)
            {
                area = Math.PI * diameterFt * diameterFt / 4.0;
            }
            else
            {
                (area, _) = Manning.PartialFlowGeometry(diameterFt, nd.DepthFt);
            }

            double velocity = area > 0.0 ? qCfs / area : 0.0;
            double pctFull = capacity.FullFlowCfs > 0.0 ? qCfs / capacity.FullFlowCfs : 0.0;

            if (velocity < criteria.MinVelocity - Tol)
                return false;
            if (velocity > criteria.MaxVelocity + Tol)
                return false;
            if (pctFull > criteria.MaxPctFull + Tol)
                return false;

            result = (velocity, pctFull, nd.Surcharged);
            return true;
        }

        private static (double VelocityFps, double PctFull, bool Surcharged) ComputeHydraulics(
            double qCfs,
            double slope,
            double n,
            double diameterFt)
        {
            if (diameterFt <= 0.0)
                return (0.0, 0.0, false);

            var pipe = new PipeSegment
            {
                DiameterFt = diameterFt,
                Slope = slope,
                ManningN = n,
            };

            var capacity = Manning.Capacity(pipe);
            var nd = Manning.NormalDepth(pipe, qCfs);

            double area = nd.Surcharged
                ? Math.PI * diameterFt * diameterFt / 4.0
                : Manning.PartialFlowGeometry(diameterFt, nd.DepthFt).AreaFt2;

            double velocity = area > 0.0 ? qCfs / area : 0.0;
            double pctFull = capacity.FullFlowCfs > 0.0 ? qCfs / capacity.FullFlowCfs : 0.0;
            return (velocity, pctFull, nd.Surcharged);
        }
    }
}