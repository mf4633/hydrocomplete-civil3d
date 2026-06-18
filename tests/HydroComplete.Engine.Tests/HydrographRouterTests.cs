using System;
using System.Collections.Generic;
using System.Linq;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class HydrographRouterTests
    {
        private static HydrographRouter.HydrographRouterOptions DefaultOptions() =>
            new HydrographRouter.HydrographRouterOptions
            {
                StormDepthIn = 5.0,
                TimestepHours = 0.25,
                UnitHydroMethod = HydrographConvolution.UnitHydrographMethod.Scs,
                ApplyMuskingumCunge = false,
                DefaultTcMinutes = 10.0,
            };

        private static NetworkAnalysisPipe MakePipe(
            string key,
            string us,
            string ds,
            double lengthFt = 500.0,
            double diameterFt = 2.0,
            double slope = 0.01)
        {
            return new NetworkAnalysisPipe
            {
                PipeKey = key,
                NetworkName = "NET",
                PipeName = key,
                UpstreamNodeId = us,
                DownstreamNodeId = ds,
                LengthFt = lengthFt,
                Segment = new PipeSegment
                {
                    Name = key,
                    DiameterFt = diameterFt,
                    Slope = slope,
                    ManningN = 0.013,
                },
            };
        }

        private static Catchment MakeCatchment(
            string name,
            double areaAcres,
            string outfallId,
            double tc = 10.0,
            double cn = 75.0)
        {
            return new Catchment
            {
                Name = name,
                AreaAcres = areaAcres,
                CurveNumber = cn,
                TcMinutes = tc,
                OutfallStructureId = outfallId,
            };
        }

        [Fact]
        public void CombineHydrographs_SumsFlows()
        {
            var a = new[] { 10.0, 20.0, 0.0 };
            var b = new[] { 5.0, 0.0, 15.0 };
            double[] combined = HydrographRouter.CombineHydrographs(a, b);
            Assert.Equal(15.0, combined[0], 6);
            Assert.Equal(20.0, combined[1], 6);
            Assert.Equal(15.0, combined[2], 6);
        }

        [Fact]
        public void CombineAtJunction_SuperposesBranches()
        {
            var branches = new List<IReadOnlyList<double>>
            {
                new[] { 1.0, 2.0 },
                new[] { 3.0, 4.0 },
                new[] { 0.5, 0.0 },
            };
            double[] combined = HydrographRouter.CombineAtJunction(branches);
            Assert.Equal(4.5, combined[0], 6);
            Assert.Equal(6.0, combined[1], 6);
        }

        [Fact]
        public void ShiftLagHydrograph_DelaysPeak()
        {
            var flows = new[] { 0.0, 100.0, 50.0, 0.0 };
            double[] shifted = HydrographRouter.ShiftLagHydrograph(flows, dtHours: 0.5, lagHours: 1.0);
            Assert.Equal(0.0, shifted[0], 6);
            Assert.Equal(0.0, shifted[1], 6);
            Assert.Equal(0.0, shifted[2], 6);
            Assert.Equal(100.0, shifted[3], 6);
            Assert.Equal(50.0, shifted[4], 6);
        }

        [Fact]
        public void Route_SingleCatchment_SinglePipe_ProducesPeak()
        {
            var catchments = new List<Catchment>
            {
                MakeCatchment("C1", 1.0, "S1"),
            };
            var pipes = new List<NetworkAnalysisPipe>
            {
                MakePipe("P1", "S1", "OUT", lengthFt: 200.0),
            };

            var result = HydrographRouter.Route(catchments, pipes, DefaultOptions());

            Assert.Equal(CatchmentAssignmentMethod.OutletStructure, result.AssignmentMethod);
            Assert.Single(result.PipeHydrographs);
            HydrographRouter.PipeHydrographResult p1 = result.PipeHydrographs["P1"];
            Assert.True(p1.PeakFlowCfs > 0);
            Assert.True(p1.VolumeAcreFt > 0);
            Assert.NotEmpty(p1.Ordinates);
            Assert.True(p1.TimeToPeakMinutes >= 0);
        }

        [Fact]
        public void Route_YJunction_SuperposesAtConfluence()
        {
            var catchments = new List<Catchment>
            {
                MakeCatchment("C1", 1.0, "S1", tc: 10.0),
                MakeCatchment("C2", 2.0, "S2", tc: 12.0),
            };
            var pipes = new List<NetworkAnalysisPipe>
            {
                MakePipe("P1", "S1", "S3", lengthFt: 300.0),
                MakePipe("P2", "S2", "S3", lengthFt: 300.0),
                MakePipe("P3", "S3", "OUT", lengthFt: 400.0),
            };

            var result = HydrographRouter.Route(catchments, pipes, DefaultOptions());

            double peakP1 = result.PipeHydrographs["P1"].PeakFlowCfs;
            double peakP2 = result.PipeHydrographs["P2"].PeakFlowCfs;
            double peakP3 = result.PipeHydrographs["P3"].PeakFlowCfs;

            Assert.True(peakP1 > 0);
            Assert.True(peakP2 > 0);
            Assert.True(peakP3 > 0);
            Assert.True(peakP3 >= Math.Max(peakP1, peakP2) * 0.5);
            Assert.True(peakP3 <= peakP1 + peakP2 + 1.0);
        }

        [Fact]
        public void Route_VolumeConservation_OutfallMatchesCatchmentSum()
        {
            var catchments = new List<Catchment>
            {
                MakeCatchment("C1", 1.5, "S1", cn: 70.0),
                MakeCatchment("C2", 2.5, "S2", cn: 80.0),
            };
            var pipes = new List<NetworkAnalysisPipe>
            {
                MakePipe("P1", "S1", "S3", lengthFt: 250.0),
                MakePipe("P2", "S2", "S3", lengthFt: 250.0),
                MakePipe("P3", "S3", "OUT", lengthFt: 350.0),
            };

            var options = DefaultOptions();
            var result = HydrographRouter.Route(catchments, pipes, options);

            double catchmentVolume = result.CatchmentHydrographs.Sum(c => c.Hydrograph.VolumeAcreFt);
            double outfallVolume = result.PipeHydrographs["P3"].VolumeAcreFt;

            Assert.True(catchmentVolume > 0);
            Assert.InRange(outfallVolume, catchmentVolume * 0.85, catchmentVolume * 1.15);
        }

        [Fact]
        public void Route_TravelLag_IncreasesTimeToPeakOnDownstreamPipe()
        {
            var catchments = new List<Catchment> { MakeCatchment("C1", 1.0, "S1") };
            var pipes = new List<NetworkAnalysisPipe>
            {
                MakePipe("P1", "S1", "S2", lengthFt: 1000.0, slope: 0.005),
                MakePipe("P2", "S2", "OUT", lengthFt: 1000.0, slope: 0.005),
            };

            var result = HydrographRouter.Route(catchments, pipes, DefaultOptions());

            double tPeakP1 = result.PipeHydrographs["P1"].TimeToPeakMinutes;
            double tPeakP2 = result.PipeHydrographs["P2"].TimeToPeakMinutes;
            Assert.True(tPeakP2 >= tPeakP1);
            Assert.True(result.PipeHydrographs["P1"].TravelTimeMinutes > 0);
        }

        [Fact]
        public void Route_UnequalBranchLengths_TrunkCombinesBothBranches()
        {
            var catchments = new List<Catchment>
            {
                MakeCatchment("C1", 1.0, "H1"),
                MakeCatchment("C2", 2.0, "H2"),
            };
            var pipes = new List<NetworkAnalysisPipe>
            {
                MakePipe("P1", "H1", "A", lengthFt: 200.0),
                MakePipe("P2", "A", "C", lengthFt: 600.0),
                MakePipe("P3", "H2", "C", lengthFt: 200.0),
                MakePipe("P4", "C", "OUT", lengthFt: 300.0),
            };

            var result = HydrographRouter.Route(catchments, pipes, DefaultOptions());

            double peakP4 = result.PipeHydrographs["P4"].PeakFlowCfs;
            double branchPeak = Math.Max(
                result.PipeHydrographs["P1"].PeakFlowCfs,
                result.PipeHydrographs["P3"].PeakFlowCfs);
            Assert.True(peakP4 >= branchPeak * 0.5);
        }

        [Fact]
        public void Route_StructureNameMatch_AssignsOutlet()
        {
            var catchments = new List<Catchment>
            {
                new Catchment
                {
                    Name = "North",
                    AreaAcres = 1.0,
                    CurveNumber = 75.0,
                    TcMinutes = 10.0,
                    OutfallStructureName = "MH-101",
                },
            };
            var pipes = new List<NetworkAnalysisPipe>
            {
                MakePipe("P1", "H101", "OUT", lengthFt: 150.0),
            };
            var names = new Dictionary<string, string> { ["H101"] = "MH-101" };

            var result = HydrographRouter.Route(catchments, pipes, DefaultOptions(), names);

            Assert.Equal("H101", result.CatchmentHydrographs[0].AssignedStructureId);
            Assert.True(result.PipeHydrographs["P1"].PeakFlowCfs > 0);
        }

        [Fact]
        public void RouteMuskingumCunge_LongReach_AttenuatesPeak()
        {
            var inflow = new List<double>();
            for (int i = 0; i < 20; i++)
                inflow.Add(i == 5 ? 200.0 : 0.0);

            var pipe = MakePipe("PL", "US", "DS", lengthFt: 5000.0, diameterFt: 4.0, slope: 0.004);
            double[] lagged = HydrographRouter.RouteMuskingumCunge(
                inflow, pipe, dtHours: 0.25, applyMuskingumCunge: false, minLengthFt: 1000.0);
            double[] routed = HydrographRouter.RouteMuskingumCunge(
                inflow, pipe, dtHours: 0.25, applyMuskingumCunge: true, minLengthFt: 1000.0);

            double peakLag = lagged.Max();
            double peakMc = routed.Max();
            Assert.True(peakMc > 0);
            Assert.True(peakMc <= peakLag + 1.0);
        }

        [Fact]
        public void Route_NoCatchmentAssignment_ThrowsOnMultipleCatchmentsWithoutTopology()
        {
            var catchments = new List<Catchment>
            {
                MakeCatchment("A", 1.0, "S1"),
                MakeCatchment("B", 2.0, "S2"),
            };

            Assert.Throws<ArgumentException>(() =>
                HydrographRouter.Route(catchments, Array.Empty<NetworkAnalysisPipe>(), DefaultOptions()));
        }

        [Fact]
        public void Route_ProducesOrdinatesInMinutes()
        {
            var catchments = new List<Catchment> { MakeCatchment("C1", 1.0, "S1") };
            var pipes = new List<NetworkAnalysisPipe> { MakePipe("P1", "S1", "OUT") };

            var result = HydrographRouter.Route(catchments, pipes, DefaultOptions());
            var ord = result.PipeHydrographs["P1"].Ordinates;

            Assert.NotEmpty(ord);
            Assert.Equal(0.0, ord[0].TimeMinutes, 3);
            Assert.True(ord.Skip(1).All(o => o.TimeMinutes > ord[0].TimeMinutes || o.FlowCfs == 0));
        }

        [Fact]
        public void Route_MultipleCatchments_GeneratesPerCatchmentHydrographs()
        {
            var catchments = new List<Catchment>
            {
                MakeCatchment("C1", 1.0, "S1"),
                MakeCatchment("C2", 3.0, "S2"),
            };
            var pipes = new List<NetworkAnalysisPipe>
            {
                MakePipe("P1", "S1", "OUT"),
                MakePipe("P2", "S2", "OUT"),
            };

            var result = HydrographRouter.Route(catchments, pipes, DefaultOptions());

            Assert.Equal(2, result.CatchmentHydrographs.Count);
            Assert.True(result.CatchmentHydrographs[0].Hydrograph.PeakFlowCfs > 0);
            Assert.True(result.CatchmentHydrographs[1].Hydrograph.PeakFlowCfs >
                result.CatchmentHydrographs[0].Hydrograph.PeakFlowCfs);
        }
    }
}