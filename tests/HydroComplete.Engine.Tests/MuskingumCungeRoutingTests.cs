using System;
using System.Linq;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class MuskingumCungeRoutingTests
    {
        [Fact]
        public void DeriveParameters_CoefficientsSumToUnity()
        {
            var inflow = new[] { 0.0, 10.0, 30.0, 50.0, 30.0, 10.0, 0.0 };
            var derived = MuskingumCungeRouting.DeriveParameters(
                inflow, new MuskingumCungeRouting.ReachParameters(), 0.5);
            Assert.Equal(1.0, derived.Sum, 3);
        }

        [Fact]
        public void Route_ReducesPeak_ForTypicalReach()
        {
            var inflow = new[] { 0.0, 10.0, 30.0, 50.0, 30.0, 10.0, 0.0 };
            var result = MuskingumCungeRouting.Route(
                inflow, new MuskingumCungeRouting.ReachParameters(), 0.5);
            Assert.True(result.PeakOutflowCfs < result.PeakInflowCfs);
            Assert.True(result.PeakReductionPercent > 0);
        }

        [Fact]
        public void Route_CelerityIsFiveThirdsVelocity()
        {
            var inflow = new[] { 0.0, 100.0, 200.0, 100.0, 0.0 };
            var result = MuskingumCungeRouting.Route(
                inflow, new MuskingumCungeRouting.ReachParameters(), 0.25);
            Assert.Equal(
                (5.0 / 3.0) * result.Parameters.ReferenceVelocityFps,
                result.Parameters.CelerityFps,
                4);
        }

        [Fact]
        public void Route_X_IsWithinValidRange()
        {
            var result = MuskingumCungeRouting.Route(
                new[] { 0.0, 20.0, 40.0, 20.0, 0.0 },
                new MuskingumCungeRouting.ReachParameters(),
                0.5);
            Assert.InRange(result.Parameters.X, 0.0, 0.5);
        }

        [Fact]
        public void Route_EmptyInflow_Throws()
            => Assert.Throws<ArgumentOutOfRangeException>(() =>
                MuskingumCungeRouting.Route(Array.Empty<double>(),
                    new MuskingumCungeRouting.ReachParameters(), 0.5));
    }
}