using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class PipeSizingTests
    {
        private const double N = 0.013;
        private const double Slope = 0.01;

        [Fact]
        public void Catalog_IsAscending_FromTwelveInches()
        {
            double[] catalog = DesignCriteria.StandardCatalogDiametersFt();
            Assert.True(catalog.Length >= 10);
            for (int i = 1; i < catalog.Length; i++)
                Assert.True(catalog[i - 1] < catalog[i]);
            Assert.Equal(DesignCriteria.InchesToFt(12), catalog[0], 6);
        }

        [Fact]
        public void SizePipe_LightFlow_OnLargePipe_IsAdequate()
        {
            var result = PipeSizing.SizePipe(5.0, Slope, N, currentDiameterFt: 2.0);
            Assert.Equal(SizeOutcome.Adequate, result.Outcome);
            Assert.Equal(2.0, result.RecommendedDiameterFt, 4);
            Assert.False(result.Surcharged);
            Assert.InRange(result.VelocityFps, 2.0, 10.0);
            Assert.True(result.PctFull < 0.85);
        }

        [Fact]
        public void SizePipe_HeavyFlow_OnSmallPipe_Upsizes()
        {
            double current = DesignCriteria.InchesToFt(18);
            var result = PipeSizing.SizePipe(20.0, Slope, N, current);
            Assert.Equal(SizeOutcome.Sized, result.Outcome);
            Assert.True(result.RecommendedDiameterFt > current);
            Assert.False(result.Surcharged);
            Assert.InRange(result.VelocityFps, 2.0, 10.0);
            Assert.True(result.PctFull <= 0.85 + 1e-6);
        }

        [Fact]
        public void SizePipeForFlow_ExtremeFlow_ReturnsNoSolution()
        {
            var result = PipeSizing.SizePipeForFlow(500.0, Slope, N);
            Assert.Equal(SizeOutcome.NoSolution, result.Outcome);
            Assert.Equal(DesignCriteria.InchesToFt(72), result.RecommendedDiameterFt, 4);
            Assert.True(result.Surcharged);
        }
    }
}