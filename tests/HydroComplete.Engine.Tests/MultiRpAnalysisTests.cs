using System;
using System.Collections.Generic;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class FaaTcTests
    {
        [Fact]
        public void Faa_MatchesHandCalc()
        {
            // L=500 ft, S=0.01, HR=0.25 ft -> Tc = 1.8*sqrt(500)/(100*0.01)^(1/3)/0.25^0.3
            var r = TimeOfConcentration.Faa(500.0, 0.01, 0.25);
            Assert.Equal(61.0, r.TcMinutes, 1);
            Assert.Contains("FAA reach", r.Steps[0].Formula);
        }

        [Fact]
        public void Faa_ZeroHydraulicRadius_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                TimeOfConcentration.Faa(500.0, 0.01, 0.0));
        }
    }

    public class MultiRpAnalysisTests
    {
        [Fact]
        public void Analyze_YBranch_CapacityRatioIncreasesWithReturnPeriod()
        {
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

            var links = new List<NetworkPipeLink>
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

            var segment = new PipeSegment
            {
                DiameterFt = 2.0,
                ManningN = 0.013,
                Slope = 0.01,
            };

            var pipes = new List<MultiRpAnalysis.AnalysisPipe>
            {
                MakePipe("P1", "P1", links[0], segment),
                MakePipe("P2", "P2", links[1], segment),
                MakePipe("P3", "P3", links[2], segment),
            };

            var idfByRp = new Dictionary<int, IdfCurve>
            {
                [2] = new IdfCurve(50.0, 10.0, 0.8),
                [10] = new IdfCurve(100.0, 10.0, 0.8),
                [25] = new IdfCurve(130.0, 10.0, 0.8),
                [100] = new IdfCurve(180.0, 10.0, 0.8),
            };

            MultiRpAnalysis.MultiRpResult result = MultiRpAnalysis.Analyze(
                catchments, pipes, idfByRp);

            Assert.Equal(4, result.ReturnPeriods.Count);
            Assert.Equal(3, result.Pipes.Count);

            MultiRpAnalysis.PipeMultiRpRow trunk = result.Pipes.Find(p => p.PipeKey == "P3")!;
            double q2 = trunk.ByReturnPeriod[2].PeakFlowCfs;
            double q10 = trunk.ByReturnPeriod[10].PeakFlowCfs;
            double q25 = trunk.ByReturnPeriod[25].PeakFlowCfs;
            double q100 = trunk.ByReturnPeriod[100].PeakFlowCfs;

            Assert.True(q2 < q10);
            Assert.True(q10 < q25);
            Assert.True(q25 < q100);

            double ratio2 = trunk.ByReturnPeriod[2].CapacityRatio;
            double ratio100 = trunk.ByReturnPeriod[100].CapacityRatio;
            Assert.True(ratio2 < ratio100);
            Assert.Equal(q100 / trunk.ByReturnPeriod[100].QFullCfs, ratio100, 3);
        }

        [Fact]
        public void CurvesFromPreset_ContainsStandardReturnPeriods()
        {
            Atlas14Presets.Preset preset = Atlas14Presets.Find("charlotte-nc")!;
            Dictionary<int, IdfCurve> curves = MultiRpAnalysis.CurvesFromPreset(preset);

            foreach (int rp in MultiRpAnalysis.StandardReturnPeriods)
            {
                Assert.True(curves.ContainsKey(rp));
                Assert.True(curves[rp].Intensity(10.0).IntensityInHr > 0);
            }
        }

        private static MultiRpAnalysis.AnalysisPipe MakePipe(
            string key,
            string name,
            NetworkPipeLink link,
            PipeSegment segment)
        {
            return new MultiRpAnalysis.AnalysisPipe
            {
                PipeKey = key,
                NetworkName = "NET",
                PipeName = name,
                Link = link,
                Segment = segment,
            };
        }
    }
}