using System;
using System.Linq;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class HydrographConvolutionTests
    {
        [Fact]
        public void InterpolateUnitHydrograph_Midpoint_ReturnsAverage()
        {
            var uh = new[]
            {
                new HydrographConvolution.UnitHydrographInput { TimeHours = 0.0, FlowCfsPerIn = 0.0 },
                new HydrographConvolution.UnitHydrographInput { TimeHours = 1.0, FlowCfsPerIn = 100.0 },
            };
            Assert.Equal(50.0, HydrographConvolution.InterpolateUnitHydrograph(uh, 0.5), 6);
        }

        [Fact]
        public void Convolve_SinglePulse_ScalesWithExcessDepth()
        {
            var uh = new[]
            {
                new HydrographConvolution.UnitHydrographInput { TimeHours = 0.0, FlowCfsPerIn = 0.0 },
                new HydrographConvolution.UnitHydrographInput { TimeHours = 0.5, FlowCfsPerIn = 200.0 },
                new HydrographConvolution.UnitHydrographInput { TimeHours = 1.0, FlowCfsPerIn = 0.0 },
            };
            var oneInch = HydrographConvolution.Convolve(
                new[] { 1.0 }, 0.0, 0.5, uh, 100.0);
            var twoInch = HydrographConvolution.Convolve(
                new[] { 2.0 }, 0.0, 0.5, uh, 100.0);
            Assert.Equal(200.0, oneInch.PeakFlowCfs, 0);
            Assert.Equal(400.0, twoInch.PeakFlowCfs, 0);
        }

        [Fact]
        public void Convolve_PeakOccursAfterRainfall()
        {
            var uh = ScsUnitHydrograph.Generate(50.0, 30.0)
                .Ordinates.Select(o => new HydrographConvolution.UnitHydrographInput
                {
                    TimeHours = o.TimeMinutes / 60.0,
                    FlowCfsPerIn = o.FlowCfs,
                }).ToList();

            var result = HydrographConvolution.Convolve(
                new[] { 0.0, 0.0, 1.0, 0.0 }, 0.0, 0.25, uh, 50.0);
            Assert.True(result.TimeToPeakHours >= 0.5);
            Assert.True(result.PeakFlowCfs > 0);
        }

        [Fact]
        public void GenerateTr20Hydrograph_ProducesPositivePeak()
        {
            var result = HydrographConvolution.GenerateTr20Hydrograph(
                areaAcres: 100.0,
                curveNumber: 75.0,
                tcMinutes: 30.0,
                totalRainfallIn: 5.0,
                timestepHours: 0.5);
            Assert.True(result.PeakFlowCfs > 0);
            Assert.True(result.TotalExcessRainfallIn > 0);
            Assert.True(result.TotalExcessRainfallIn < 5.0);
            Assert.NotEmpty(result.Steps);
        }

        [Fact]
        public void GenerateTr20Hydrograph_SnyderMethod_ProducesPeak()
        {
            var result = HydrographConvolution.GenerateTr20Hydrograph(
                50.0, 70.0, 25.0, 4.0, 0.5,
                HydrographConvolution.UnitHydrographMethod.Snyder);
            Assert.True(result.PeakFlowCfs > 0);
        }

        [Fact]
        public void BuildUnitHydrograph_AllMethods_ReturnOrdinates()
        {
            foreach (HydrographConvolution.UnitHydrographMethod method in Enum.GetValues(
                typeof(HydrographConvolution.UnitHydrographMethod)))
            {
                var uh = HydrographConvolution.BuildUnitHydrograph(method, 25.0, 20.0, 0.25);
                Assert.NotEmpty(uh);
                Assert.True(uh.Max(x => x.FlowCfsPerIn) > 0);
            }
        }
    }
}