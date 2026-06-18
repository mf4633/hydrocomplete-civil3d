using System;
using System.Collections.Generic;
using System.Linq;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class BmpOptimizerTests
    {
        private static BmpOptimizer.SiteData DefaultSite() => new BmpOptimizer.SiteData
        {
            AreaAcres = 2.0,
            ImperviousPercent = 50.0,
            RainfallDepthIn = 1.0,
            AnnualRainfallIn = 45.0,
            TssConcentrationMgPerL = 80.0,
        };

        private static Dictionary<string, double> DefaultTargets() => new Dictionary<string, double>
        {
            [Pollutant.Tss] = 0.85,
            [Pollutant.Tn] = 0.40,
            [Pollutant.Tp] = 0.45,
        };

        [Fact]
        public void CalculateWqv_MatchesSchuelerFormula()
        {
            var site = new BmpOptimizer.SiteData
            {
                AreaAcres = 1.0,
                ImperviousPercent = 50.0,
                RainfallDepthIn = 1.0,
            };

            BmpOptimizer.WqvSizingResult wqv = BmpOptimizer.CalculateWqv(site);

            Assert.Equal(0.5, wqv.RunoffCoefficientRv, 3);
            Assert.Equal(1815.0, wqv.WqvCf, 0);
            Assert.Equal(0.0417, wqv.WqvAcreFt, 3);
            Assert.Contains(wqv.Steps, s => s.Label == "WQV");
            Assert.Contains(wqv.Steps, s => s.Label == "Rv");
        }

        [Fact]
        public void CalculateWqv_UsesDefaultAreaWhenZero()
        {
            var site = new BmpOptimizer.SiteData { AreaAcres = 0.0, ImperviousPercent = 0.0 };
            BmpOptimizer.WqvSizingResult wqv = BmpOptimizer.CalculateWqv(site);

            Assert.Equal(0.05, wqv.RunoffCoefficientRv, 3);
            Assert.Equal(181.5, wqv.WqvCf, 0);
        }

        [Fact]
        public void PresentWorthAnnuity_MatchesAnnuityFormula()
        {
            double pwa = BmpOptimizer.PresentWorthAnnuity(0.05, 20.0);
            Assert.Equal(12.462, pwa, 2);
        }

        [Fact]
        public void PresentWorthAnnuity_ZeroRate_ReturnsYears()
        {
            Assert.Equal(20.0, BmpOptimizer.PresentWorthAnnuity(0.0, 20.0), 3);
        }

        [Fact]
        public void LifecycleCost_MatchesNpvComponents()
        {
            BmpOptimizer.LifecycleCostResult lc = BmpOptimizer.LifecycleCost(
                "wet-pond", footprintSf: 1000.0, designLifeYears: 20.0, discountRate: 0.05);

            Assert.Equal(8500.0, lc.ConstructionCost, 0);
            Assert.Equal(5000.0, lc.LandCost, 0);
            Assert.Equal(255.0, lc.AnnualMaintenance, 0);
            Assert.True(lc.MaintenanceNpv > 3000.0);
            Assert.Equal(lc.ConstructionCost + lc.LandCost + lc.MaintenanceNpv, lc.TotalNpv, 0);
            Assert.NotEmpty(lc.Steps);
            Assert.Contains(lc.Steps, s => s.Label == "NPV");
        }

        [Fact]
        public void LifecycleCost_UnknownBmp_ReturnsError()
        {
            BmpOptimizer.LifecycleCostResult lc = BmpOptimizer.LifecycleCost("unknown-bmp", 1000.0);
            Assert.NotNull(lc.Error);
            Assert.Equal(0.0, lc.TotalNpv);
        }

        [Fact]
        public void EstimateAnnualTssLoad_MatchesSimpleMethod()
        {
            var site = DefaultSite();
            double rv = WaterQualityEngine.RunoffCoefficientFromImpervious(site.ImperviousPercent);
            double load = BmpOptimizer.EstimateAnnualTssLoadLbs(site, rv);

            Assert.True(load > 0.0);
            Assert.Equal(815.8, load, 0);
        }

        [Fact]
        public void SeriesRemoval_TwoStage_MatchesFormula()
        {
            double combined = BmpOptimizer.SeriesRemoval(new[] { 0.60, 0.50 });
            Assert.Equal(0.80, combined, 2);
        }

        [Fact]
        public void OptimizeBmpSelection_RanksByCostPerLbAscending()
        {
            BmpOptimizer.BmpSelectionResult result = BmpOptimizer.OptimizeBmpSelection(
                DefaultSite(), DefaultTargets());

            Assert.Equal(10, result.Rankings.Count);
            for (int i = 1; i < result.Rankings.Count; i++)
                Assert.True(result.Rankings[i].CostPerLb >= result.Rankings[i - 1].CostPerLb);

            Assert.Equal(1, result.Rankings[0].Rank);
            Assert.NotEmpty(result.Steps);
        }

        [Fact]
        public void OptimizeBmpSelection_FlagsMeetsTarget()
        {
            BmpOptimizer.BmpSelectionResult result = BmpOptimizer.OptimizeBmpSelection(
                DefaultSite(), DefaultTargets());

            var infiltration = result.Rankings.First(r => r.BmpType == "infiltration-basin");
            var grassSwale = result.Rankings.First(r => r.BmpType == "grass-swale");

            Assert.True(infiltration.MeetsTarget);
            Assert.False(grassSwale.MeetsTarget);
            Assert.True(result.Rankings.Count(r => r.MeetsTarget) >= 2);
        }

        [Fact]
        public void OptimizeBmpSelection_GrassSwaleCheaperPerLbThanSandFilter()
        {
            BmpOptimizer.BmpSelectionResult result = BmpOptimizer.OptimizeBmpSelection(
                DefaultSite(), DefaultTargets());

            var grass = result.Rankings.First(r => r.BmpType == "grass-swale");
            var sand = result.Rankings.First(r => r.BmpType == "sand-filter");

            Assert.True(grass.CostPerLb < sand.CostPerLb);
            Assert.True(grass.Rank < sand.Rank);
        }

        [Fact]
        public void OptimizeTreatmentTrain_FindsValidTrain()
        {
            BmpOptimizer.TreatmentTrainResult result = BmpOptimizer.OptimizeTreatmentTrain(
                DefaultSite(), DefaultTargets());

            Assert.NotNull(result.BestTrain);
            Assert.True(result.TotalEvaluated > 0);
            Assert.True(result.BestTrain!.CombinedRemoval[Pollutant.Tss] >= 0.85);
            Assert.True(result.BestTrain.CombinedRemoval[Pollutant.Tn] >= 0.40);
            Assert.True(result.BestTrain.CombinedRemoval[Pollutant.Tp] >= 0.45);
            Assert.Contains(result.Steps, s => s.Label == "best_train_cost");
        }

        [Fact]
        public void OptimizeTreatmentTrain_IncludesTwoAndThreeBmpTrains()
        {
            BmpOptimizer.TreatmentTrainResult result = BmpOptimizer.OptimizeTreatmentTrain(
                DefaultSite(), DefaultTargets());

            Assert.Contains(result.AllTrains, t => t.TrainSize == 2);
            Assert.Contains(result.AllTrains, t => t.TrainSize == 3);
            Assert.True(result.BestTrain!.TotalCost <= result.AllTrains.Max(t => t.TotalCost));
        }

        [Fact]
        public void OptimizeTreatmentTrain_SingleBmpCannotMeetTarget_ReturnsNullBest()
        {
            var site = DefaultSite();
            var impossibleTargets = new Dictionary<string, double>
            {
                [Pollutant.Tss] = 0.99,
                [Pollutant.Tn] = 0.95,
                [Pollutant.Tp] = 0.95,
            };

            BmpOptimizer.TreatmentTrainResult result = BmpOptimizer.OptimizeTreatmentTrain(
                site, impossibleTargets);

            Assert.Null(result.BestTrain);
            Assert.Equal(0, result.TotalEvaluated);
        }

        [Fact]
        public void DefaultCostLibrary_ContainsTenBmpTypes()
        {
            Assert.Equal(10, BmpOptimizer.DefaultCostLibrary.Count);
            Assert.True(BmpOptimizer.DefaultCostLibrary.ContainsKey("bioretention"));
            Assert.True(BmpOptimizer.DefaultCostLibrary.ContainsKey("infiltration-basin"));
        }
    }
}