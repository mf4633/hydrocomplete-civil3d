using System;
using System.Collections.Generic;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class ManningTests
    {
        // D=2 ft, n=0.013, S=0.01 -> Q_full ~ 22.6 cfs, V_full ~ 7.2 ft/s (hand-checked).
        [Fact]
        public void Capacity_FullBarrel_MatchesHandCalc()
        {
            var pipe = new PipeSegment { DiameterFt = 2.0, ManningN = 0.013, Slope = 0.01 };
            var r = Manning.Capacity(pipe);
            Assert.Equal(22.62, r.FullFlowCfs, 1);   // within 0.05
            Assert.Equal(7.20, r.FullVelocityFps, 1);
        }

        [Fact]
        public void Capacity_PeakExceedsFull_ByAboutEightPercent()
        {
            var pipe = new PipeSegment { DiameterFt = 2.0, ManningN = 0.013, Slope = 0.01 };
            var r = Manning.Capacity(pipe);
            double ratio = r.PeakFlowCfs / r.FullFlowCfs;
            Assert.InRange(ratio, 1.05, 1.10); // classic circular-pipe peak/full ~1.08
        }

        [Fact]
        public void NormalDepth_RoundTrips_ThroughFlowAtDepth()
        {
            var pipe = new PipeSegment { DiameterFt = 2.0, ManningN = 0.013, Slope = 0.01 };
            double target = 10.0;
            var nd = Manning.NormalDepth(pipe, target);
            Assert.False(nd.Surcharged);
            Assert.InRange(nd.RelativeDepth, 0.0, 0.94);
            double backOut = Manning.FlowAtDepth(pipe.DiameterFt, nd.DepthFt, pipe.ManningN, pipe.Slope);
            Assert.Equal(target, backOut, 2); // bisection converged to the target flow
        }

        [Fact]
        public void NormalDepth_FlowAbovePeak_FlagsSurcharge()
        {
            var pipe = new PipeSegment { DiameterFt = 2.0, ManningN = 0.013, Slope = 0.01 };
            var nd = Manning.NormalDepth(pipe, 100.0); // far above peak capacity
            Assert.True(nd.Surcharged);
            Assert.Equal(pipe.DiameterFt, nd.DepthFt, 5);
        }

        [Fact]
        public void FlowAtDepth_BelowInvert_IsZero()
        {
            Assert.Equal(0.0, Manning.FlowAtDepth(2.0, 0.0, 0.013, 0.01));
        }

        [Fact]
        public void Capacity_ZeroSlope_Throws()
        {
            var pipe = new PipeSegment { DiameterFt = 2.0, ManningN = 0.013, Slope = 0.0 };
            Assert.Throws<ArgumentOutOfRangeException>(() => Manning.Capacity(pipe));
        }

        [Fact]
        public void PartialFlowGeometry_AtFullDepth_MatchesFullBarrel()
        {
            double d = 2.0;
            var (area, r) = Manning.PartialFlowGeometry(d, d);
            Assert.Equal(Math.PI * d * d / 4.0, area, 4);
            Assert.Equal(d / 4.0, r, 4);
        }

        [Fact]
        public void PartialFlowGeometry_BelowInvert_IsZero()
        {
            var (area, r) = Manning.PartialFlowGeometry(2.0, 0.0);
            Assert.Equal(0.0, area);
            Assert.Equal(0.0, r);
        }
    }

    public class ReachFactoryTests
    {
        [Fact]
        public void FromNormalDepth_SubCapacityFlow_UsesPartialGeometry()
        {
            var pipe = new PipeSegment { DiameterFt = 2.0, ManningN = 0.013, Slope = 0.01 };
            double designQ = 10.0;
            var reach = ReachFactory.FromNormalDepth(pipe, designQ, lengthFt: 100.0, name: "P1");

            Assert.False(reach.FlowSurcharged);
            Assert.InRange(reach.RelativeDepth, 0.0, 0.94);
            Assert.True(reach.AreaFt2 < Math.PI * pipe.DiameterFt * pipe.DiameterFt / 4.0);
            Assert.True(reach.HydRadiusFt < pipe.DiameterFt / 4.0);
            Assert.True(reach.VelHeadDownFt > 0);
            Assert.Equal(reach.VelHeadUpFt, reach.VelHeadDownFt, 6);
            Assert.Equal(pipe.DiameterFt, reach.DiameterFt);
        }

        [Fact]
        public void FromNormalDepth_AbovePeak_FlagsSurchargeAndUsesFullBarrel()
        {
            var pipe = new PipeSegment { DiameterFt = 2.0, ManningN = 0.013, Slope = 0.01 };
            var reach = ReachFactory.FromNormalDepth(pipe, 100.0);

            Assert.True(reach.FlowSurcharged);
            Assert.Equal(1.0, reach.RelativeDepth, 4);
            double areaFull = Math.PI * pipe.DiameterFt * pipe.DiameterFt / 4.0;
            Assert.Equal(areaFull, reach.AreaFt2, 4);
            Assert.Equal(pipe.DiameterFt / 4.0, reach.HydRadiusFt, 4);
        }

        [Fact]
        public void FromFullBarrel_MatchesLegacyGeometry()
        {
            var pipe = new PipeSegment { DiameterFt = 2.0, ManningN = 0.013, Slope = 0.01 };
            var reach = ReachFactory.FromFullBarrel(pipe, 10.0, lengthFt: 50.0);

            Assert.False(reach.FlowSurcharged);
            Assert.Equal(1.0, reach.RelativeDepth, 4);
            Assert.Equal(Math.PI * 4.0 / 4.0, reach.AreaFt2, 4);
            Assert.Equal(0.5, reach.HydRadiusFt, 4);
        }
    }

    public class RationalTests
    {
        [Fact]
        public void Peak_Single_IsCiA()
        {
            var r = Rational.Peak(0.85, 4.0, 2.5);
            Assert.Equal(8.5, r.PeakFlowCfs, 6);
        }

        [Fact]
        public void Peak_Composite_UsesAreaWeightedC()
        {
            var cs = new List<Catchment>
            {
                new Catchment { Name = "A", RunoffC = 0.9, AreaAcres = 1.0 },
                new Catchment { Name = "B", RunoffC = 0.5, AreaAcres = 3.0 },
            };
            var r = Rational.Peak(cs, 3.0);
            Assert.Equal(0.6, r.CompositeC, 6);   // (0.9*1 + 0.5*3)/4
            Assert.Equal(4.0, r.TotalAreaAcres, 6);
            Assert.Equal(7.2, r.PeakFlowCfs, 6);  // 0.6 * 3 * 4
        }

        [Fact]
        public void Peak_C_OutOfRange_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Rational.Peak(1.5, 4.0, 2.0));
        }
    }

    public class TimeOfConcentrationTests
    {
        [Fact]
        public void Kirpich_MatchesHandCalc()
        {
            var r = TimeOfConcentration.Kirpich(1000.0, 0.01);
            Assert.Equal(9.37, r.TcMinutes, 1); // 0.0078*1000^0.77*0.01^-0.385
        }

        [Fact]
        public void FromReaches_SumsTravelTimes()
        {
            var reaches = new List<TimeOfConcentration.TravelReach>
            {
                new TimeOfConcentration.TravelReach { Name = "sheet", LengthFt = 300, VelocityFps = 1.5 },
                new TimeOfConcentration.TravelReach { Name = "channel", LengthFt = 1200, VelocityFps = 4.0 },
            };
            var r = TimeOfConcentration.FromReaches(reaches);
            Assert.Equal(8.333, r.TcMinutes, 2); // 3.333 + 5.000
        }

        [Fact]
        public void Kirpich_ZeroLength_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => TimeOfConcentration.Kirpich(0.0, 0.01));
        }
    }

    public class IdfTests
    {
        [Fact]
        public void Intensity_MatchesHandCalc()
        {
            var curve = new IdfCurve(120.0, 12.0, 0.85);
            var r = curve.Intensity(10.0);
            Assert.Equal(8.68, r.IntensityInHr, 1); // 120/(10+12)^0.85
            Assert.Equal(10.0, r.DurationMin, 6);
        }

        [Fact]
        public void Intensity_BelowFloor_UsesMinDuration()
        {
            var curve = new IdfCurve(120.0, 12.0, 0.85, minDurationMin: 5.0);
            var r = curve.Intensity(2.0);
            Assert.Equal(5.0, r.DurationMin, 6);    // floored
            Assert.Equal(10.80, r.IntensityInHr, 1); // 120/(5+12)^0.85
        }
    }

    public class Hec22Tests
    {
        [Fact]
        public void MinorHeadLoss_DefaultManholeK_IsKTimesVh()
        {
            double vh = Hec22.VelocityHeadFt(5.0);
            var r = Hec22.MinorHeadLoss(Hec22.DefaultManholeK, vh);
            Assert.Equal(Hec22.DefaultManholeK * vh, r.HeadLossFt, 6);
            Assert.NotEmpty(r.Steps);
        }

        [Fact]
        public void VelocityHeadFromFlow_MatchesManual()
        {
            // Q=10 cfs, A=3 ft² -> V=3.333 fps -> Vh=0.1727 ft
            double vh = Hec22.VelocityHeadFromFlow(10.0, 3.0);
            Assert.Equal(0.173, vh, 2);
        }

        [Theory]
        [InlineData(0, 0.0)]
        [InlineData(45, 0.1485)]
        [InlineData(90, 0.297)]
        public void BendLossK_TableAnchors_MatchHec22Eq7_5(double degrees, double expectedK)
        {
            Assert.Equal(expectedK, Hec22.BendLossK(degrees), 4);
        }

        [Fact]
        public void BendLossK_InterpolatesBetweenTableValues()
        {
            double k22 = Hec22.BendLossK(22.5);
            Assert.Equal(0.07425, k22, 4);
        }

        [Fact]
        public void BendLossK_NegativeDegrees_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Hec22.BendLossK(-1.0));
        }
    }

    public class Atlas14PresetsTests
    {
        [Fact]
        public void Find_Charlotte_ReturnsPreset()
        {
            var p = Atlas14Presets.Find("charlotte-nc");
            Assert.NotNull(p);
            Assert.Equal("Charlotte, NC", p!.DisplayName);
        }

        [Fact]
        public void Nearest_CharlotteCoords_PicksCharlotte()
        {
            var p = Atlas14Presets.Nearest(35.23, -80.84);
            Assert.Equal("charlotte-nc", p.Key);
        }

        [Fact]
        public void ResolveForDrawing_CharlotteCoords_ReturnsCharlotte()
        {
            var p = Atlas14Presets.ResolveForDrawing(35.23, -80.84);
            Assert.NotNull(p);
            Assert.Equal("charlotte-nc", p!.Key);
        }

        [Fact]
        public void ResolveForDrawing_NullCoords_ReturnsNull()
        {
            Assert.Null(Atlas14Presets.ResolveForDrawing(null, -80.84));
            Assert.Null(Atlas14Presets.ResolveForDrawing(35.23, null));
            Assert.Null(Atlas14Presets.ResolveForDrawing(null, null));
        }

        [Fact]
        public void ResolveForDrawing_InvalidCoords_ReturnsNull()
        {
            Assert.Null(Atlas14Presets.ResolveForDrawing(95.0, -80.84));
            Assert.Null(Atlas14Presets.ResolveForDrawing(35.23, -200.0));
        }

        [Fact]
        public void PeakFromCatchments_UsesSystemTc()
        {
            var catchments = new List<Catchment>
            {
                new Catchment { Name = "A", RunoffC = 0.85, AreaAcres = 2.0, TcMinutes = 12.0 },
                new Catchment { Name = "B", RunoffC = 0.55, AreaAcres = 1.0, TcMinutes = 8.0 },
            };
            var preset = Atlas14Presets.Find("charlotte-nc");
            var r = Atlas14Presets.PeakFromCatchments(catchments, preset!);
            Assert.True(r.PeakFlowCfs > 0);
            Assert.True(r.IntensityInHr > 0);
        }
    }

    public class HglTests
    {
        // Trap Q=17.656 cfs, n=0.013, A=3 ft², R=0.6708 ft, L=100 ft -> hf ~0.500 ft (hglStep0_2).
        [Fact]
        public void ManningFrictionHeadLoss_MatchesHydroToolsHglStep0_2()
        {
            var r = Hgl.ManningFrictionHeadLoss(17.656, 0.013, 3.0, 0.6708, 100.0);
            Assert.Equal(0.5, r.HfFt, 1);
            Assert.NotEmpty(r.Steps);
        }

        [Fact]
        public void SteadyNetworkHglProfile_TwoReaches_ReturnsDescendingHgl()
        {
            var reaches = new List<NetworkReach>
            {
                new NetworkReach
                {
                    Name = "R1",
                    LengthFt = 100.0,
                    ManningN = 0.013,
                    AreaFt2 = 3.0,
                    HydRadiusFt = 0.6708,
                    FlowCfs = 17.656,
                },
                new NetworkReach
                {
                    Name = "R2",
                    LengthFt = 100.0,
                    ManningN = 0.013,
                    AreaFt2 = 3.0,
                    HydRadiusFt = 0.6708,
                    FlowCfs = 17.656,
                },
            };

            var profile = Hgl.SteadyNetworkHglProfile(reaches, startHglFt: 10.0);

            Assert.Equal(2, profile.Count);
            Assert.True(profile[0].HglFt > profile[1].HglFt);
            Assert.True(profile[0].HfFt > 0);
            Assert.NotEmpty(profile[0].Steps);
        }

        [Fact]
        public void CrownElevationFt_IsInvertPlusDiameter()
        {
            Assert.Equal(105.0, Hgl.CrownElevationFt(100.0, 5.0), 6);
        }

        [Fact]
        public void IsSurcharged_FlagsWhenHglExceedsEitherCrown()
        {
            Assert.False(Hgl.IsSurcharged(104.0, 103.0, 100.0, 99.0, 5.0));
            Assert.True(Hgl.IsSurcharged(105.1, 103.0, 100.0, 99.0, 5.0));
            Assert.True(Hgl.IsSurcharged(104.0, 104.1, 100.0, 99.0, 5.0));
            Assert.True(Hgl.IsSurcharged(106.0, 105.0, 100.0, 99.0, 5.0));
        }

        [Fact]
        public void IsSurcharged_AtCrownIsNotSurcharged()
        {
            Assert.False(Hgl.IsSurcharged(105.0, 104.0, 100.0, 99.0, 5.0));
            Assert.False(Hgl.IsSurcharged(104.0, 104.0, 100.0, 99.0, 5.0));
        }

        [Fact]
        public void SteadyNetworkHglProfile_NormalDepth_DiffersFromFullBarrel_WhenQBelowFull()
        {
            var pipe = new PipeSegment { DiameterFt = 2.0, ManningN = 0.013, Slope = 0.01 };
            double designQ = 10.0;

            var normalReach = ReachFactory.FromNormalDepth(pipe, designQ, lengthFt: 100.0, name: "N");
            var fullReach = ReachFactory.FromFullBarrel(pipe, designQ, lengthFt: 100.0, name: "F");

            var normalProfile = Hgl.SteadyNetworkHglProfile(
                new List<NetworkReach> { normalReach }, startHglFt: 10.0);
            var fullProfile = Hgl.SteadyNetworkHglProfile(
                new List<NetworkReach> { fullReach }, startHglFt: 10.0);

            Assert.NotEqual(normalProfile[0].HfFt, fullProfile[0].HfFt);
            Assert.True(normalProfile[0].HfFt > fullProfile[0].HfFt);
            Assert.Equal(normalReach.RelativeDepth, normalProfile[0].RelativeDepth, 4);
            Assert.False(normalProfile[0].FlowSurcharged);
        }

        [Fact]
        public void SteadyNetworkHglProfile_WithJunctionLosses_DropsMoreThanFrictionOnly()
        {
            var reaches = new List<NetworkReach>
            {
                new NetworkReach
                {
                    Name = "R1",
                    LengthFt = 100.0,
                    ManningN = 0.013,
                    AreaFt2 = 3.0,
                    HydRadiusFt = 0.6708,
                    FlowCfs = 17.656,
                    JunctionLossK = Hec22.DefaultManholeK,
                },
            };

            var plain = Hgl.SteadyNetworkHglProfile(reaches, startHglFt: 10.0);
            var options = new HglProfileOptions { IncludeJunctionLosses = true };
            var withLoss = Hgl.SteadyNetworkHglProfile(reaches, startHglFt: 10.0, options);

            Assert.True(withLoss[0].HglFt < plain[0].HglFt);
            Assert.True(withLoss[0].HmFt > 0);
        }

        [Fact]
        public void SteadyNetworkHglProfile_WithBendLossK_DropsMoreThanJunctionOnly()
        {
            var baseReach = new NetworkReach
            {
                Name = "R1",
                LengthFt = 100.0,
                ManningN = 0.013,
                AreaFt2 = 3.0,
                HydRadiusFt = 0.6708,
                FlowCfs = 17.656,
                JunctionLossK = Hec22.DefaultManholeK,
            };

            var options = new HglProfileOptions { IncludeJunctionLosses = true };
            var junctionOnly = Hgl.SteadyNetworkHglProfile(
                new List<NetworkReach> { baseReach }, startHglFt: 10.0, options);

            var withBendReach = new NetworkReach
            {
                Name = baseReach.Name,
                LengthFt = baseReach.LengthFt,
                ManningN = baseReach.ManningN,
                AreaFt2 = baseReach.AreaFt2,
                HydRadiusFt = baseReach.HydRadiusFt,
                FlowCfs = baseReach.FlowCfs,
                JunctionLossK = baseReach.JunctionLossK,
                BendLossK = Hec22.BendLossK(45),
            };
            var withBend = Hgl.SteadyNetworkHglProfile(
                new List<NetworkReach> { withBendReach }, startHglFt: 10.0, options);

            Assert.True(withBend[0].HglFt < junctionOnly[0].HglFt);
            Assert.True(withBend[0].HmFt > junctionOnly[0].HmFt);
        }
    }
}
