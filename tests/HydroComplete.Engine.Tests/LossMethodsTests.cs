using System;
using System.Linq;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class LossMethodsTests
    {
        [Fact]
        public void HortonCumulative_At24Hours_SoilB_MatchesHandCalc()
        {
            // f0=3, fc=0.3, k=3.5 -> F(24) = 7.2 + 0.771*(1-e^-84) ≈ 7.97 in
            var p = LossMethods.HortonForSoilGroup("B");
            Assert.Equal(7.97, LossMethods.HortonCumulativeInfiltrationInches(24.0, p), 1);
        }

        [Fact]
        public void GreenAmptExcess_FiveInchStorm_SoilB_MatchesJsFormula()
        {
            var p = LossMethods.GreenAmptForSoilGroup("B");
            // f = 1.32 + 8.74*0.453*1.32/5 = 2.37 in/hr equivalent loss depth
            double excess = LossMethods.GreenAmptExcessDepthInches(5.0, p);
            Assert.Equal(2.63, excess, 1);
        }

        [Fact]
        public void InitialConstant_FiveInch24Hr_MatchesJs()
        {
            // CN=75 -> Ia=0.667, after Ia=4.333, fc=0.15*24=3.6 -> excess=0.733
            Assert.Equal(0.73, LossMethods.InitialConstantExcessDepthInches(5.0, 75, 0.15, 24), 1);
        }

        [Fact]
        public void ConstantRate_FiveInch24Hr_MatchesJs()
        {
            // fc=0.2*24=4.8 -> excess=0.2
            Assert.Equal(0.2, LossMethods.ConstantRateExcessDepthInches(5.0, 0.2, 24), 1);
        }

        [Fact]
        public void HortonIncremental_SumDoesNotExceedRainfall()
        {
            var result = LossMethods.ComputeIncremental(
                new[] { 0.5, 1.0, 1.5, 2.0 }, 1.0,
                new LossMethods.LossParameters { Method = LossMethods.LossMethodType.Horton });
            Assert.True(result.TotalLossIn + result.TotalExcessIn <= result.TotalRainfallIn + 1e-6);
            Assert.Equal(result.TotalExcessIn, result.Increments.Sum(x => x.ExcessRainfallIn), 6);
        }

        [Fact]
        public void GreenAmptIncremental_LossNeverExceedsRainfall()
        {
            var result = LossMethods.ComputeIncremental(
                new[] { 1.0, 1.0, 2.0 }, 1.0,
                new LossMethods.LossParameters { Method = LossMethods.LossMethodType.GreenAmpt });
            Assert.All(result.Increments, inc =>
            {
                Assert.True(inc.LossIn <= inc.RainfallIn + 1e-9);
                Assert.True(inc.ExcessRainfallIn >= 0.0);
            });
        }

        [Fact]
        public void CurveNumberIncremental_MatchesScsRunoff()
        {
            var depths = new[] { 1.0, 2.0, 2.0 };
            var loss = LossMethods.ComputeIncremental(depths, 1.0,
                new LossMethods.LossParameters { Method = LossMethods.LossMethodType.CurveNumber, CurveNumber = 80 });
            var scs = ScsRunoff.ComputeIncremental(depths, 80);
            Assert.Equal(scs.TotalRunoffIn, loss.TotalExcessIn, 6);
        }
    }
}