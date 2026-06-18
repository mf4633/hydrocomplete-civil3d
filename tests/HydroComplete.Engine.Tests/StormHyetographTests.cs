using System.Linq;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class StormHyetographTests
    {
        [Fact]
        public void TypeII_FractionsSumToOne()
            => Assert.Equal(1.0, StormHyetograph.TypeII(1.0).Increments.Sum(x => x.Fraction), 4);

        [Fact]
        public void TypeII_DepthsSumToTotalDepth()
            => Assert.Equal(5.0, StormHyetograph.TypeII(5.0).Increments.Sum(x => x.DepthIn), 3);

        [Fact]
        public void TypeII_PeakIntensityNearMidStorm()
        {
            var peak = StormHyetograph.TypeII(1.0).Increments.OrderByDescending(x => x.IntensityPerHour).First();
            Assert.InRange(peak.StartTimeHours, 11.0, 12.5);
        }

        [Fact]
        public void TypeIIUniform_FractionsSumToOne()
        {
            var storm = StormHyetograph.TypeIIUniform(3.0, 24.0, 0.1);
            Assert.Equal(1.0, storm.Increments.Sum(x => x.Fraction), 3);
            Assert.Equal(3.0, storm.Increments.Sum(x => x.DepthIn), 2);
        }
    }
}