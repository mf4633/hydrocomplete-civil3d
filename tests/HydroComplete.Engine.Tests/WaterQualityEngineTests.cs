using System;
using System.Collections.Generic;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class WaterQualityEngineTests
    {
        [Fact]
        public void CalculateScsRunoff_DryAmc_ProducesRunoffWithTraces()
        {
            var r = WaterQualityEngine.CalculateScsRunoff(2.0, 75, antecedentDryDays: 10);

            Assert.Equal(62, r.AdjustedCurveNumber);
            Assert.Equal(AntecedentMoistureCondition.AmcI, r.MoistureCondition);
            Assert.Equal(0.087, r.RunoffDepthIn, 3);
            Assert.Contains(r.Steps, s => s.Label == "S");
            Assert.Contains(r.Steps, s => s.Label == "Ia");
            Assert.Contains(r.Steps, s => s.Label == "Q");
        }

        [Fact]
        public void CalculateScsRunoff_BelowInitialAbstraction_ReturnsZero()
        {
            var r = WaterQualityEngine.CalculateScsRunoff(0.5, 75, antecedentDryDays: 3);

            Assert.Equal(75, r.AdjustedCurveNumber);
            Assert.Equal(0.0, r.RunoffDepthIn);
            Assert.Contains(r.Steps, s => s.Label == "Q" && s.Value == 0.0);
        }

        [Fact]
        public void CalculateScsRunoff_ExplicitAmcIII_IncreasesCurveNumber()
        {
            var r = WaterQualityEngine.CalculateScsRunoff(
                2.0, 75, antecedentDryDays: 10, AntecedentMoistureCondition.AmcIII);

            Assert.Equal(88, r.AdjustedCurveNumber);
            Assert.Equal(AntecedentMoistureCondition.AmcIII, r.MoistureCondition);
        }

        [Fact]
        public void CalculateWqv_MatchesSchuelerRvFormula()
        {
            var wqv = WaterQualityEngine.CalculateWqv(1.0, 2.0, 50.0);

            Assert.Equal(0.5, wqv.RunoffCoefficientRv, 3);
            Assert.Equal(3630.0, wqv.WqvCf, 0);
            Assert.Contains(wqv.Steps, s => s.Label == "WQV");
        }

        [Fact]
        public void ComputeWqv_UsesCfPerAcreInchConstant()
        {
            var wqv = WaterQualityEngine.ComputeWqv(2.0, 1.0, 0.5);

            Assert.Equal(3630.0, wqv.WqvCf, 0);
            Assert.Equal(50.0, wqv.ImperviousPercent, 1);
        }

        [Fact]
        public void RunoffCoefficientFromImpervious_MatchesEmbeddedFormula()
        {
            Assert.Equal(0.5, WaterQualityEngine.RunoffCoefficientFromImpervious(50.0), 3);
        }

        [Fact]
        public void CalculateBuildup_ExponentialSaturation_IncreasesWithDryDays()
        {
            var shortDry = WaterQualityEngine.CalculateBuildup(1, Pollutant.Tss, 1.0, LandUse.Residential);
            var longDry = WaterQualityEngine.CalculateBuildup(10, Pollutant.Tss, 1.0, LandUse.Residential);

            Assert.True(longDry.TotalBuildupLbs > shortDry.TotalBuildupLbs);
            Assert.Equal(23.736, longDry.BuildupPerAcre, 2);
            Assert.NotEmpty(longDry.Steps);
        }

        [Fact]
        public void CalculateWashoff_PowerLaw_CapsAtAvailableBuildup()
        {
            var buildup = WaterQualityEngine.CalculateBuildup(5, Pollutant.Tss, 1.0, LandUse.Commercial);
            var washoff = WaterQualityEngine.CalculateWashoff(2.0, buildup.TotalBuildupLbs, Pollutant.Tss, LandUse.Commercial);

            Assert.Equal(1.0, washoff.WashoffFraction, 2);
            Assert.Equal(buildup.TotalBuildupLbs, washoff.WashoffLoadLbs, 3);
        }

        [Fact]
        public void CalculateEmcLoad_ResidentialCommercialIndustrial_UseDistinctEmc()
        {
            double runoff = 0.5;
            double area = 1.0;

            var res = WaterQualityEngine.CalculateEmcLoad(Pollutant.Tss, LandUse.Residential, runoff, area);
            var com = WaterQualityEngine.CalculateEmcLoad(Pollutant.Tss, LandUse.Commercial, runoff, area);
            var ind = WaterQualityEngine.CalculateEmcLoad(Pollutant.Tss, LandUse.Industrial, runoff, area);

            Assert.Equal(101, res.EmcMgPerL);
            Assert.Equal(163, com.EmcMgPerL);
            Assert.Equal(198, ind.EmcMgPerL);
            Assert.True(ind.EmcLoadLbs > com.EmcLoadLbs);
            Assert.True(com.EmcLoadLbs > res.EmcLoadLbs);
        }

        [Fact]
        public void CalculateEventPollutantLoads_CombinesEmcAndWashoff()
        {
            var loads = WaterQualityEngine.CalculateEventPollutantLoads(0.75, 2.0, LandUse.Residential, 5);

            Assert.Equal(3, loads.LoadsLbs.Count);
            Assert.True(loads.LoadsLbs[Pollutant.Tss] > 0);
            Assert.Contains(loads.Steps, s => s.Label == "TSS_total");
        }

        [Fact]
        public void ApplyBmpTreatment_Bioretention_RemovesMassPerEfficiency()
        {
            var influent = new Dictionary<string, double> { [Pollutant.Tss] = 10.0 };
            var treated = WaterQualityEngine.ApplyBmpTreatment(influent, BmpType.Bioretention);

            Assert.Equal(8.5, treated.RemovedLbs[Pollutant.Tss], 2);
            Assert.Equal(1.5, treated.TreatedLbs[Pollutant.Tss], 2);
            Assert.Equal(0.85, treated.RemovalEfficiency[Pollutant.Tss], 2);
        }

        [Fact]
        public void ApplyTreatmentTrain_SeriesRemoval_MatchesCompoundFormula()
        {
            var initial = new Dictionary<string, double> { [Pollutant.Tss] = 100.0 };
            var train = WaterQualityEngine.ApplyTreatmentTrain(
                initial,
                new[] { BmpType.Bioretention, BmpType.WetPond });

            Assert.Equal(3.0, train.FinalEffluentLbs[Pollutant.Tss], 1);
            Assert.Equal(0.97, train.OverallRemovalEfficiency[Pollutant.Tss], 2);
            Assert.Equal(2, train.BmpSteps.Count);
            Assert.Contains(train.Steps, s => s.Label == "eta_total_TSS");
        }

        [Fact]
        public void SeriesRemovalEfficiency_MatchesProductFormula()
        {
            double eta = WaterQualityEngine.SeriesRemovalEfficiency(new[] { 0.85, 0.80 });
            Assert.Equal(0.97, eta, 2);
        }

        [Fact]
        public void SizeBmp_WetPond_UsesDepthBasedArea()
        {
            var sizing = WaterQualityEngine.SizeBmp(BmpType.WetPond, 1.0, 1.0, 50.0);

            Assert.Equal(1815.0, sizing.TotalWqvCf, 0);
            Assert.Equal(453.75, sizing.SurfaceAreaSf, 1);
            Assert.Contains(sizing.Steps, s => s.Label == "A_BMP");
        }

        [Fact]
        public void SizeBmp_Bioretention_UsesSurfaceAreaRatio()
        {
            var sizing = WaterQualityEngine.SizeBmp(BmpType.Bioretention, 1.0, 1.0, 50.0);

            Assert.Equal(2178.0, sizing.SurfaceAreaSf, 0);
            Assert.Equal(5.0, sizing.FootprintPercent, 1);
        }

        [Fact]
        public void SizeBmp_VegetatedSwale_ComputesLengthFromVolume()
        {
            var sizing = WaterQualityEngine.SizeBmp(BmpType.VegetatedSwale, 1.0, 1.0, 50.0);

            Assert.True(sizing.LengthFt >= 50.0);
            Assert.Equal(2.0, sizing.WidthFt);
            Assert.Contains(sizing.Steps, s => s.Label == "L_swale");
        }

        [Fact]
        public void AnalyzeFirstFlush_PartitionsVolumeAndMass()
        {
            var totalLoads = new Dictionary<string, double>
            {
                [Pollutant.Tss] = 10.0,
                [Pollutant.Tn] = 4.0,
            };

            var ff = WaterQualityEngine.AnalyzeFirstFlush(1000.0, totalLoads, 0.20);

            Assert.Equal(200.0, ff.FirstFlushVolumeCf, 0);
            Assert.Equal(5.0, ff.FirstFlushLoadsLbs[Pollutant.Tss], 2);
            Assert.Equal(1.4, ff.FirstFlushLoadsLbs[Pollutant.Tn], 2);
            Assert.True(ff.FirstFlushConcentrationsMgPerL[Pollutant.Tss] > 0);
        }

        [Fact]
        public void ApplyBmpTreatment_UnknownBmp_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                WaterQualityEngine.ApplyBmpTreatment(new Dictionary<string, double>(), "unknown-bmp"));
        }

        [Fact]
        public void ComputeWqvFromCatchments_UsesAreaWeightedRv()
        {
            var catchments = new[]
            {
                new Catchment { AreaAcres = 1.0, RunoffC = 0.3 },
                new Catchment { AreaAcres = 1.0, RunoffC = 0.7 },
            };

            var wqv = WaterQualityEngine.ComputeWqvFromCatchments(catchments, 1.0);

            Assert.Equal(0.5, wqv.RunoffCoefficientRv, 3);
            Assert.Equal(3630.0, wqv.WqvCf, 0);
        }
    }
}