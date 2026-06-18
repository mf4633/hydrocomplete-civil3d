using System.Collections.Generic;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class RusleTests
    {
        [Fact]
        public void LsFactor_FivePercentSlope_MatchesHandCalc()
        {
            double ls = RusleAnalysis.LsFactor(100.0, 5.0);
            // m=0.5 at exactly 5% slope; L=(100/72.6)^0.5, S=10.8*sin(atan(0.05))+0.03
            Assert.Equal(0.668, ls, 2);
        }

        [Fact]
        public void LsFactor_SteepSlope_UsesAlternateSFormula()
        {
            double lsSteep = RusleAnalysis.LsFactor(200.0, 12.0);
            double lsModerate = RusleAnalysis.LsFactor(200.0, 8.0);
            Assert.True(lsSteep > lsModerate);
        }

        [Fact]
        public void SoilLoss_CharlotteConstructionSite_MatchesHandCalc()
        {
            var site = new RusleAnalysis.SiteInput
            {
                Region = "charlotte-nc",
                SoilType = "loam",
                Cover = "construction-site",
                Practice = "none",
                SlopeLengthFt = 100.0,
                SlopePercent = 5.0,
                AreaAcres = 2.0,
            };

            var r = RusleAnalysis.SoilLoss(site);
            Assert.Equal(180.0, r.R, 0);
            Assert.Equal(0.38, r.K, 2);
            Assert.Equal(0.90, r.C, 2);
            Assert.Equal(41.1, r.SoilLossPerAcreTonsYr, 1);
            Assert.Equal(82.3, r.TotalSoilLossTonsYr, 1);
            Assert.Contains(r.Steps, s => s.Label == "A");
        }

        [Fact]
        public void SoilLoss_SsurgOKFactor_OverridesSoilLookup()
        {
            var site = new RusleAnalysis.SiteInput { KFactor = 0.25, AreaAcres = 1.0 };
            var r = RusleAnalysis.SoilLoss(site);
            Assert.Equal(0.25, r.K, 3);
            Assert.Equal("USDA SSURGO", r.KSource);
        }

        [Fact]
        public void LsFactor_NegativeSlope_Throws()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => RusleAnalysis.LsFactor(100.0, -1.0));
        }
    }

    public class MusleTests
    {
        [Fact]
        public void SingleStorm_StandardInputs_MatchesHandCalc()
        {
            var r = Musle.SingleStorm(1.0, 10.0, k: 0.38, ls: 1.0, c: 0.90, p: 1.00);
            Assert.Equal(10.0, r.Qqp, 2);
            Assert.Equal(118.0, r.SedimentYieldTons, 0);
            Assert.NotEmpty(r.Steps);
        }

        [Fact]
        public void SingleStorm_ZeroRunoff_YieldsZero()
        {
            var r = Musle.SingleStorm(0.0, 10.0, 0.38, 1.0, 0.9, 1.0);
            Assert.Equal(0.0, r.SedimentYieldTons, 6);
        }

        [Fact]
        public void StormSequence_TwoStorms_ProducesPositiveAnnualExpected()
        {
            var watershed = new Musle.WatershedInput { AreaAcres = 5.0, CurveNumber = 80 };
            var storms = new Dictionary<string, double>
            {
                ["2-year"] = 2.5,
                ["10-year"] = 4.0,
            };

            var r = Musle.StormSequence(watershed, storms);
            Assert.Equal(2, r.Storms.Count);
            Assert.True(r.Storms[1].SedimentYieldTons >= r.Storms[0].SedimentYieldTons);
            Assert.True(r.AnnualExpectedTons > 0);
        }
    }

    public class SedimentSettlingTests
    {
        [Fact]
        public void SettlingVelocity_ClayUsesStokesRegime()
        {
            var sv = SedimentSettling.SettlingVelocity(0.0008, waterTempC: 20.0);
            Assert.Equal("Stokes (laminar)", sv.Regime);
            Assert.True(sv.VsFps > 0);
            Assert.True(sv.ReynoldsNumber < 0.5);
        }

        [Fact]
        public void SettlingVelocity_MediumSandUsesRubeyRegime()
        {
            var sv = SedimentSettling.SettlingVelocity(0.354, waterTempC: 20.0);
            Assert.Equal("Rubey (transitional)", sv.Regime);
            Assert.True(sv.VsFps > 0.01);
        }

        [Fact]
        public void CampEfficiency_ConstructionPsd_WeightedByBins()
        {
            var psd = SedimentSettling.BuildSevenBinPsd(new SedimentSettling.SitePsdInput
            {
                PctClay = 10,
                PctSilt = 30,
                PctSand = 60,
            });

            var camp = SedimentSettling.CampEfficiency(1.0, 435.0, psd);
            Assert.InRange(camp.OverallEfficiencyPct, 50.0, 95.0);
            Assert.Equal(1.0 / 435.0, camp.OverflowVelocityFps, 5);
            Assert.Contains(camp.Steps, s => s.Label == "eta_total");
        }

        [Fact]
        public void WeightedTrapEfficiency_ConstructionSite_OnSedimentTrap()
        {
            var psd = new SedimentSettling.ThreeBinPsd { Clay = 0.10, Silt = 0.30, Sand = 0.60 };
            var eff = SedimentSettling.WeightedTrapEfficiency("sediment-trap", psd);
            Assert.Equal(70.0, eff.OverallEfficiencyPct, 1);
        }
    }

    public class SedimentBasinTests
    {
        [Fact]
        public void Design_SurfaceAreaMethod_MatchesNcdeqRatio()
        {
            var r = SedimentBasin.Design(2.0, 10.0, 5.0);
            Assert.Equal(870.0, r.SurfaceAreaSf, 0);
            Assert.Equal(2610.0, r.PoolVolumeCf, 0);
            Assert.Equal(48.0, r.DewateringTimeHr, 0);
            Assert.Equal(70.0, r.TrappingEfficiencyPct, 0);
        }

        [Fact]
        public void Design_WithPsd_UsesCampEfficiency()
        {
            var r = SedimentBasin.Design(1.0, 5.0, 10.0, new SedimentBasin.DesignOptions
            {
                Psd = new SedimentSettling.SitePsdInput { PctClay = 10, PctSilt = 30, PctSand = 60 },
            });

            Assert.Equal("Camp (1946) with Stokes/Rubey", r.TrapEfficiencyMethod);
            Assert.NotNull(r.Camp);
            Assert.Equal(r.Camp.OverallEfficiencyPct, r.TrappingEfficiencyPct, 2);
        }

        [Fact]
        public void Design_ForebayFraction_AppliesToPoolVolume()
        {
            var r = SedimentBasin.Design(1.0, 1.0, 1.0, new SedimentBasin.DesignOptions { ForebayFraction = 0.20 });
            Assert.Equal(r.PoolVolumeCf * 0.20, r.ForebayVolumeCf, 1);
        }
    }

    public class SedimentTrapTests
    {
        [Fact]
        public void Design_TwoAcres_Uses3600CfPerAcre()
        {
            var r = SedimentTrap.Design(2.0, "construction-site");
            Assert.Equal(7200.0, r.VolumeCf, 0);
            Assert.Equal(3600.0, r.SurfaceAreaSf, 0);
            Assert.True(r.Appropriate);
        }
    }

    public class SiltFenceTests
    {
        [Fact]
        public void Design_HalfAcre_Requires200Lf()
        {
            var r = SiltFence.Design(80.0, 10.0, 0.5);
            Assert.True(r.Feasible);
            Assert.Equal(200.0, r.FenceLengthLf, 0);
            Assert.Equal(50.0, r.TssRemovalPct, 0);
        }

        [Fact]
        public void Design_ExcessiveSlopeLength_NotFeasible()
        {
            var r = SiltFence.Design(150.0, 5.0, 0.25);
            Assert.False(r.Feasible);
        }
    }

    public class CheckDamTests
    {
        [Fact]
        public void Design_FiveHundredFootChannel_TwoPercentSlope_FiveDams()
        {
            var r = CheckDam.Design(500.0, 2.0, 6.0);
            Assert.Equal(100.0, r.SpacingFt, 0);
            Assert.Equal(5, r.NumberOfDams);
            Assert.Equal(2.0, r.NotchWidthFt, 1);
        }

        [Fact]
        public void Design_WeirCapacity_MatchesChowFormula()
        {
            var r = CheckDam.Design(100.0, 10.0, 6.0);
            Assert.Equal(2.89, r.WeirCapacityCfs, 1);
        }
    }

    public class TreatmentTrainTests
    {
        [Fact]
        public void Analyze_SiltFenceThenBasin_TssSeriesRemoval()
        {
            var bmps = new[]
            {
                new TreatmentTrain.BmpStageInput { Type = "silt-fence" },
                new TreatmentTrain.BmpStageInput { Type = "sediment-basin" },
            };

            var r = TreatmentTrain.Analyze(bmps, new Dictionary<string, double> { ["TSS"] = 100.0 });
            Assert.Equal(85.0, r.TotalEfficienciesPct["TSS"], 1);
            Assert.Equal(15.0, r.FinalLoads["TSS"], 1);
        }

        [Fact]
        public void Analyze_ThreeStageTrain_ProducesFormulaSteps()
        {
            var bmps = new[]
            {
                new TreatmentTrain.BmpStageInput { Type = "silt-fence" },
                new TreatmentTrain.BmpStageInput { Type = "rock-check-dam" },
                new TreatmentTrain.BmpStageInput { Type = "sediment-basin" },
            };

            var r = TreatmentTrain.Analyze(bmps);
            Assert.Equal(3, r.Stages.Count);
            Assert.True(r.TotalEfficienciesPct["TSS"] > 85.0);
            Assert.Contains(r.Steps, s => s.Label == "E_TSS");
        }
    }
}