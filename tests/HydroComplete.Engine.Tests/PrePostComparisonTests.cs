using System.Collections.Generic;
using System.Linq;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class PrePostComparisonTests
    {
        private static PrePostComparison.WatershedInput PreDev() => new PrePostComparison.WatershedInput
        {
            AreaAcres = 50.0,
            CurveNumber = 65.0,
            TcHours = 0.5,
        };

        private static PrePostComparison.WatershedInput PostDev() => new PrePostComparison.WatershedInput
        {
            AreaAcres = 50.0,
            CurveNumber = 80.0,
            TcHours = 0.3,
        };

        private static Dictionary<string, double> StormSuite() => new Dictionary<string, double>
        {
            ["2-year"] = 2.5,
            ["10-year"] = 4.5,
            ["25-year"] = 5.5,
            ["100-year"] = 7.0,
        };

        private static PrePostComparison.PondConfiguration LargeDetentionPond() =>
            new PrePostComparison.PondConfiguration
            {
                MaxStorageFt3 = 200_000.0,
                AvgDepthFt = 10.0,
                Outlets = new List<OutletStructures.OutletDefinition>
                {
                    new OutletStructures.OrificeOutlet
                    {
                        Name = "primary",
                        DiameterInches = 4.0,
                        Cd = 0.6,
                        InvertElevFt = 0.0,
                    },
                    new OutletStructures.WeirOutlet
                    {
                        Name = "emergency",
                        LengthFt = 12.0,
                        Cw = 3.0,
                        CrestElevFt = 8.0,
                    },
                },
            };

        [Fact]
        public void Run_NoDetention_PostExceedsPre_Fails()
        {
            PrePostComparison.PrePostComparisonResult result = PrePostComparison.Run(
                PreDev(), PostDev(), StormSuite(), pondConfig: null);

            Assert.False(result.AllPass);
            Assert.All(result.Rows, r => Assert.False(r.Pass));
            Assert.All(result.Rows, r => Assert.True(r.PostDevelopment.PeakRoutedCfs > r.PreDevelopment.PeakFlowCfs));
        }

        [Fact]
        public void Run_WithDetention_ReducesPostPeak()
        {
            PrePostComparison.PrePostComparisonResult result = PrePostComparison.Run(
                PreDev(), PostDev(), StormSuite(), LargeDetentionPond());

            Assert.All(result.Rows, r =>
                Assert.True(r.PostDevelopment.PeakRoutedCfs <= r.PostDevelopment.PeakUnroutedCfs));
            Assert.Contains(result.Rows, r => r.PostDevelopment.PeakReductionPercent > 0.0);
        }

        [Fact]
        public void Run_StormsSortedByReturnPeriod()
        {
            PrePostComparison.PrePostComparisonResult result = PrePostComparison.Run(
                PreDev(), PostDev(), StormSuite(), LargeDetentionPond());

            var keys = result.Rows.Select(r => r.ReturnPeriod).ToList();
            Assert.Equal(new[] { "2-year", "10-year", "25-year", "100-year" }, keys);
        }

        [Fact]
        public void Run_HasCalcSteps()
        {
            PrePostComparison.PrePostComparisonResult result = PrePostComparison.Run(
                PreDev(), PostDev(), StormSuite(), LargeDetentionPond());

            Assert.Equal(4, result.Rows.Count);
            Assert.Contains(result.Steps, s => s.Label == "storms");
            Assert.Contains(result.Steps, s => s.Label == "pass_count");
            Assert.Contains(result.Steps, s => s.Label == "all_pass");
        }

        [Fact]
        public void Run_AllPassFlag_ReflectsIndividualRows()
        {
            PrePostComparison.PrePostComparisonResult result = PrePostComparison.Run(
                PreDev(), PostDev(), StormSuite(), LargeDetentionPond());

            bool expected = result.Rows.All(r => r.Pass);
            Assert.Equal(expected, result.AllPass);
        }

        [Fact]
        public void PeakFlowCfs_ScalesWithRunoffDepth()
        {
            var watershed = PreDev();
            double shallow = PrePostComparison.PeakFlowCfs(watershed, 2.0);
            double deep = PrePostComparison.PeakFlowCfs(watershed, 6.0);

            Assert.True(deep > shallow);
            Assert.True(shallow > 0.0);
        }

        [Fact]
        public void Run_SmallerSite_WithDetention_CanPassLowerStorms()
        {
            var pre = new PrePostComparison.WatershedInput
            {
                AreaAcres = 10.0,
                CurveNumber = 70.0,
                TcHours = 0.6,
            };
            var post = new PrePostComparison.WatershedInput
            {
                AreaAcres = 10.0,
                CurveNumber = 78.0,
                TcHours = 0.4,
            };
            var storms = new Dictionary<string, double> { ["2-year"] = 2.0 };

            var pond = new PrePostComparison.PondConfiguration
            {
                MaxStorageFt3 = 80_000.0,
                AvgDepthFt = 8.0,
                Outlets = new List<OutletStructures.OutletDefinition>
                {
                    new OutletStructures.OrificeOutlet
                    {
                        Name = "primary",
                        DiameterInches = 3.0,
                        Cd = 0.6,
                    },
                },
            };

            PrePostComparison.PrePostComparisonResult result = PrePostComparison.Run(
                pre, post, storms, pond);

            Assert.Single(result.Rows);
            Assert.True(result.Rows[0].PostDevelopment.PeakRoutedCfs
                < result.Rows[0].PostDevelopment.PeakUnroutedCfs);
        }
    }
}