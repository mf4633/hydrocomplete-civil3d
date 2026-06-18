using System;
using System.Collections.Generic;
using System.Linq;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class PondChainRoutingTests
    {
        private static List<DetentionRouting.HydrographPoint> TriangularInflow(
            double peakCfs,
            double durationHours,
            double dtHours = 0.1)
        {
            var hydro = new List<DetentionRouting.HydrographPoint>();
            for (double t = 0.0; t <= durationHours; t += dtHours)
            {
                double flow = t <= durationHours / 2.0
                    ? peakCfs * (t / (durationHours / 2.0))
                    : peakCfs * (1.0 - (t - durationHours / 2.0) / (durationHours / 2.0));
                hydro.Add(new DetentionRouting.HydrographPoint
                {
                    TimeHours = Math.Round(t, 2),
                    FlowCfs = Math.Max(0.0, flow),
                });
            }

            return hydro;
        }

        private static PondChainRouting.PondDefinition SmallPond(
            double maxStorageFt3,
            double orificeInches,
            double avgDepthFt = 8.0)
        {
            var outlets = new List<OutletStructures.OutletDefinition>
            {
                new OutletStructures.OrificeOutlet
                {
                    Name = "primary",
                    DiameterInches = orificeInches,
                    Cd = 0.6,
                    InvertElevFt = 0.0,
                },
            };

            return new PondChainRouting.PondDefinition
            {
                StorageCurve = DetentionRouting.BuildPrismaticStorageIndicationCurve(
                    maxStorageFt3, outlets, avgDepthFt),
                Outlets = outlets,
                TimestepHours = 0.1,
            };
        }

        [Fact]
        public void Route_EmptyPondList_ReturnsEmptyResult()
        {
            var inflow = TriangularInflow(30.0, 2.0);
            PondChainRouting.PondChainResult result = PondChainRouting.Route(
                inflow, Array.Empty<PondChainRouting.PondDefinition>());

            Assert.Equal(0, result.PondCount);
            Assert.True(result.Converged);
            Assert.Empty(result.PondResults);
        }

        [Fact]
        public void Route_SinglePond_AttenuatesInflow()
        {
            var inflow = TriangularInflow(40.0, 2.5);
            var ponds = new[] { SmallPond(40_000.0, 6.0) };

            PondChainRouting.PondChainResult result = PondChainRouting.Route(inflow, ponds);

            Assert.Single(result.PondResults);
            Assert.True(result.Converged);
            Assert.Equal(0, result.Iterations);
            Assert.True(result.PondResults[0].PeakOutflowCfs < result.PondResults[0].PeakInflowCfs);
            Assert.Contains(result.Steps, s => s.Label == "Q_final");
        }

        [Fact]
        public void Route_TwoPonds_FinalPeakLessThanSinglePond()
        {
            var inflow = TriangularInflow(50.0, 3.0);
            var single = new[] { SmallPond(60_000.0, 8.0) };
            var chain = new[]
            {
                SmallPond(35_000.0, 6.0),
                SmallPond(45_000.0, 4.0),
            };

            PondChainRouting.PondChainResult singleResult = PondChainRouting.Route(inflow, single);
            PondChainRouting.PondChainResult chainResult = PondChainRouting.Route(inflow, chain);

            double singlePeak = singleResult.PondResults[0].PeakOutflowCfs;
            double chainPeak = chainResult.PondResults.Last().PeakOutflowCfs;

            Assert.True(chainPeak <= singlePeak + 0.5);
            Assert.Equal(2, chainResult.PondResults.Count);
        }

        [Fact]
        public void Route_TwoPonds_DownstreamReceivesUpstreamOutflow()
        {
            var inflow = TriangularInflow(35.0, 2.0);
            var ponds = new[]
            {
                SmallPond(30_000.0, 6.0),
                SmallPond(40_000.0, 5.0),
            };

            PondChainRouting.PondChainResult result = PondChainRouting.Route(inflow, ponds);

            Assert.Equal(35.0, result.PondResults[0].PeakInflowCfs, 0);
            Assert.True(result.PondResults[1].PeakInflowCfs <= result.PondResults[0].PeakOutflowCfs + 1.0);
            Assert.True(result.PondResults[1].PeakInflowCfs > 0.0);
        }

        [Fact]
        public void Route_MultiPond_TailwaterIterationRuns()
        {
            var inflow = TriangularInflow(45.0, 2.5);
            var ponds = new[]
            {
                SmallPond(25_000.0, 5.0, avgDepthFt: 6.0),
                SmallPond(30_000.0, 4.0, avgDepthFt: 6.0),
                SmallPond(35_000.0, 3.0, avgDepthFt: 6.0),
            };

            PondChainRouting.PondChainResult result = PondChainRouting.Route(inflow, ponds);

            Assert.Equal(3, result.PondResults.Count);
            Assert.True(result.Iterations > 0);
            Assert.Equal(2, result.Tailwaters.Count);
            Assert.Contains(result.Steps, s => s.Label == "iterations");
            Assert.Contains(result.Steps, s => s.Label == "converged");
        }

        [Fact]
        public void ApplyTailwater_ReducesOutflowAtGivenStage()
        {
            var outlets = new List<OutletStructures.OutletDefinition>
            {
                new OutletStructures.OrificeOutlet
                {
                    Name = "primary",
                    DiameterInches = 12.0,
                    Cd = 0.6,
                    InvertElevFt = 0.0,
                },
            };

            var baseCurve = DetentionRouting.BuildPrismaticStorageIndicationCurve(
                20_000.0, outlets, avgDepthFt: 6.0);

            var noTw = PondChainRouting.ApplyTailwater(baseCurve, outlets, 0.0);
            var withTw = PondChainRouting.ApplyTailwater(baseCurve, outlets, 2.0);

            var at6 = baseCurve.First(p => Math.Abs(p.ElevationFt - 6.0) < 0.01);
            var noTwAt6 = noTw.First(p => Math.Abs(p.ElevationFt - 6.0) < 0.01);
            var withTwAt6 = withTw.First(p => Math.Abs(p.ElevationFt - 6.0) < 0.01);

            Assert.True(noTwAt6.OutflowCfs > 0.0);
            Assert.True(withTwAt6.OutflowCfs < noTwAt6.OutflowCfs);
            Assert.Equal(at6.StorageFt3, withTwAt6.StorageFt3, 0);
        }

        [Fact]
        public void Route_FinalOutflowLessThanInflowPeak()
        {
            var inflow = TriangularInflow(55.0, 3.0);
            var ponds = new[]
            {
                SmallPond(50_000.0, 7.0),
                SmallPond(55_000.0, 5.0),
            };

            PondChainRouting.PondChainResult result = PondChainRouting.Route(inflow, ponds);
            double inflowPeak = inflow.Max(p => p.FlowCfs);
            double finalPeak = result.PondResults.Last().PeakOutflowCfs;

            Assert.True(finalPeak < inflowPeak);
        }

        [Fact]
        public void Route_HasSummaryCalcSteps()
        {
            var inflow = TriangularInflow(25.0, 2.0);
            var ponds = new[] { SmallPond(20_000.0, 6.0), SmallPond(25_000.0, 5.0) };

            PondChainRouting.PondChainResult result = PondChainRouting.Route(inflow, ponds);

            Assert.Contains(result.Steps, s => s.Label == "pond_count" && s.Value == 2.0);
            Assert.Contains(result.Steps, s => s.Label == "Q_final");
            Assert.Contains(result.Steps, s => s.Label == "damping");
        }
    }
}