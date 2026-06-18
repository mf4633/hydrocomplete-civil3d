using System;
using System.Linq;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class SnyderUnitHydrographTests
    {
        [Fact]
        public void LagHours_StandardBasin_MatchesSnyderFormula()
        {
            // Ct=1.8, L=1.0, Lc=0.5 -> tp = 1.8*(0.5)^0.3 = 1.46 hr
            Assert.Equal(1.46, SnyderUnitHydrograph.LagHours(1.0, 0.5), 2);
        }

        [Fact]
        public void PeakDischarge_OneSqMi_Matches640CpOverTp()
        {
            // 640 ac = 1 sq mi, tp=2 hr, Cp=0.6 -> qp = 192 cfs
            Assert.Equal(192.0, SnyderUnitHydrograph.PeakDischargeCfs(640.0, 2.0), 0);
        }

        [Fact]
        public void Widths_ScaleWithLag()
        {
            Assert.Equal(4.28, SnyderUnitHydrograph.Width50Hours(2.0), 2);
            Assert.Equal(2.74, SnyderUnitHydrograph.Width75Hours(2.0), 2);
        }

        [Fact]
        public void Generate_PeakAtLagTime()
        {
            var uh = SnyderUnitHydrograph.Generate(
                100.0, channelLengthMi: 1.2, centroidDistanceMi: 0.6);
            var peak = uh.Ordinates.OrderByDescending(o => o.FlowCfs).First();
            Assert.Equal(uh.PeakFlowCfs, peak.FlowCfs, 2);
            Assert.Equal(uh.LagHours, peak.TimeHours, 2);
            Assert.NotEmpty(uh.Steps);
        }

        [Fact]
        public void Generate_ZeroArea_Throws()
            => Assert.Throws<ArgumentOutOfRangeException>(() => SnyderUnitHydrograph.Generate(0.0));
    }
}