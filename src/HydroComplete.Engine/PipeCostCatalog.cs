using System;
using System.Collections.Generic;

namespace HydroComplete.Engine
{
    /// <summary>Storm pipe unit costs ($/LF) for Hydraflow-style cost roll-ups.</summary>
    public static class PipeCostCatalog
    {
        public sealed class CostLine
        {
            public string Material { get; set; } = "";
            public double DiameterIn { get; set; }
            public double CostPerLf { get; set; }
        }

        public sealed class PipeCostItem
        {
            public string PipeName { get; set; } = "";
            public string NetworkName { get; set; } = "";
            public double LengthFt { get; set; }
            public double DiameterFt { get; set; }
            public string Material { get; set; } = "RCP";
            public double CostPerLf { get; set; }
            public double TotalCost { get; set; }
        }

        public sealed class NetworkCostRollup
        {
            public string NetworkName { get; set; } = "";
            public double TotalLengthFt { get; set; }
            public double TotalCost { get; set; }
            public List<PipeCostItem> Pipes { get; } = new List<PipeCostItem>();
        }

        private static readonly CostLine[] Catalog =
        {
            new CostLine { Material = "RCP", DiameterIn = 12, CostPerLf = 45 },
            new CostLine { Material = "RCP", DiameterIn = 15, CostPerLf = 52 },
            new CostLine { Material = "RCP", DiameterIn = 18, CostPerLf = 62 },
            new CostLine { Material = "RCP", DiameterIn = 24, CostPerLf = 78 },
            new CostLine { Material = "RCP", DiameterIn = 30, CostPerLf = 95 },
            new CostLine { Material = "RCP", DiameterIn = 36, CostPerLf = 115 },
            new CostLine { Material = "RCP", DiameterIn = 42, CostPerLf = 138 },
            new CostLine { Material = "RCP", DiameterIn = 48, CostPerLf = 165 },
            new CostLine { Material = "PVC", DiameterIn = 12, CostPerLf = 38 },
            new CostLine { Material = "PVC", DiameterIn = 15, CostPerLf = 44 },
            new CostLine { Material = "PVC", DiameterIn = 18, CostPerLf = 52 },
            new CostLine { Material = "PVC", DiameterIn = 24, CostPerLf = 68 },
            new CostLine { Material = "HDPE", DiameterIn = 12, CostPerLf = 42 },
            new CostLine { Material = "HDPE", DiameterIn = 18, CostPerLf = 58 },
            new CostLine { Material = "HDPE", DiameterIn = 24, CostPerLf = 72 },
            new CostLine { Material = "BOX", DiameterIn = 24, CostPerLf = 120 },
            new CostLine { Material = "BOX", DiameterIn = 36, CostPerLf = 165 },
            new CostLine { Material = "BOX", DiameterIn = 48, CostPerLf = 210 },
        };

        public static double LookupCostPerLf(double diameterFt, string material = "RCP")
        {
            if (diameterFt <= 0) return 0;
            double diameterIn = diameterFt * 12.0;
            string mat = string.IsNullOrWhiteSpace(material) ? "RCP" : material.Trim();

            CostLine? best = null;
            double bestDiff = double.MaxValue;
            foreach (CostLine line in Catalog)
            {
                if (!string.Equals(line.Material, mat, StringComparison.OrdinalIgnoreCase))
                    continue;

                double diff = Math.Abs(line.DiameterIn - diameterIn);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    best = line;
                }
            }

            if (best == null)
            {
                foreach (CostLine line in Catalog)
                {
                    if (!string.Equals(line.Material, "RCP", StringComparison.OrdinalIgnoreCase))
                        continue;
                    double diff = Math.Abs(line.DiameterIn - diameterIn);
                    if (diff < bestDiff)
                    {
                        bestDiff = diff;
                        best = line;
                    }
                }
            }

            return best?.CostPerLf ?? 0;
        }

        public static List<NetworkCostRollup> RollupByNetwork(
            IReadOnlyList<(string Network, string Pipe, double LengthFt, double DiameterFt, string Material)> pipes)
        {
            var byNetwork = new Dictionary<string, NetworkCostRollup>(StringComparer.OrdinalIgnoreCase);
            foreach ((string network, string pipe, double lengthFt, double diameterFt, string material) in pipes)
            {
                string netName = string.IsNullOrWhiteSpace(network) ? "Network" : network.Trim();
                if (!byNetwork.TryGetValue(netName, out NetworkCostRollup? rollup))
                {
                    rollup = new NetworkCostRollup { NetworkName = netName };
                    byNetwork[netName] = rollup;
                }

                double rate = LookupCostPerLf(diameterFt, material);
                double total = rate * lengthFt;
                rollup.Pipes.Add(new PipeCostItem
                {
                    PipeName = pipe,
                    NetworkName = netName,
                    LengthFt = lengthFt,
                    DiameterFt = diameterFt,
                    Material = material,
                    CostPerLf = rate,
                    TotalCost = total,
                });
                rollup.TotalLengthFt += lengthFt;
                rollup.TotalCost += total;
            }

            var list = new List<NetworkCostRollup>(byNetwork.Values);
            list.Sort((a, b) => string.Compare(a.NetworkName, b.NetworkName, StringComparison.OrdinalIgnoreCase));
            return list;
        }
    }
}