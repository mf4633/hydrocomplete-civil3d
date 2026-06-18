using System;
using System.Linq;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class ScsRunoffTests
    {
        [Fact]
        public void MaxRetention_Cn70_MatchesTr55() => Assert.Equal(4.286, ScsRunoff.MaxRetentionFromCn(70), 2);

        [Fact]
        public void InitialAbstraction_IsTwentyPercentOfS()
        {
            double s = ScsRunoff.MaxRetentionFromCn(75);
            Assert.Equal(0.2 * s, ScsRunoff.InitialAbstractionFromCn(75), 4);
        }

        [Fact]
        public void CumulativeRunoff_BelowIa_IsZero()
        {
            double ia = ScsRunoff.InitialAbstractionFromCn(75);
            Assert.Equal(0.0, ScsRunoff.CumulativeRunoffDepth(ia * 0.5, 75));
        }

        [Fact]
        public void CumulativeRunoff_AtFiveInchStorm_MatchesScsEquation()
        {
            // CN=75 -> S=3.333, Ia=0.667; P=5 -> Q = (4.333)^2/(4.333+3.333)=2.45 in
            Assert.Equal(2.45, ScsRunoff.CumulativeRunoffDepth(5.0, 75), 2);
        }

        [Fact]
        public void IncrementalRunoff_FirstStepsTrackAbstraction()
        {
            var depths = StormHyetograph.TypeIIUniform(5.0, 24.0, 0.5).Increments.Select(x => x.DepthIn).ToList();
            var result = ScsRunoff.ComputeIncremental(depths, 75);
            Assert.True(result.Increments.Take(5).All(x => x.IncrementalRunoffIn == 0));
            Assert.True(result.Increments.Any(x => x.IncrementalRunoffIn > 0));
        }

        [Fact]
        public void IncrementalRunoff_SumEqualsTotal()
        {
            var result = ScsRunoff.ComputeIncremental(new[] { 1.0, 2.0, 2.0 }, 80);
            Assert.Equal(result.TotalRunoffIn, result.Increments.Sum(x => x.IncrementalRunoffIn), 6);
            Assert.Equal(ScsRunoff.CumulativeRunoffDepth(5.0, 80), result.TotalRunoffIn, 6);
        }

        [Fact]
        public void InvalidCurveNumber_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ScsRunoff.MaxRetentionFromCn(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => ScsRunoff.ComputeIncremental(new[] { 1.0 }, 101));
        }
    }
}