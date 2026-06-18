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
            => Assert.Equal(1.0, MuskingumRouting.ComputeCoefficients(1.0, 0.2, 0.5).Sum, 4);

        [Fact]
        public void Route_ReducesPeak_ForTypicalReach()
        {
            var result = MuskingumRouting.Route(new[] { 0.0, 10.0, 30.0, 50.0, 30.0, 10.0, 0.0 }, 1.0, 0.2, 0.5);
            Assert.True(result.PeakOutflowCfs < result.PeakInflowCfs);
        }

        [Fact]
        public void Route_PreservesVolume_WithinTolerance()
        {
            var inflow = new[] { 0.0, 5.0, 15.0, 25.0, 20.0, 10.0, 5.0, 0.0 };
            // dt >= 2*K*X (0.4 hr) keeps C0 >= 0 and preserves mass balance.
            var result = MuskingumRouting.Route(inflow, 1.0, 0.2, 0.5);
            double relError = Math.Abs(result.InflowVolumeAcreFt - result.OutflowVolumeAcreFt) / result.InflowVolumeAcreFt;
            Assert.True(relError < 0.02, $"Volume error {relError:P2}");
        }

        [Fact]
        public void Route_ConstantInflow_ApproachesSteadyState()
        {
            var inflow = Enumerable.Repeat(10.0, 40).ToArray();
            var result = MuskingumRouting.Route(inflow, 1.0, 0.2, 0.5);
            double outflowAtEndOfInflow = result.Points[inflow.Length - 1].OutflowCfs;
            Assert.Equal(10.0, outflowAtEndOfInflow, 1);
        }

        [Fact]
        public void InvalidX_Throws()
            => Assert.Throws<ArgumentOutOfRangeException>(() => MuskingumRouting.ComputeCoefficients(1.0, 0.6, 0.1));

        [Fact]
        public void UnstableTimestep_Throws()
            => Assert.Throws<ArgumentOutOfRangeException>(() => MuskingumRouting.ComputeCoefficients(1.0, 0.2, 0.1));
    }
}