using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    /// <summary>
    /// Plan deflection and bend-loss K used by Civil3D NetworkTopology.BuildReaches.
    /// </summary>
    public class NetworkTopologyTests
    {
        [Theory]
        [InlineData(1, 0, 1, 0, 0)]      // straight-through
        [InlineData(1, 0, 0, 1, 90)]     // 90° bend
        [InlineData(1, 0, -1, 0, 180)]   // reverse
        public void PlanDeflection_ComputesExpectedAngle(
            double inX, double inY, double outX, double outY, double expectedDeg)
        {
            double angle = PipePlanGeometry.DeflectionDegrees(inX, inY, outX, outY);
            Assert.Equal(expectedDeg, angle, 1);
        }

        [Fact]
        public void BendLossK_At45Deg_MatchesHec22Table()
        {
            double k = Hec22.BendLossK(45.0);
            Assert.Equal(Hec22.BendLossK45Deg, k, 4);
        }

        [Fact]
        public void BendLossK_FromPlanDeflection_MatchesInterpolatedValue()
        {
            (double inX, double inY) = PipePlanGeometry.FlowDirection(0, 0, 10, 0);
            (double outX, double outY) = PipePlanGeometry.FlowDirection(10, 0, 17.07, 7.07);
            double angle = PipePlanGeometry.DeflectionDegrees(inX, inY, outX, outY);
            double k = Hec22.BendLossK(angle);

            Assert.Equal(45.0, angle, 1);
            Assert.Equal(Hec22.BendLossK45Deg, k, 4);
        }

        [Fact]
        public void BendLossK_At90Deg_MatchesHec22Anchor()
        {
            double k = Hec22.BendLossK(90.0);
            Assert.Equal(Hec22.BendLossK90Deg, k, 4);
        }
    }
}