using System.Collections.Generic;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class CatchmentFlowRouterTests
    {
        // Y-branch: C1 -> S1 -P1-> S3 -P3-> S4
        //           C2 -> S2 -P2-> S3
        [Fact]
        public void Route_YBranch_AccumulatesAtJunction()
        {
            var idf = new IdfCurve(100.0, 10.0, 0.8);

            var catchments = new List<Catchment>
            {
                new Catchment
                {
                    Name = "C1",
                    AreaAcres = 1.0,
                    RunoffC = 0.5,
                    TcMinutes = 10.0,
                    OutfallStructureId = "S1",
                },
                new Catchment
                {
                    Name = "C2",
                    AreaAcres = 2.0,
                    RunoffC = 0.6,
                    TcMinutes = 12.0,
                    OutfallStructureId = "S2",
                },
            };

            var pipes = new List<NetworkPipeLink>
            {
                new NetworkPipeLink
                {
                    PipeKey = "P1",
                    NetworkName = "NET",
                    PipeName = "P1",
                    UpstreamStructureId = "S1",
                    DownstreamStructureId = "S3",
                },
                new NetworkPipeLink
                {
                    PipeKey = "P2",
                    NetworkName = "NET",
                    PipeName = "P2",
                    UpstreamStructureId = "S2",
                    DownstreamStructureId = "S3",
                },
                new NetworkPipeLink
                {
                    PipeKey = "P3",
                    NetworkName = "NET",
                    PipeName = "P3",
                    UpstreamStructureId = "S3",
                    DownstreamStructureId = "S4",
                },
            };

            var result = CatchmentFlowRouter.Route(catchments, pipes, idf);

            double q1 = Rational.Peak(catchments[0], idf).PeakFlowCfs;
            double q2 = Rational.Peak(catchments[1], idf).PeakFlowCfs;

            Assert.Equal(CatchmentAssignmentMethod.OutletStructure, result.AssignmentMethod);
            Assert.Equal(q1, result.PipeFlowCfs["P1"], 2);
            Assert.Equal(q2, result.PipeFlowCfs["P2"], 2);
            Assert.Equal(q1 + q2, result.PipeFlowCfs["P3"], 2);
            Assert.Equal(q1 + q2, result.TotalPeakCfs, 2);
        }

        // Unequal-length branches into one confluence — the case a plain BFS undercounts.
        //   C1 -> H1 -P1-> A -P2-> C        (long branch: 2 hops to the junction)
        //   C2 -> H2 -P3-> C                (short branch: 1 hop)
        //                  C  -P4-> OUT
        // P4 must carry q1 + q2; a non-topological walk reaches C via the short
        // branch and locks P4 to q2 only.
        [Fact]
        public void Route_UnequalBranchLengths_TrunkCarriesFullSum()
        {
            var idf = new IdfCurve(100.0, 10.0, 0.8);

            var catchments = new List<Catchment>
            {
                new Catchment { Name = "C1", AreaAcres = 1.0, RunoffC = 0.5, TcMinutes = 10.0, OutfallStructureId = "H1" },
                new Catchment { Name = "C2", AreaAcres = 2.0, RunoffC = 0.6, TcMinutes = 12.0, OutfallStructureId = "H2" },
            };

            var pipes = new List<NetworkPipeLink>
            {
                new NetworkPipeLink { PipeKey = "P1", NetworkName = "NET", PipeName = "P1", UpstreamStructureId = "H1", DownstreamStructureId = "A" },
                new NetworkPipeLink { PipeKey = "P2", NetworkName = "NET", PipeName = "P2", UpstreamStructureId = "A",  DownstreamStructureId = "C" },
                new NetworkPipeLink { PipeKey = "P3", NetworkName = "NET", PipeName = "P3", UpstreamStructureId = "H2", DownstreamStructureId = "C" },
                new NetworkPipeLink { PipeKey = "P4", NetworkName = "NET", PipeName = "P4", UpstreamStructureId = "C",  DownstreamStructureId = "OUT" },
            };

            var result = CatchmentFlowRouter.Route(catchments, pipes, idf);

            double q1 = Rational.Peak(catchments[0], idf).PeakFlowCfs;
            double q2 = Rational.Peak(catchments[1], idf).PeakFlowCfs;

            Assert.Equal(q1, result.PipeFlowCfs["P1"], 2);
            Assert.Equal(q1, result.PipeFlowCfs["P2"], 2);
            Assert.Equal(q2, result.PipeFlowCfs["P3"], 2);
            Assert.Equal(q1 + q2, result.PipeFlowCfs["P4"], 2);
        }

        // A pipe whose downstream end has no structure (outfall) must not throw.
        [Fact]
        public void Route_UnconnectedOutfallEnd_DoesNotThrow()
        {
            var idf = new IdfCurve(100.0, 10.0, 0.8);
            var catchments = new List<Catchment>
            {
                new Catchment { Name = "C1", AreaAcres = 1.0, RunoffC = 0.5, TcMinutes = 10.0, OutfallStructureId = "S1" },
            };
            var pipes = new List<NetworkPipeLink>
            {
                new NetworkPipeLink { PipeKey = "P1", NetworkName = "NET", PipeName = "P1", UpstreamStructureId = "S1", DownstreamStructureId = "" },
            };

            var result = CatchmentFlowRouter.Route(catchments, pipes, idf);

            double q1 = Rational.Peak(catchments[0], idf).PeakFlowCfs;
            Assert.Equal(q1, result.PipeFlowCfs["P1"], 2);
        }

        [Fact]
        public void Route_NoOutlets_AreaWeightsToHeadwaters()
        {
            var idf = new IdfCurve(100.0, 10.0, 0.8);

            var catchments = new List<Catchment>
            {
                new Catchment { Name = "A", AreaAcres = 1.0, RunoffC = 0.5, TcMinutes = 10.0 },
                new Catchment { Name = "B", AreaAcres = 3.0, RunoffC = 0.5, TcMinutes = 10.0 },
            };

            var pipes = new List<NetworkPipeLink>
            {
                new NetworkPipeLink
                {
                    PipeKey = "P1",
                    NetworkName = "NET",
                    PipeName = "P1",
                    UpstreamStructureId = "HW1",
                    DownstreamStructureId = "OUT",
                },
            };

            var result = CatchmentFlowRouter.Route(catchments, pipes, idf);

            Assert.Equal(CatchmentAssignmentMethod.AreaWeightedHeadwater, result.AssignmentMethod);
            double total = Rational.Peak(catchments[0], idf).PeakFlowCfs
                + Rational.Peak(catchments[1], idf).PeakFlowCfs;
            Assert.Equal(total, result.PipeFlowCfs["P1"], 2);
        }

        [Fact]
        public void Route_StructureNameMatch_AssignsOutlet()
        {
            var idf = new IdfCurve(80.0, 8.0, 0.75);

            var catchments = new List<Catchment>
            {
                new Catchment
                {
                    Name = "North",
                    AreaAcres = 0.5,
                    RunoffC = 0.7,
                    TcMinutes = 8.0,
                    OutfallStructureName = "MH-101",
                },
            };

            var pipes = new List<NetworkPipeLink>
            {
                new NetworkPipeLink
                {
                    PipeKey = "P1",
                    NetworkName = "NET",
                    PipeName = "P1",
                    UpstreamStructureId = "H101",
                    DownstreamStructureId = "OUT",
                },
            };

            var names = new Dictionary<string, string> { ["H101"] = "MH-101" };
            var result = CatchmentFlowRouter.Route(catchments, pipes, idf, names);

            double q = Rational.Peak(catchments[0], idf).PeakFlowCfs;
            Assert.Equal(q, result.PipeFlowCfs["P1"], 2);
            Assert.Equal("H101", result.CatchmentFlows[0].AssignedStructureId);
        }

        [Fact]
        public void Peak_PerCatchment_UsesCatchmentTc()
        {
            var idf = new IdfCurve(100.0, 10.0, 0.8);
            var cm = new Catchment { RunoffC = 0.5, AreaAcres = 1.0, TcMinutes = 5.0 };

            var at5 = Rational.Peak(cm, idf);
            cm.TcMinutes = 30.0;
            var at30 = Rational.Peak(cm, idf);

            Assert.NotEqual(at5.IntensityInHr, at30.IntensityInHr);
            Assert.NotEqual(at5.PeakFlowCfs, at30.PeakFlowCfs);
        }
    }
}