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
    }
}
