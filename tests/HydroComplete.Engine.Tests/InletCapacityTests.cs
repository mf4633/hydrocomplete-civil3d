using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class InletCapacityTests
    {
        [Fact]
        public void GrateCapacity_LongerGrate_CarriesMore()
        {
            double a = InletCapacity.GrateCapacityCfs(2.0, 0.15, 0.005);
            double b = InletCapacity.GrateCapacityCfs(5.0, 0.15, 0.005);
            Assert.True(b > a);
        }

        [Fact]
        public void GrateCapacity_NonPositiveInputs_ReturnZero()
        {
            Assert.Equal(0.0, InletCapacity.GrateCapacityCfs(0.0, 0.15, 0.005));
            Assert.Equal(0.0, InletCapacity.GrateCapacityCfs(5.0, 0.0, 0.005));
            Assert.Equal(0.0, InletCapacity.GrateCapacityCfs(5.0, 0.15, 0.0));
        }

        [Fact]
        public void CheckInlet_DesignBelowCapacity_Passes()
        {
            double cap = InletCapacity.GrateCapacityCfs(5.0, 0.15, 0.005);
            var check = InletCapacity.CheckInlet(cap * 0.5, 5.0, 0.15, 0.005);
            Assert.True(check.Ok);
            Assert.Equal(cap, check.CapacityCfs, 4);
            Assert.Contains(check.Steps, s => s.Label == "Q_cap");
            Assert.Contains(check.Steps, s => s.Label == "ok" && s.Value == 1.0);
        }

        [Fact]
        public void CheckInlet_DesignAboveCapacity_Fails()
        {
            double cap = InletCapacity.GrateCapacityCfs(2.0, 0.15, 0.005);
            var check = InletCapacity.CheckInlet(cap * 2.0, 2.0, 0.15, 0.005);
            Assert.False(check.Ok);
            Assert.Contains(check.Steps, s => s.Label == "ok" && s.Value == 0.0);
        }
    }
}