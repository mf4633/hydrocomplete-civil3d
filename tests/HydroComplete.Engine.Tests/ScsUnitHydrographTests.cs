using System;
using System.Linq;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class ScsUnitHydrographTests
    {
        [Fact]
        public void TimeToPeak_FromTc_UsesTr55Formula()
        {
            // Tc = 30 min -> D = 0.133*0.5 hr = 0.0665 hr, Tl = 0.3 hr, Tp = 0.33325 hr
            double tpHr = ScsUnitHydrograph.TimeToPeakHours(30.0);
            Assert.Equal(0.333, tpHr, 2);
            Assert.Equal(20.0, tpHr * 60.0, 1);
        }

        [Fact]
        public void PeakDischarge_Uses484PeakFactor()
        {
            // 100 ac = 0.15625 sq mi, Tp = 0.33325 hr -> qp ~ 227 cfs
            double tpHr = ScsUnitHydrograph.TimeToPeakHours(30.0);
            double qp = ScsUnitHydrograph.PeakDischargeCfs(100.0, tpHr);
            Assert.Equal(227.0, qp, 0);
        }

        [Fact]
        public void DimensionlessFlow_AtPeak_IsUnity()
        {
            Assert.Equal(1.0, ScsUnitHydrograph.DimensionlessFlow(1.0), 6);
        }

        [Fact]
        public void DimensionlessFlow_AtHalfPeak_MatchesTable()
        {
            Assert.Equal(0.72, ScsUnitHydrograph.DimensionlessFlow(0.5), 2);
        }

        [Fact]
        public void DimensionlessFlow_InterpolatesBetweenTablePoints()
        {
            // Midway between (0.4, 0.53) and (0.5, 0.72) -> 0.625
            Assert.Equal(0.625, ScsUnitHydrograph.DimensionlessFlow(0.45), 2);
        }

        [Fact]
        public void Generate_PeakOrdinateMatchesTp()
        {
            var uh = ScsUnitHydrograph.Generate(50.0, 20.0);
            var peak = uh.Ordinates.OrderByDescending(o => o.FlowCfs).First();

            Assert.Equal(uh.PeakFlowCfs, peak.FlowCfs, 2);
            Assert.Equal(uh.TimeToPeakMinutes, peak.TimeMinutes, 1);
            Assert.Equal(1.0, peak.QRatio, 2);
            Assert.NotEmpty(uh.Steps);
        }

        [Fact]
        public void Generate_UsesCustomDurationWhenSupplied()
        {
            var defaultUh = ScsUnitHydrograph.Generate(10.0, 60.0);
            var customUh = ScsUnitHydrograph.Generate(10.0, 60.0, durationMinutes: 30.0);

            Assert.NotEqual(defaultUh.DurationMinutes, customUh.DurationMinutes);
            Assert.NotEqual(defaultUh.TimeToPeakMinutes, customUh.TimeToPeakMinutes);
            Assert.True(customUh.TimeToPeakMinutes > defaultUh.TimeToPeakMinutes);
        }

        [Fact]
        public void Generate_ZeroTc_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ScsUnitHydrograph.Generate(10.0, 0.0));
        }

        [Fact]
        public void PeakDischarge_ZeroArea_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                ScsUnitHydrograph.PeakDischargeCfs(0.0, 0.5));
        }
    }
}