using System;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class MomentumJunctionTests
    {
        [Fact]
        public void StraightThroughLoss_EqualVelocities_ReturnsZero()
        {
            double q = 10.0;
            double d = 2.0;
            double y = 1.2;
            var up = new MomentumJunction.PipeLeg { DiameterFt = d, DepthFt = y, FlowCfs = q };
            var down = new MomentumJunction.PipeLeg { DiameterFt = d, DepthFt = y, FlowCfs = q };

            var r = MomentumJunction.StraightThroughLoss(up, down);

            Assert.Equal(0.0, r.HglDropFt, 9);
            Assert.Equal(r.UpstreamVelocityFps, r.DownstreamVelocityFps, 6);
            Assert.NotEmpty(r.Steps);
        }

        [Fact]
        public void StraightThroughLoss_Expansion_PositiveHglDrop()
        {
            // Smaller upstream -> higher velocity -> momentum loss when entering larger pipe.
            double q = 10.0;
            var up = new MomentumJunction.PipeLeg { DiameterFt = 1.0, DepthFt = 0.75, FlowCfs = q };
            var down = new MomentumJunction.PipeLeg { DiameterFt = 2.0, DepthFt = 0.80, FlowCfs = q };

            double aUp = MomentumJunction.FlowAreaFt2(up.DiameterFt, up.DepthFt);
            double aDown = MomentumJunction.FlowAreaFt2(down.DiameterFt, down.DepthFt);
            double vUp = q / aUp;
            double vDown = q / aDown;
            double expected = Math.Max(0.0, q * (vUp - vDown) / (Hec22.G_Fps2 * aDown));

            var r = MomentumJunction.StraightThroughLoss(up, down);

            Assert.True(vUp > vDown);
            Assert.Equal(expected, r.HglDropFt, 6);
            Assert.True(r.HglDropFt > 0);
        }

        [Fact]
        public void StraightThroughLoss_Contraction_ClampedToZero()
        {
            // Larger upstream -> lower velocity; raw momentum term is negative.
            double q = 10.0;
            var up = new MomentumJunction.PipeLeg { DiameterFt = 2.0, DepthFt = 0.80, FlowCfs = q };
            var down = new MomentumJunction.PipeLeg { DiameterFt = 1.0, DepthFt = 0.75, FlowCfs = q };

            var r = MomentumJunction.StraightThroughLoss(up, down);

            Assert.True(r.UpstreamVelocityFps < r.DownstreamVelocityFps);
            Assert.Equal(0.0, r.HglDropFt, 9);
        }

        [Fact]
        public void FlowAreaFt2_Surcharged_UsesFullBarrel()
        {
            double d = 2.0;
            double area = MomentumJunction.FlowAreaFt2(d, d);
            Assert.Equal(Math.PI * d * d / 4.0, area, 6);
        }

        [Fact]
        public void StraightThroughLoss_NullLeg_Throws()
        {
            var leg = new MomentumJunction.PipeLeg { DiameterFt = 2.0, DepthFt = 1.0, FlowCfs = 5.0 };
            Assert.Throws<ArgumentNullException>(() => MomentumJunction.StraightThroughLoss(null!, leg));
            Assert.Throws<ArgumentNullException>(() => MomentumJunction.StraightThroughLoss(leg, null!));
        }
    }
}