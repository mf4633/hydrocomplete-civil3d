using System;
using System.Collections.Generic;
using System.Linq;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class WaterQualityRoutingTests
    {
        [Fact]
        public void BioretentionRouting_TreatsVolumeWithinCapacity()
        {
            var config = new BioretentionRouting.BioretentionConfig();
            var result = BioretentionRouting.Route(config, designVolumeCf: 5000, surfaceAreaSf: 1000);

            Assert.True(result.TreatedVolumeCf > 0);
            Assert.True(result.TreatedVolumeCf <= 5000);
            Assert.True(result.ResidenceTimeHr > 0);
            Assert.Contains(result.Steps, s => s.Label == "V_treated");
        }

        [Fact]
        public void BioretentionRouting_OverflowWhenVolumeExceedsCapacity()
        {
            var config = new BioretentionRouting.BioretentionConfig { PondingDepthFt = 0.5, MediaDepthFt = 2.0 };
            var result = BioretentionRouting.Route(config, designVolumeCf: 50_000, surfaceAreaSf: 500);

            Assert.True(result.OverflowVolumeCf > 0);
            Assert.True(result.BypassFractionPercent > 0);
            Assert.True(result.TreatedVolumeCf < 50_000);
        }

        [Fact]
        public void BioretentionRouting_DryMedia_IncreasesAvailableStorage()
        {
            var dry = new BioretentionRouting.BioretentionConfig { CurrentMediaMoisture = 0.10 };
            var wet = new BioretentionRouting.BioretentionConfig { CurrentMediaMoisture = 0.35 };

            var dryResult = BioretentionRouting.Route(dry, 8000, 800);
            var wetResult = BioretentionRouting.Route(wet, 8000, 800);

            Assert.True(dryResult.TotalCapacityCf > wetResult.TotalCapacityCf);
            Assert.True(dryResult.TreatedVolumeCf >= wetResult.TreatedVolumeCf);
        }

        [Fact]
        public void BioretentionRouting_TssRemoval_IncreasesWithResidenceTime()
        {
            var slowDrain = BioretentionRouting.Route(
                new BioretentionRouting.BioretentionConfig { KsatInPerHr = 0.5 },
                1000, 1000);
            var fastDrain = BioretentionRouting.Route(
                new BioretentionRouting.BioretentionConfig { KsatInPerHr = 5.0 },
                1000, 1000);

            Assert.True(slowDrain.ResidenceTimeHr > fastDrain.ResidenceTimeHr);
            double slowTss = slowDrain.RemovalEfficiency[Pollutant.Tss].TreatedPercent;
            double fastTss = fastDrain.RemovalEfficiency[Pollutant.Tss].TreatedPercent;
            Assert.True(slowTss > fastTss);
        }

        [Fact]
        public void BioretentionRouting_BlendedEfficiency_AccountsForBypass()
        {
            var result = BioretentionRouting.Route(
                new BioretentionRouting.BioretentionConfig(),
                designVolumeCf: 100_000,
                surfaceAreaSf: 500);

            var tss = result.RemovalEfficiency[Pollutant.Tss];
            Assert.True(tss.BlendedPercent < tss.TreatedPercent);
        }

        [Fact]
        public void WetPondRouting_FirstOrderDecay_ProducesRemoval()
        {
            var result = WetlandRouting.RouteWetPond(
                new WetlandRouting.WetPondConfig { ResidentTimeDays = 14 },
                10_000,
                5000);

            Assert.True(result.RemovalEfficiency[Pollutant.Tss].TreatedPercent > 50);
            Assert.True(result.ResidenceTimeDays >= 13.9);
            Assert.Contains(result.Steps, s => s.Label == "E_TSS");
        }

        [Fact]
        public void WetPondRouting_LongerResidence_IncreasesRemoval()
        {
            var shortPond = WetlandRouting.RouteWetPond(
                new WetlandRouting.WetPondConfig { ResidentTimeDays = 7 }, 5000, 3000);
            var longPond = WetlandRouting.RouteWetPond(
                new WetlandRouting.WetPondConfig { ResidentTimeDays = 28 }, 5000, 3000);

            Assert.True(
                longPond.RemovalEfficiency[Pollutant.Tn].TreatedPercent >
                shortPond.RemovalEfficiency[Pollutant.Tn].TreatedPercent);
        }

        [Fact]
        public void ConstructedWetlandRouting_FourZonesInSeries()
        {
            var result = WetlandRouting.RouteConstructedWetland(
                new WetlandRouting.WetlandConfig(),
                15_000,
                12_000);

            Assert.Equal(4, result.ZoneCount);
            Assert.Equal(0, result.OverflowVolumeCf);
            var tss = result.RemovalEfficiency[Pollutant.Tss];
            Assert.Equal(4, tss.Zones.Count);
            Assert.True(tss.TreatedPercent > 0);
        }

        [Fact]
        public void ConstructedWetlandRouting_EffluentApproachesBackground()
        {
            var result = WetlandRouting.RouteConstructedWetland(
                new WetlandRouting.WetlandConfig(),
                5000,
                50_000);

            var tp = result.RemovalEfficiency[Pollutant.Tp];
            double finalConc = tp.Zones.Last().EffluentConcentration;
            Assert.True(finalConc > 0.04);
            Assert.True(finalConc < 0.40);
        }

        [Fact]
        public void HargreavesEt_CharlotteJuly_InExpectedRange()
        {
            var et = ContinuousSimulation.CalculateHargreavesEt(197, 6, "charlotte-nc");
            Assert.InRange(et.Et0In, 0.10, 0.35);
        }

        [Fact]
        public void HargreavesEt_PhoenixJuly_ExceedsCharlotte()
        {
            var charlotte = ContinuousSimulation.CalculateHargreavesEt(197, 6, "charlotte-nc");
            var atlanta = ContinuousSimulation.CalculateHargreavesEt(197, 6, "atlanta-ga");
            Assert.True(atlanta.Et0In > 0);
            Assert.True(atlanta.Et0In >= charlotte.Et0In * 0.8);
        }

        [Fact]
        public void SoilMoistureBalance_DryPeriod_ReducesTheta()
        {
            var soil = ContinuousSimulation.GetSoilParamsForLandUse("residential-low");
            double theta = soil.FieldCapacity;
            for (int i = 0; i < 10; i++)
            {
                var smb = ContinuousSimulation.DailySoilMoistureBalance(theta, 0, 0.20, soil);
                theta = smb.ThetaNew;
            }

            Assert.True(theta < soil.FieldCapacity);
            Assert.True(theta >= soil.WiltingPoint);
        }

        [Fact]
        public void SoilMoistureBalance_RainEvent_WetsSoil()
        {
            var soil = ContinuousSimulation.GetSoilParamsForLandUse(LandUse.Commercial);
            double theta = soil.WiltingPoint + 0.02;
            var smb = ContinuousSimulation.DailySoilMoistureBalance(theta, 1.0, 0, soil);
            Assert.True(smb.ThetaNew > theta);
            Assert.True(smb.InfiltrationIn > 0);
        }

        [Fact]
        public void GenerateHistoricalEvents_Charlotte_IsDeterministic()
        {
            var a = ContinuousSimulation.GenerateHistoricalEvents("charlotte-nc", 1);
            var b = ContinuousSimulation.GenerateHistoricalEvents("charlotte-nc", 1);
            Assert.Equal(365, a.Count);
            Assert.Equal(a[100].RainfallIn, b[100].RainfallIn, 3);
        }

        [Fact]
        public void ContinuousSimulation_OneYear_ProducesRunoffAndLoads()
        {
            var result = ContinuousSimulation.Run(new ContinuousSimulation.SiteData
            {
                Location = "charlotte-nc",
                Years = 1,
                AreaAcres = 2.0,
                CurveNumber = 75,
                LandUse = LandUse.Commercial,
            });

            Assert.True(result.EventCount > 0);
            Assert.True(result.TotalRunoffAcreIn > 0);
            Assert.True(result.TotalLoadsLbs[Pollutant.Tss] > 0);
            Assert.Equal(12, result.MonthlyAverage.Count);
        }

        [Fact]
        public void ContinuousSimulation_WithBioretention_ReportsRemoval()
        {
            var result = ContinuousSimulation.Run(
                new ContinuousSimulation.SiteData { Location = "charlotte-nc", Years = 1, AreaAcres = 3.0 },
                new ContinuousSimulation.BmpSimulationConfig
                {
                    BmpType = BmpType.Bioretention,
                    SurfaceAreaSf = 3000,
                });

            Assert.NotNull(result.OverallRemovalPercent);
            Assert.True(result.OverallRemovalPercent![Pollutant.Tss] > 0);
            Assert.True(result.TotalTreatedLbs[Pollutant.Tss] > 0);
        }

        [Fact]
        public void SoilDatabase_Lookup_ByKey_ReturnsCecil()
        {
            SoilDatabase.SoilProperties soil = SoilDatabase.Lookup("cecil-sandy-loam");
            Assert.Equal('B', soil.HydrologicSoilGroup);
            Assert.Equal(0.24, soil.KFactor, 2);
            Assert.Equal(0.60, soil.InfiltrationRateInPerHr, 2);
        }

        [Fact]
        public void SoilDatabase_Lookup_FuzzyName_MatchesSeries()
        {
            SoilDatabase.SoilProperties soil = SoilDatabase.Lookup("Norfolk sandy loam");
            Assert.Equal("Norfolk", soil.Series);
            Assert.Equal('A', soil.HydrologicSoilGroup);
        }

        [Fact]
        public void SoilDatabase_HasAtLeastFiftyEntries()
        {
            Assert.True(SoilDatabase.AllSoilKeys().Count >= 50);
        }

        [Fact]
        public void SoilDatabase_SuggestBmp_HsgD_BioretentionNotRecommended()
        {
            SoilDatabase.BmpSuggestionResult suggestion =
                SoilDatabase.SuggestBmp("iredell-loam", BmpType.Bioretention);

            Assert.Equal(SoilDatabase.BmpSuitability.NotRecommended, suggestion.Suitability);
            Assert.Contains(BmpType.WetPond, suggestion.Alternatives);
        }

        [Fact]
        public void SoilDatabase_SuggestBmp_HsgA_BioretentionExcellent()
        {
            SoilDatabase.BmpSuggestionResult suggestion =
                SoilDatabase.SuggestBmp("norfolk-sandy-loam", BmpType.Bioretention);

            Assert.Equal(SoilDatabase.BmpSuitability.Excellent, suggestion.Suitability);
        }

        [Fact]
        public void SoilDatabase_SuggestBmp_WetlandOnRains_IsExcellent()
        {
            SoilDatabase.BmpSuggestionResult suggestion =
                SoilDatabase.SuggestBmp("rains-sandy-loam", "constructed-wetland");

            Assert.Equal(SoilDatabase.BmpSuitability.Excellent, suggestion.Suitability);
        }

        [Fact]
        public void SoilDatabase_Lookup_Unknown_Throws()
        {
            Assert.Throws<ArgumentException>(() => SoilDatabase.Lookup("nonexistent-soil-xyz"));
        }
    }
}