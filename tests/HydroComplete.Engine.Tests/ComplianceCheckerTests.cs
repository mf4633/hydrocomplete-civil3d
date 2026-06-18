using System.Collections.Generic;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class ComplianceCheckerTests
    {
        private static ComplianceAnalysisResults PassingWaterQuality(double tss = 90, double tn = 35, double tp = 35)
        {
            return new ComplianceAnalysisResults
            {
                WaterQuality = new WaterQualityComplianceInput
                {
                    BmpCount = 1,
                    HasInfiltrationBmp = true,
                    WqvRequiredCf = 5000,
                    WqvProvidedCf = 5500,
                    DrawdownHours = 72,
                    BmpEfficiency = new List<BmpEfficiencyInput>
                    {
                        new BmpEfficiencyInput
                        {
                            Type = "bioretention",
                            TssRemovalPercent = tss,
                            TnRemovalPercent = tn,
                            TpRemovalPercent = tp,
                        },
                    },
                },
                Hydrology = new HydrologyComplianceInput
                {
                    HasDetention = true,
                    PrePeakCfs = 10.0,
                    PostPeakCfs = 9.5,
                },
                Sediment = new SedimentComplianceInput
                {
                    TotalSoilLossTonsPerAcYr = 3.0,
                    SedimentControlCount = 1,
                },
            };
        }

        [Fact]
        public void Nc_Requires85PercentTss_FailsAt80()
        {
            var results = PassingWaterQuality(tss: 80);
            ComplianceReport report = ComplianceChecker.CheckCompliance(results, "NC", "residential");
            ComplianceCriterion? tss = FindCriterion(report, "TSS Removal");
            Assert.NotNull(tss);
            Assert.Equal(ComplianceStatus.Fail, tss!.Status);
            Assert.False(report.OverallPass);
        }

        [Fact]
        public void Nc_PassesAt85PercentTss()
        {
            var results = PassingWaterQuality(tss: 85);
            ComplianceReport report = ComplianceChecker.CheckCompliance(results, "NC", "residential");
            ComplianceCriterion? tss = FindCriterion(report, "TSS Removal");
            Assert.NotNull(tss);
            Assert.Equal(ComplianceStatus.Pass, tss!.Status);
        }

        [Fact]
        public void Sc_Requires80PercentTss()
        {
            var results = PassingWaterQuality(tss: 80);
            ComplianceReport report = ComplianceChecker.CheckCompliance(results, "SC", "commercial");
            ComplianceCriterion? tss = FindCriterion(report, "TSS Removal");
            Assert.NotNull(tss);
            Assert.Equal(ComplianceStatus.Pass, tss!.Status);
        }

        [Fact]
        public void Va_Requires50PercentTp()
        {
            var results = PassingWaterQuality(tp: 45);
            ComplianceReport report = ComplianceChecker.CheckCompliance(results, "VA", "residential");
            ComplianceCriterion? tp = FindCriterion(report, "TP Removal");
            Assert.NotNull(tp);
            Assert.Equal(ComplianceStatus.Fail, tp!.Status);
        }

        [Fact]
        public void Fl_Requires50PercentTn()
        {
            var results = PassingWaterQuality(tn: 55);
            ComplianceReport report = ComplianceChecker.CheckCompliance(results, "FL", "residential");
            ComplianceCriterion? tn = FindCriterion(report, "TN Removal");
            Assert.NotNull(tn);
            Assert.Equal(ComplianceStatus.Pass, tn!.Status);
        }

        [Fact]
        public void Tx_Uses150InchWqvStorm()
        {
            StateComplianceConfig tx = StateCompliance.Get("TX");
            Assert.Equal(1.5, tx.WqVolumeFactorInches, 2);
            Assert.Equal(1.5, tx.DesignStormInches, 2);
        }

        [Fact]
        public void Ca_Uses075InchDesignStorm()
        {
            StateComplianceConfig ca = StateCompliance.Get("CA");
            Assert.Equal(0.75, ca.DesignStormInches, 2);
            Assert.Equal(0.75, ca.WqVolumeFactorInches, 2);
        }

        [Fact]
        public void Ny_RoadwayTssRequirementIs40Percent()
        {
            StateComplianceConfig ny = StateCompliance.Get("NY");
            double required = StateCompliance.RequiredTssPercent(ny, "roadway");
            Assert.Equal(40.0, required, 1);

            var results = PassingWaterQuality(tss: 45);
            ComplianceReport report = ComplianceChecker.CheckCompliance(results, "NY", "roadway");
            ComplianceCriterion? tss = FindCriterion(report, "TSS Removal");
            Assert.NotNull(tss);
            Assert.Equal(ComplianceStatus.Pass, tss!.Status);
        }

        [Fact]
        public void Default_StateUsedForUnknownCode()
        {
            StateComplianceConfig cfg = StateCompliance.Get("ZZ");
            Assert.Equal("DEFAULT", cfg.Code);
            Assert.Equal(80.0, cfg.TssRemovalPercent, 1);
        }

        [Fact]
        public void PeakFlow_FailsWhenPostExceedsPre()
        {
            var results = PassingWaterQuality();
            results.Hydrology!.PostPeakCfs = 12.0;
            results.Hydrology.PrePeakCfs = 10.0;

            ComplianceReport report = ComplianceChecker.CheckCompliance(results, "NC", "residential");
            ComplianceCriterion? peak = FindCriterion(report, "Peak Flow Attenuation");
            Assert.NotNull(peak);
            Assert.Equal(ComplianceStatus.Fail, peak!.Status);
        }

        [Fact]
        public void Drawdown_FailsOutsideNcWindow()
        {
            var results = PassingWaterQuality();
            results.WaterQuality!.DrawdownHours = 150;

            ComplianceReport report = ComplianceChecker.CheckCompliance(results, "NC", "residential");
            ComplianceCriterion? drawdown = FindCriterion(report, "BMP Drawdown Time");
            Assert.NotNull(drawdown);
            Assert.Equal(ComplianceStatus.Fail, drawdown!.Status);
        }

        [Fact]
        public void Erosion_FailsAboveTolerableT()
        {
            var results = PassingWaterQuality();
            results.Sediment!.TotalSoilLossTonsPerAcYr = 8.0;

            ComplianceReport report = ComplianceChecker.CheckCompliance(results, "NC", "residential");
            ComplianceCriterion? erosion = FindCriterion(report, "Soil Loss (Tolerable T)");
            Assert.NotNull(erosion);
            Assert.Equal(ComplianceStatus.Fail, erosion!.Status);
        }

        [Fact]
        public void CombinedRemoval_UsesTreatmentTrainFormula()
        {
            double? combined = ComplianceChecker.CombinedRemovalPercent(new[] { 50.0, 50.0 });
            Assert.NotNull(combined);
            Assert.Equal(75.0, combined!.Value, 1);
        }

        private static ComplianceCriterion? FindCriterion(ComplianceReport report, string name)
        {
            foreach (ComplianceCriterion c in report.Criteria)
            {
                if (c.Name == name) return c;
            }

            return null;
        }
    }
}