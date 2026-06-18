using System.Collections.Generic;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class PipeCostCatalogTests
    {
        [Fact]
        public void LookupCostPerLf_MatchesNearestCatalogSize()
        {
            double cost = PipeCostCatalog.LookupCostPerLf(2.0, "RCP");
            Assert.Equal(78, cost);
        }

        [Fact]
        public void RollupByNetwork_SumsPipeCosts()
        {
            var pipes = new List<(string, string, double, double, string)>
            {
                ("Storm", "P1", 100, 2.0, "RCP"),
                ("Storm", "P2", 50, 2.0, "RCP"),
            };

            List<PipeCostCatalog.NetworkCostRollup> rollups = PipeCostCatalog.RollupByNetwork(pipes);
            Assert.Single(rollups);
            Assert.Equal(150 * 78, rollups[0].TotalCost, 0);
            Assert.Equal(2, rollups[0].Pipes.Count);
        }
    }
}