using System.Collections.Generic;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class HglBackwaterTests
    {
        private static List<NetworkReach> TwoReachMain()
        {
            // Two full-barrel 2-ft reaches (A=pi, R=0.5), ordered upstream -> downstream.
            return new List<NetworkReach>
            {
                new NetworkReach { Name = "R1", LengthFt = 100, ManningN = 0.013, AreaFt2 = 3.14159, HydRadiusFt = 0.5, FlowCfs = 15 },
                new NetworkReach { Name = "R2-outfall", LengthFt = 100, ManningN = 0.013, AreaFt2 = 3.14159, HydRadiusFt = 0.5, FlowCfs = 20 },
            };
        }

        [Fact]
        public void Backwater_AnchorsOutfallAtTailwater()
        {
            const double tw = 100.0;
            var profile = Hgl.SteadyBackwaterFromOutfall(TwoReachMain(), tw, null);

            Assert.Equal(2, profile.Count);
            // Downstream end of the outfall reach is the tailwater.
            Assert.Equal(tw, profile[1].HglFt, 6);
        }

        [Fact]
        public void Backwater_RisesUpstream_ByFrictionPlusMinor()
        {
            const double tw = 100.0;
            var profile = Hgl.SteadyBackwaterFromOutfall(TwoReachMain(), tw, null);

            // Each reach: HGL_up = HGL_down + h_f + h_m.
            foreach (HglProfilePoint p in profile)
                Assert.Equal(p.HglFt + p.HfFt + p.HmFt, p.HglUpstreamFt, 6);

            // Continuity: the upstream reach's downstream node equals the outfall reach's upstream node.
            Assert.Equal(profile[0].HglFt, profile[1].HglUpstreamFt, 6);

            // HGL rises going upstream (tailwater-controlled), never below tailwater.
            Assert.True(profile[0].HglUpstreamFt > tw);
            Assert.True(profile[1].HglUpstreamFt > tw);
        }

        [Fact]
        public void Backwater_MinorLosses_RaiseUpstreamHgl()
        {
            const double tw = 100.0;
            var noMinor = Hgl.SteadyBackwaterFromOutfall(TwoReachMain(), tw, new HglProfileOptions());
            var withMinor = Hgl.SteadyBackwaterFromOutfall(TwoReachMain(), tw, new HglProfileOptions
            {
                IncludeJunctionLosses = true,
                IncludeExitLoss = true,
            });

            // Adding exit/junction losses can only raise the upstream HGL.
            Assert.True(withMinor[0].HglUpstreamFt >= noMinor[0].HglUpstreamFt);
        }

        [Fact]
        public void Backwater_BendLoss_RaisesUpstreamHgl()
        {
            const double tw = 100.0;
            var reaches = TwoReachMain();
            reaches[0].DeflectionAngleDeg = 45.0;
            reaches[0].HasContinuingOutflow = true;

            var noBend = Hgl.SteadyBackwaterFromOutfall(reaches, tw, new HglProfileOptions());
            var withBend = Hgl.SteadyBackwaterFromOutfall(reaches, tw, new HglProfileOptions
            {
                UseBendLoss = true,
            });

            Assert.True(withBend[0].HmFt > noBend[0].HmFt);
            Assert.True(withBend[0].HglUpstreamFt > noBend[0].HglUpstreamFt);
        }

        [Fact]
        public void Backwater_MomentumJunction_RaisesUpstreamHgl()
        {
            const double tw = 100.0;
            const double q = 10.0;
            var reaches = new List<NetworkReach>
            {
                new NetworkReach
                {
                    Name = "R1-in",
                    LengthFt = 100,
                    ManningN = 0.013,
                    AreaFt2 = 0.75,
                    HydRadiusFt = 0.35,
                    FlowCfs = q,
                    DiameterFt = 1.0,
                    FlowDepthFt = 0.75,
                    DownstreamInflowCount = 2,
                    HasContinuingOutflow = true,
                },
                new NetworkReach
                {
                    Name = "R2-out",
                    LengthFt = 100,
                    ManningN = 0.013,
                    AreaFt2 = 1.6,
                    HydRadiusFt = 0.55,
                    FlowCfs = q,
                    DiameterFt = 2.0,
                    FlowDepthFt = 0.80,
                },
            };

            var noMomentum = Hgl.SteadyBackwaterFromOutfall(reaches, tw, new HglProfileOptions());
            var withMomentum = Hgl.SteadyBackwaterFromOutfall(reaches, tw, new HglProfileOptions
            {
                UseMomentumJunction = true,
            });

            Assert.True(withMomentum[0].HmFt > noMomentum[0].HmFt);
            Assert.True(withMomentum[0].HglUpstreamFt > noMomentum[0].HglUpstreamFt);
        }
    }
}
