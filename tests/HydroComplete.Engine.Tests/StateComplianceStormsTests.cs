using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class StateComplianceStormsTests
    {
        [Fact]
        public void GetPeakStormSuite_NC_HasFourStorms()
        {
            var nc = StateCompliance.Get("NC");
            var storms = StateCompliance.GetPeakStormSuite(nc);

            Assert.Equal(4, storms.Count);
            Assert.Equal(3.0, storms["2-year"], 2);
            Assert.Equal(4.5, storms["10-year"], 2);
            Assert.Equal(7.2, storms["100-year"], 2);
        }

        [Fact]
        public void GetPeakStormSuite_UnknownState_FallsBackToDefault()
        {
            var ak = StateCompliance.Get("AK");
            var storms = StateCompliance.GetPeakStormSuite(ak);

            Assert.Equal(4, storms.Count);
            Assert.Equal(7.0, storms["100-year"], 2);
        }
    }
}