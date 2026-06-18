using System;
using System.Linq;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class ClarkUnitHydrographTests
    {
        [Fact]
        public void StorageCoefficient_IsFortyPercentOfTc()
            => Assert.Equal(12.0, ClarkUnitHydrograph.StorageCoefficientMinutes(30.0), 6);

        [Fact]
        public void TimeAreaHistogram_SumsToUnity()
        {
            var hist = ClarkUnitHydrograph.TimeAreaHistogram(4);
            Assert.Equal(1.0, hist.Sum(), 6);
            Assert.Equal(0.0, hist[0], 6);
            Assert.Equal(0.5, hist[2], 6);
        }

        [Fact]
        public void Generate_PeakIsPositive()
        {
            var uh = ClarkUnitHydrograph.Generate(50.0, 30.0);
            Assert.True(uh.PeakFlowCfs > 0);
            Assert.True(uh.TimeToPeakMinutes > 0);
            Assert.NotEmpty(uh.Steps);
        }

        [Fact]
        public void Generate_RoutedPeakLessThanTranslatedSum()
        {
            var uh = ClarkUnitHydrograph.Generate(10.0, 20.0, timestepMinutes: 5.0);
            double translatedPeak = uh.Ordinates.Max(o => o.TranslatedFlowCfs);
            Assert.True(uh.PeakFlowCfs <= translatedPeak + 1e-6);
        }

        [Fact]
        public void Generate_ZeroTc_Throws()
            => Assert.Throws<ArgumentOutOfRangeException>(() => ClarkUnitHydrograph.Generate(10.0, 0.0));
    }
}