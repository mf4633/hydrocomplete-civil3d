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

        [Fact]
        public void SagCapacity_DeeperFlow_CarriesMore()
        {
            double shallow = InletCapacity.SagCapacityCfs(5.0, 0.10);
            double deep = InletCapacity.SagCapacityCfs(5.0, 0.20);
            Assert.True(deep > shallow);
        }

        [Fact]
        public void SagCapacity_NonPositiveInputs_ReturnZero()
        {
            Assert.Equal(0.0, InletCapacity.SagCapacityCfs(0.0, 0.15));
            Assert.Equal(0.0, InletCapacity.SagCapacityCfs(5.0, 0.0));
        }

        [Fact]
        public void CurbOpeningCapacity_TallerOpening_CarriesMore()
        {
            double low = InletCapacity.CurbOpeningCapacityCfs(0.25, 5.0, 0.15, 0.005);
            double high = InletCapacity.CurbOpeningCapacityCfs(0.50, 5.0, 0.15, 0.005);
            Assert.True(high > low);
            Assert.Equal(low * 2.0, high, 4);
        }

        [Fact]
        public void CheckInlet_SagType_UsesWeirFormulaWithoutSlope()
        {
            double expected = InletCapacity.SagCapacityCfs(5.0, 0.15);
            var check = InletCapacity.CheckInlet(
                expected * 0.5, InletCapacity.InletType.Sag, 5.0, 0.15, 0.005);
            Assert.True(check.Ok);
            Assert.Equal(InletCapacity.InletType.Sag, check.InletType);
            Assert.Equal(expected, check.CapacityCfs, 4);
            Assert.Contains(check.Steps, s => s.Label == "Q_cap" && s.Formula == "Cw*L*d^1.5");
        }

        [Fact]
        public void CheckInlet_CurbOpening_DesignAboveCapacity_Fails()
        {
            double cap = InletCapacity.CurbOpeningCapacityCfs(0.5, 5.0, 0.15, 0.005);
            var check = InletCapacity.CheckInlet(
                cap * 2.0, InletCapacity.InletType.CurbOpening, 5.0, 0.15, 0.005, 0.5);
            Assert.False(check.Ok);
            Assert.Equal(InletCapacity.InletType.CurbOpening, check.InletType);
            Assert.Contains(check.Steps, s => s.Label == "a" && s.Value == 0.5);
        }
    }
}