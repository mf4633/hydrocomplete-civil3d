using System;
using System.Collections.Generic;
using System.Linq;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Gradually varied flow (GVF) for trapezoidal open channels — US customary units.
    /// Standard Step Method with Manning friction slope; mirrors hc-refactored HydraulicEngine.
    /// </summary>
    public static class GraduallyVariedFlow
    {
        /// <summary>Eddy/expansion loss coefficient in the energy equation.</summary>
        public const double DefaultEddyLossCoefficient = 0.1;

        public enum GvfBoundaryType
        {
            Normal,
            Critical,
            Known,
        }

        public sealed class ChannelParameters
        {
            public double BottomWidthFt { get; set; }
            public double SideSlopeZ { get; set; }
            public double ManningN { get; set; }
            public double BedSlopeFtPerFt { get; set; }
        }

        public sealed class Station
        {
            public double DistanceFt { get; set; }
            public double InvertElevFt { get; set; }
        }

        public sealed class ProfilePoint
        {
            public double StationFt { get; set; }
            public double InvertElevFt { get; set; }
            public double DepthFt { get; set; }
            public double WaterSurfaceElevFt { get; set; }
            public double VelocityFps { get; set; }
            public double FroudeNumber { get; set; }
            public string FlowRegime { get; set; } = "";
            public double FrictionSlope { get; set; }
        }

        public sealed class ConjugateDepthResult : TracedResult
        {
            public bool IsValid { get; set; } = true;
            public string? ErrorMessage { get; set; }
            public double UpstreamDepthFt { get; set; }
            public double ConjugateDepthFt { get; set; }
            public double UpstreamVelocityFps { get; set; }
            public double DownstreamVelocityFps { get; set; }
            public double FroudeUpstream { get; set; }
            public double FroudeDownstream { get; set; }
            public double EnergyLossFt { get; set; }
            public double EnergyLossCheckFt { get; set; }
            public double EfficiencyPercent { get; set; }
            public string JumpType { get; set; } = "";
        }

        public sealed class ProfileResult : TracedResult
        {
            public IReadOnlyList<ProfilePoint> Profile { get; set; } = Array.Empty<ProfilePoint>();
            public string ProfileType { get; set; } = "";
            public double NormalDepthFt { get; set; }
            public double CriticalDepthFt { get; set; }
            public bool IsSubcritical { get; set; }
            public double BoundaryDepthFt { get; set; }
            public GvfBoundaryType BoundaryType { get; set; }
        }

        /// <summary>
        /// Conjugate (sequent) depth after a hydraulic jump — Belanger equation (rectangular channel).
        /// </summary>
        public static ConjugateDepthResult ConjugateDepth(
            double upstreamDepthFt,
            double upstreamVelocityFps,
            double bottomWidthFt)
        {
            if (upstreamDepthFt <= 0) throw new ArgumentOutOfRangeException(nameof(upstreamDepthFt));
            if (upstreamVelocityFps < 0) throw new ArgumentOutOfRangeException(nameof(upstreamVelocityFps));
            if (bottomWidthFt <= 0) throw new ArgumentOutOfRangeException(nameof(bottomWidthFt));

            double g = ChannelHydraulics.G;
            double fr1 = upstreamVelocityFps / Math.Sqrt(g * upstreamDepthFt);

            if (fr1 < 1.0)
            {
                return new ConjugateDepthResult
                {
                    IsValid = false,
                    ErrorMessage =
                        $"Upstream Froude number ({fr1:0.###}) is less than 1. A hydraulic jump requires supercritical upstream flow.",
                    FroudeUpstream = fr1,
                    UpstreamDepthFt = upstreamDepthFt,
                    UpstreamVelocityFps = upstreamVelocityFps,
                };
            }

            double y2 = 0.5 * upstreamDepthFt * (-1.0 + Math.Sqrt(1.0 + 8.0 * fr1 * fr1));
            double q = upstreamVelocityFps * upstreamDepthFt * bottomWidthFt;
            double a2 = y2 * bottomWidthFt;
            double v2 = a2 > 0 ? q / a2 : 0.0;
            double fr2 = v2 / Math.Sqrt(g * y2);

            double e1 = SpecificEnergy(upstreamDepthFt, upstreamVelocityFps);
            double e2 = SpecificEnergy(y2, v2);
            double energyLoss = e1 - e2;
            double energyLossCheck = Math.Pow(y2 - upstreamDepthFt, 3) / (4.0 * upstreamDepthFt * y2);
            double efficiency = e1 > 0 ? (e2 / e1) * 100.0 : 0.0;

            string jumpType;
            if (fr1 < 1.7) jumpType = "Undular jump (standing waves)";
            else if (fr1 < 2.5) jumpType = "Weak jump (smooth surface rise)";
            else if (fr1 < 4.5) jumpType = "Oscillating jump (irregular waves)";
            else if (fr1 < 9.0) jumpType = "Steady jump (well-defined roller)";
            else jumpType = "Strong jump (rough, high energy dissipation)";

            var result = new ConjugateDepthResult
            {
                IsValid = true,
                UpstreamDepthFt = upstreamDepthFt,
                ConjugateDepthFt = y2,
                UpstreamVelocityFps = upstreamVelocityFps,
                DownstreamVelocityFps = v2,
                FroudeUpstream = fr1,
                FroudeDownstream = fr2,
                EnergyLossFt = energyLoss,
                EnergyLossCheckFt = energyLossCheck,
                EfficiencyPercent = efficiency,
                JumpType = jumpType,
            };

            result.Steps.Add(new CalcStep("Fr_1", fr1, "-", "V1/sqrt(g*y1)"));
            result.Steps.Add(new CalcStep("y_2", y2, "ft", "y1/2*(-1+sqrt(1+8*Fr1^2)) Belanger"));
            result.Steps.Add(new CalcStep("dE", energyLoss, "ft", "(y2-y1)^3/(4*y1*y2)"));
            return result;
        }

        /// <summary>Critical depth — delegates to <see cref="ChannelHydraulics.CriticalDepth"/>.</summary>
        public static ChannelHydraulics.CriticalDepthResult ComputeCriticalDepth(
            double bottomWidthFt,
            double sideSlopeZ,
            double flowCfs)
            => ChannelHydraulics.CriticalDepth(bottomWidthFt, sideSlopeZ, flowCfs);

        /// <summary>
        /// Water surface profile via Standard Step Method (Newton-Raphson per reach).
        /// </summary>
        public static ProfileResult ComputeWaterSurfaceProfile(
            double flowCfs,
            ChannelParameters channel,
            GvfBoundaryType boundaryType,
            double knownBoundaryDepthFt,
            IReadOnlyList<Station> stations,
            double eddyLossCoefficient = DefaultEddyLossCoefficient)
        {
            if (flowCfs < 0) throw new ArgumentOutOfRangeException(nameof(flowCfs));
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            if (stations == null || stations.Count == 0)
                throw new ArgumentException("At least one station is required.", nameof(stations));
            if (eddyLossCoefficient < 0) throw new ArgumentOutOfRangeException(nameof(eddyLossCoefficient));

            double b = channel.BottomWidthFt;
            double z = channel.SideSlopeZ;
            double n = channel.ManningN;
            double s0 = channel.BedSlopeFtPerFt;

            if (b < 0) throw new ArgumentOutOfRangeException(nameof(channel.BottomWidthFt));
            if (z < 0) throw new ArgumentOutOfRangeException(nameof(channel.SideSlopeZ));
            if (n <= 0) throw new ArgumentOutOfRangeException(nameof(channel.ManningN));
            if (s0 < 0) throw new ArgumentOutOfRangeException(nameof(channel.BedSlopeFtPerFt));

            double startDepth = ResolveBoundaryDepth(flowCfs, channel, boundaryType, knownBoundaryDepthFt);
            double yn = ChannelHydraulics.NormalDepth(b, z, n, s0, flowCfs).DepthFt;
            double yc = ChannelHydraulics.CriticalDepth(b, z, flowCfs).DepthFt;
            bool isSubcritical = startDepth > yc;

            var sortedStations = stations
                .OrderBy(st => isSubcritical ? -st.DistanceFt : st.DistanceFt)
                .ToList();

            var profile = new List<ProfilePoint>();
            double prevDepth = startDepth;
            double g = ChannelHydraulics.G;

            for (int i = 0; i < sortedStations.Count; i++)
            {
                Station stn = sortedStations[i];

                if (i == 0)
                {
                    var geom = ChannelHydraulics.TrapezoidalGeometry(b, z, startDepth);
                    double v = geom.AreaFt2 > 0 ? flowCfs / geom.AreaFt2 : 0.0;
                    double hydraulicDepth = geom.TopWidthFt > 0 ? geom.AreaFt2 / geom.TopWidthFt : 0.0;
                    double fr = FroudeNumber(v, hydraulicDepth);
                    double sf = ManningFrictionSlope(flowCfs, b, z, n, startDepth);

                    profile.Add(new ProfilePoint
                    {
                        StationFt = stn.DistanceFt,
                        InvertElevFt = stn.InvertElevFt,
                        DepthFt = startDepth,
                        WaterSurfaceElevFt = stn.InvertElevFt + startDepth,
                        VelocityFps = v,
                        FroudeNumber = fr,
                        FlowRegime = FlowRegimeLabel(fr),
                        FrictionSlope = sf,
                    });
                    continue;
                }

                Station prevStn = sortedStations[i - 1];
                double dL = Math.Abs(stn.DistanceFt - prevStn.DistanceFt);
                double y2 = prevDepth;

                for (int iter = 0; iter < 50; iter++)
                {
                    var geom1 = ChannelHydraulics.TrapezoidalGeometry(b, z, prevDepth);
                    double v1 = geom1.AreaFt2 > 0 ? flowCfs / geom1.AreaFt2 : 0.0;
                    var geom2 = ChannelHydraulics.TrapezoidalGeometry(b, z, y2);
                    double v2 = geom2.AreaFt2 > 0 ? flowCfs / geom2.AreaFt2 : 0.0;

                    double sf1 = ManningFrictionSlope(flowCfs, b, z, n, prevDepth);
                    double sf2 = ManningFrictionSlope(flowCfs, b, z, n, y2);
                    double sfAvg = 0.5 * (sf1 + sf2);

                    double e1 = prevStn.InvertElevFt + prevDepth + v1 * v1 / (2.0 * g);
                    double e2 = stn.InvertElevFt + y2 + v2 * v2 / (2.0 * g);
                    double hf = sfAvg * dL;
                    double he = eddyLossCoefficient * Math.Abs(v1 * v1 / (2.0 * g) - v2 * v2 / (2.0 * g));

                    // Energy balance H_up = H_down + hf + he (Chow 11-4). The losses are ADDED
                    // when the unknown section is upstream (subcritical march runs
                    // downstream->upstream) and SUBTRACTED when it is downstream (supercritical
                    // march runs upstream->downstream); otherwise supercritical profiles solve
                    // to a downstream head higher than upstream.
                    double dir = isSubcritical ? 1.0 : -1.0;
                    double residual = e1 + dir * (hf + he) - e2;

                    if (Math.Abs(residual) < 1e-6)
                        break;

                    const double dy = 0.0001;
                    double y2p = y2 + dy;
                    var geom2p = ChannelHydraulics.TrapezoidalGeometry(b, z, y2p);
                    double v2p = geom2p.AreaFt2 > 0 ? flowCfs / geom2p.AreaFt2 : 0.0;
                    double sf2p = ManningFrictionSlope(flowCfs, b, z, n, y2p);
                    double sfAvgP = 0.5 * (sf1 + sf2p);
                    double e2p = stn.InvertElevFt + y2p + v2p * v2p / (2.0 * g);
                    double hfP = sfAvgP * dL;
                    double heP = eddyLossCoefficient * Math.Abs(v1 * v1 / (2.0 * g) - v2p * v2p / (2.0 * g));
                    double residualP = e1 + dir * (hfP + heP) - e2p;

                    double dRdy = (residualP - residual) / dy;
                    if (Math.Abs(dRdy) < 1e-12)
                        break;

                    y2 -= residual / dRdy;
                    y2 = Math.Max(0.01, y2);
                }

                {
                    var geom = ChannelHydraulics.TrapezoidalGeometry(b, z, y2);
                    double v = geom.AreaFt2 > 0 ? flowCfs / geom.AreaFt2 : 0.0;
                    double hydraulicDepth = geom.TopWidthFt > 0 ? geom.AreaFt2 / geom.TopWidthFt : 0.0;
                    double fr = FroudeNumber(v, hydraulicDepth);

                    profile.Add(new ProfilePoint
                    {
                        StationFt = stn.DistanceFt,
                        InvertElevFt = stn.InvertElevFt,
                        DepthFt = y2,
                        WaterSurfaceElevFt = stn.InvertElevFt + y2,
                        VelocityFps = v,
                        FroudeNumber = fr,
                        FlowRegime = FlowRegimeLabel(fr),
                        FrictionSlope = ManningFrictionSlope(flowCfs, b, z, n, y2),
                    });
                }

                prevDepth = y2;
            }

            profile.Sort((a, bpt) => a.StationFt.CompareTo(bpt.StationFt));
            string profileType = ClassifyProfile(s0, yn, yc, startDepth);

            var result = new ProfileResult
            {
                Profile = profile,
                ProfileType = profileType,
                NormalDepthFt = yn,
                CriticalDepthFt = yc,
                IsSubcritical = isSubcritical,
                BoundaryDepthFt = startDepth,
                BoundaryType = boundaryType,
            };

            result.Steps.Add(new CalcStep("y_n", yn, "ft", "Manning normal depth"));
            result.Steps.Add(new CalcStep("y_c", yc, "ft", "Q^2/g = A^3/T"));
            result.Steps.Add(new CalcStep("y_boundary", startDepth, "ft", boundaryType.ToString()));
            result.Steps.Add(new CalcStep("profile_type", 0, "", profileType));
            return result;
        }

        /// <summary>Specific energy E = y + V²/(2g).</summary>
        public static double SpecificEnergy(double depthFt, double velocityFps)
            => depthFt + velocityFps * velocityFps / (2.0 * ChannelHydraulics.G);

        /// <summary>Froude number Fr = V / sqrt(g * D_h) where D_h = A/T.</summary>
        public static double FroudeNumber(double velocityFps, double hydraulicDepthFt)
        {
            if (hydraulicDepthFt <= 0) return double.PositiveInfinity;
            return velocityFps / Math.Sqrt(ChannelHydraulics.G * hydraulicDepthFt);
        }

        /// <summary>Manning-based friction slope S_f = (V*n/(K_n*R^(2/3)))².</summary>
        public static double ManningFrictionSlope(
            double flowCfs,
            double bottomWidthFt,
            double sideSlopeZ,
            double manningN,
            double depthFt)
        {
            var geom = ChannelHydraulics.TrapezoidalGeometry(bottomWidthFt, sideSlopeZ, depthFt);
            if (geom.AreaFt2 <= 0 || geom.HydRadiusFt <= 0)
                return 0.0;

            double v = flowCfs / geom.AreaFt2;
            double ratio = v * manningN / (ChannelHydraulics.Kn * Math.Pow(geom.HydRadiusFt, 2.0 / 3.0));
            return ratio * ratio;
        }

        private static double ResolveBoundaryDepth(
            double flowCfs,
            ChannelParameters channel,
            GvfBoundaryType boundaryType,
            double knownBoundaryDepthFt)
        {
            switch (boundaryType)
            {
                case GvfBoundaryType.Normal:
                    return ChannelHydraulics.NormalDepth(
                        channel.BottomWidthFt,
                        channel.SideSlopeZ,
                        channel.ManningN,
                        channel.BedSlopeFtPerFt,
                        flowCfs).DepthFt;
                case GvfBoundaryType.Critical:
                    return ChannelHydraulics.CriticalDepth(
                        channel.BottomWidthFt,
                        channel.SideSlopeZ,
                        flowCfs).DepthFt;
                case GvfBoundaryType.Known:
                    if (knownBoundaryDepthFt <= 0)
                        throw new ArgumentOutOfRangeException(nameof(knownBoundaryDepthFt));
                    return knownBoundaryDepthFt;
                default:
                    throw new ArgumentOutOfRangeException(nameof(boundaryType));
            }
        }

        private static string ClassifyProfile(double s0, double yn, double yc, double startDepth)
        {
            if (s0 > 0 && yn > yc)
            {
                if (startDepth > yn) return "M1 (backwater)";
                if (startDepth < yn && startDepth > yc) return "M2 (drawdown)";
                return "M3 (supercritical on mild slope)";
            }

            if (s0 > 0 && yn < yc)
            {
                if (startDepth > yc) return "S1 (subcritical on steep slope)";
                if (startDepth < yc && startDepth > yn) return "S2 (drawdown)";
                return "S3 (supercritical below normal)";
            }

            return "Computed profile";
        }

        private static string FlowRegimeLabel(double fr)
        {
            if (fr < 1.0) return "subcritical";
            if (fr > 1.0) return "supercritical";
            return "critical";
        }
    }
}