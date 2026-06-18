using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class Atlas14CoverageTests
    {
        [Theory]
        [InlineData("boise-id")]
        [InlineData("coeur-dalene-id")]
        [InlineData("idaho-falls-id")]
        [InlineData("billings-mt")]
        [InlineData("helena-mt")]
        [InlineData("missoula-mt")]
        [InlineData("great-falls-mt")]
        [InlineData("bozeman-mt")]
        public void Volume12_Preset_Exists(string key)
        {
            Assert.NotNull(Atlas14Presets.Find(key));
        }

        // Each embedded preset must define all four standard return periods and have
        // intensity strictly increasing with return period (a typo in the 20-arg
        // constructor line would usually break monotonicity).
        [Fact]
        public void AllPresets_IntensityIncreasesWithReturnPeriod()
        {
            foreach (Atlas14Presets.Preset p in Atlas14Presets.List())
            {
                double i2 = p.ToCurve(2).Intensity(60.0).IntensityInHr;
                double i10 = p.ToCurve(10).Intensity(60.0).IntensityInHr;
                double i25 = p.ToCurve(25).Intensity(60.0).IntensityInHr;
                double i100 = p.ToCurve(100).Intensity(60.0).IntensityInHr;

                Assert.True(i2 < i10, $"{p.Key}: 2-yr !< 10-yr");
                Assert.True(i10 < i25, $"{p.Key}: 10-yr !< 25-yr");
                Assert.True(i25 < i100, $"{p.Key}: 25-yr !< 100-yr");
            }
        }

        [Fact]
        public void Nearest_InteriorNorthwest_ResolvesToVolume12City()
        {
            // Billings, MT coordinates -> Billings preset.
            Assert.Equal("billings-mt", Atlas14Presets.Nearest(45.78, -108.50).Key);
            // Spokane, WA (Atlas 2, no preset) -> nearest is Coeur d'Alene, ID (same Interior NW climate).
            Assert.Equal("coeur-dalene-id", Atlas14Presets.Nearest(47.66, -117.43).Key);
        }
    }
}
