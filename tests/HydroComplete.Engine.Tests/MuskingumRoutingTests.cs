using System;
using System.Linq;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class MuskingumRoutingTests
    {
        [Fact]
        public void Coefficients_SumToUnity()
            => Assert.Equal(1.0, MuskingumRouting.ComputeCoefficients(1.0, 0.2, 0.1).Sum, 4);

        [Fact]
        public void Route_ReducesPeak_ForTypicalReach()
        {
            var result = MuskingumRouting.Route(new[] { 0.0, 10.0, 30.0, 50.0, 30.0, 10.0, 0.0 }, 1.0, 0.2, 0.1);
            Assert.True(result.PeakOutflowCfs < result.PeakInflowCfs);
        }

        [Fact]
        public void Route_PreservesVolume_WithinTolerance()
        {
            var inflow = new[] { 0.0, 5.0, 15.0, 25.0, 20.0, 10.0, 5.0, 0.0 };
            var result = MuskingumRouting.Route(inflow, 0.5, 0.2, 0.1);
            double relError = Math.Abs(result.InflowVolumeAcreFt - result.OutflowVolumeAcreFt) / result.InflowVolumeAcreFt;
            Assert.True(relError < 0.02);
        }

        [Fact]
        public void Route_ConstantInflow_ApproachesSteadyState()
        {
            var result = MuskingumRouting.Route(Enumerable.Repeat(10.0, 20).ToArray(), 1.0, 0.2, 0.1);
            Assert.Equal(10.0, result.Points.Last().OutflowCfs, 1);
        }

        [Fact]
        public void InvalidX_Throws()
            => Assert.Throws<ArgumentOutOfRangeException>(() => MuskingumRouting.ComputeCoefficients(1.0, 0.6, 0.1));
    }
}