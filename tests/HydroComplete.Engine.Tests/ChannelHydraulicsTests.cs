using System;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class ChannelHydraulicsTests
    {
        [Fact]
        public void FlowAtDepth_StandardTrap_MatchesJsReference()
            => Assert.Equal(17.656, ChannelHydraulics.FlowAtDepth(2.0, 1.0, 1.0, 0.013, 0.005).FlowCfs, 2);

        [Fact]
        public void NormalDepth_RoundTrips_ThroughFlowAtDepth()
        {
            var nd = ChannelHydraulics.NormalDepth(2.0, 1.0, 0.013, 0.005, 17.656);
            var back = ChannelHydraulics.FlowAtDepth(2.0, 1.0, nd.DepthFt, 0.013, 0.005);
            Assert.Equal(17.656, back.FlowCfs, 2);
            Assert.Equal(1.0, nd.DepthFt, 2);
        }

        [Fact]
        public void CriticalDepth_FroudeNumber_IsNearUnity()
        {
            var crit = ChannelHydraulics.CriticalDepth(2.0, 1.0, 17.656);
            Assert.InRange(crit.FroudeNumber, 0.99, 1.01);
        }

        [Fact]
        public void TrapezoidalGeometry_AtUnitDepth_MatchesFormula()
        {
            var g = ChannelHydraulics.TrapezoidalGeometry(4.0, 2.0, 1.0);
            Assert.Equal(6.0, g.AreaFt2, 3);
            Assert.Equal(8.472, g.WettedPerimeterFt, 2);
            Assert.Equal(8.0, g.TopWidthFt, 3);
        }

        [Fact]
        public void FlowAtDepth_ZeroDepth_Throws()
            => Assert.Throws<ArgumentOutOfRangeException>(() =>
                ChannelHydraulics.FlowAtDepth(2.0, 1.0, 0.0, 0.013, 0.005));
    }
}