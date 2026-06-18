using System;
using System.Collections.Generic;
using System.Linq;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class GraduallyVariedFlowTests
    {
        private static GraduallyVariedFlow.ChannelParameters MildChannel => new GraduallyVariedFlow.ChannelParameters
        {
            BottomWidthFt = 10.0,
            SideSlopeZ = 2.0,
            ManningN = 0.03,
            BedSlopeFtPerFt = 0.001,
        };

        private static IReadOnlyList<GraduallyVariedFlow.Station> ThreeStations =>
            new[]
            {
                new GraduallyVariedFlow.Station { DistanceFt = 0.0, InvertElevFt = 100.0 },
                new GraduallyVariedFlow.Station { DistanceFt = 100.0, InvertElevFt = 99.9 },
                new GraduallyVariedFlow.Station { DistanceFt = 200.0, InvertElevFt = 99.8 },
            };

        [Fact]
        public void ComputeCriticalDepth_MatchesChannelHydraulics()
        {
            double q = 100.0;
            var gvf = GraduallyVariedFlow.ComputeCriticalDepth(10.0, 2.0, q);
            var ch = ChannelHydraulics.CriticalDepth(10.0, 2.0, q);
            Assert.Equal(ch.DepthFt, gvf.DepthFt, 4);
            Assert.InRange(gvf.FroudeNumber, 0.99, 1.01);
        }

        [Fact]
        public void ConjugateDepth_Belanger_HandCalculation()
        {
            // y1=0.5 ft, V1=10 ft/s => Fr1 = 10/sqrt(32.174*0.5) ≈ 2.493
            // y2 = 0.5*0.5*(-1+sqrt(1+8*Fr1^2)) ≈ 1.531 ft
            var result = GraduallyVariedFlow.ConjugateDepth(0.5, 10.0, 10.0);
            Assert.True(result.IsValid);
            Assert.Equal(2.493, result.FroudeUpstream, 2);
            Assert.Equal(1.531, result.ConjugateDepthFt, 2);
            Assert.True(result.EnergyLossFt > 0);
            Assert.Contains("Belanger", result.Steps[1].Formula);
        }

        [Fact]
        public void ConjugateDepth_SubcriticalUpstream_ReturnsError()
        {
            var result = GraduallyVariedFlow.ConjugateDepth(2.0, 1.0, 10.0);
            Assert.False(result.IsValid);
            Assert.NotNull(result.ErrorMessage);
        }

        [Fact]
        public void NormalBoundaryDepth_MatchesNormalDepthSolver()
        {
            const double q = 100.0;
            var channel = MildChannel;
            var nd = ChannelHydraulics.NormalDepth(
                channel.BottomWidthFt, channel.SideSlopeZ, channel.ManningN,
                channel.BedSlopeFtPerFt, q);

            var profile = GraduallyVariedFlow.ComputeWaterSurfaceProfile(
                q, channel, GraduallyVariedFlow.GvfBoundaryType.Normal, 0.0,
                new[] { ThreeStations[0] });

            Assert.Equal(nd.DepthFt, profile.BoundaryDepthFt, 4);
        }

        [Fact]
        public void CriticalBoundaryDepth_MatchesCriticalDepthSolver()
        {
            const double q = 100.0;
            var channel = MildChannel;
            var yc = ChannelHydraulics.CriticalDepth(
                channel.BottomWidthFt, channel.SideSlopeZ, q);

            var profile = GraduallyVariedFlow.ComputeWaterSurfaceProfile(
                q, channel, GraduallyVariedFlow.GvfBoundaryType.Critical, 0.0,
                new[] { ThreeStations[0] });

            Assert.Equal(yc.DepthFt, profile.BoundaryDepthFt, 4);
        }

        [Fact]
        public void KnownBoundaryDepth_UsesSpecifiedValue()
        {
            const double known = 3.25;
            var profile = GraduallyVariedFlow.ComputeWaterSurfaceProfile(
                100.0, MildChannel, GraduallyVariedFlow.GvfBoundaryType.Known, known,
                new[] { ThreeStations[0] });

            Assert.Equal(known, profile.BoundaryDepthFt, 4);
        }

        [Fact]
        public void M1Profile_Backwater_RisesUpstream()
        {
            const double q = 100.0;
            var channel = MildChannel;
            double yn = ChannelHydraulics.NormalDepth(
                channel.BottomWidthFt, channel.SideSlopeZ, channel.ManningN,
                channel.BedSlopeFtPerFt, q).DepthFt;

            // Downstream normal depth with depth above yn at upstream => M1
            var stations = new[]
            {
                new GraduallyVariedFlow.Station { DistanceFt = 0.0, InvertElevFt = 100.0 },
                new GraduallyVariedFlow.Station { DistanceFt = 200.0, InvertElevFt = 99.8 },
            };

            var profile = GraduallyVariedFlow.ComputeWaterSurfaceProfile(
                q, channel, GraduallyVariedFlow.GvfBoundaryType.Known, yn + 0.5, stations);

            Assert.Contains("M1", profile.ProfileType);
            Assert.True(profile.IsSubcritical);
            var upstream = profile.Profile.First(p => p.StationFt == 0.0);
            var downstream = profile.Profile.First(p => p.StationFt == 200.0);
            // M1 backwater: water surface rises upstream (depth may fall if invert steps up)
            Assert.True(upstream.WaterSurfaceElevFt > downstream.WaterSurfaceElevFt);
            Assert.True(downstream.DepthFt > yn);
        }

        [Fact]
        public void SteepSlope_S1_Classification_WhenSubcriticalAboveCritical()
        {
            var channel = new GraduallyVariedFlow.ChannelParameters
            {
                BottomWidthFt = 10.0,
                SideSlopeZ = 2.0,
                ManningN = 0.03,
                BedSlopeFtPerFt = 0.03,
            };
            const double q = 50.0;

            double yn = ChannelHydraulics.NormalDepth(
                channel.BottomWidthFt, channel.SideSlopeZ, channel.ManningN,
                channel.BedSlopeFtPerFt, q).DepthFt;
            double yc = ChannelHydraulics.CriticalDepth(
                channel.BottomWidthFt, channel.SideSlopeZ, q).DepthFt;

            Assert.True(yn < yc);

            var profile = GraduallyVariedFlow.ComputeWaterSurfaceProfile(
                q, channel, GraduallyVariedFlow.GvfBoundaryType.Known, yc + 0.3,
                new[] { ThreeStations[0] });

            Assert.Contains("S1", profile.ProfileType);
        }

        [Fact]
        public void Profile_Stations_SortedAscendingByChainage()
        {
            var profile = GraduallyVariedFlow.ComputeWaterSurfaceProfile(
                100.0, MildChannel, GraduallyVariedFlow.GvfBoundaryType.Normal, 0.0, ThreeStations);

            var distances = profile.Profile.Select(p => p.StationFt).ToList();
            Assert.Equal(new[] { 0.0, 100.0, 200.0 }, distances);
        }

        [Fact]
        public void Profile_BoundaryStation_HasCalcStepTraces()
        {
            var profile = GraduallyVariedFlow.ComputeWaterSurfaceProfile(
                100.0, MildChannel, GraduallyVariedFlow.GvfBoundaryType.Normal, 0.0, ThreeStations);

            Assert.Contains(profile.Steps, s => s.Label == "y_n");
            Assert.Contains(profile.Steps, s => s.Label == "y_c");
            Assert.Contains(profile.Steps, s => s.Label == "y_boundary");
            Assert.Contains(profile.Steps, s => s.Label == "profile_type");
        }

        [Fact]
        public void ManningFrictionSlope_IsPositiveAtNormalDepth()
        {
            double yn = ChannelHydraulics.NormalDepth(
                MildChannel.BottomWidthFt, MildChannel.SideSlopeZ, MildChannel.ManningN,
                MildChannel.BedSlopeFtPerFt, 100.0).DepthFt;

            double sf = GraduallyVariedFlow.ManningFrictionSlope(
                100.0, MildChannel.BottomWidthFt, MildChannel.SideSlopeZ,
                MildChannel.ManningN, yn);

            Assert.True(sf > 0);
            Assert.InRange(sf, MildChannel.BedSlopeFtPerFt * 0.5, MildChannel.BedSlopeFtPerFt * 2.0);
        }

        [Fact]
        public void SpecificEnergy_MatchesFormula()
        {
            double e = GraduallyVariedFlow.SpecificEnergy(2.0, 5.0);
            double expected = 2.0 + 25.0 / (2.0 * ChannelHydraulics.G);
            Assert.Equal(expected, e, 4);
        }

        [Fact]
        public void FroudeNumber_SubcriticalFlow_LessThanOne()
        {
            var profile = GraduallyVariedFlow.ComputeWaterSurfaceProfile(
                100.0, MildChannel, GraduallyVariedFlow.GvfBoundaryType.Normal, 0.0, ThreeStations);

            foreach (var pt in profile.Profile)
                Assert.True(pt.FroudeNumber < 1.0);
        }

        [Fact]
        public void WaterSurfaceElevation_EqualsInvertPlusDepth()
        {
            var profile = GraduallyVariedFlow.ComputeWaterSurfaceProfile(
                100.0, MildChannel, GraduallyVariedFlow.GvfBoundaryType.Normal, 0.0, ThreeStations);

            foreach (var pt in profile.Profile)
                Assert.Equal(pt.InvertElevFt + pt.DepthFt, pt.WaterSurfaceElevFt, 4);
        }

        [Fact]
        public void ComputeWaterSurfaceProfile_EmptyStations_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                GraduallyVariedFlow.ComputeWaterSurfaceProfile(
                    100.0, MildChannel, GraduallyVariedFlow.GvfBoundaryType.Normal, 0.0,
                    Array.Empty<GraduallyVariedFlow.Station>()));
        }
    }
}