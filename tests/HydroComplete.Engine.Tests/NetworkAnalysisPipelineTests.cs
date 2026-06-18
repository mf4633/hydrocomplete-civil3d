using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class NetworkAnalysisPipelineTests
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        [Fact]
        public void SyntheticFivePipeNetwork_RoutesFlowsToTrunkPipes()
        {
            SyntheticFixture fixture = LoadFixture();
            NetworkAnalysisResult result = RunFixture(fixture);

            Assert.NotNull(result.Routing);
            Assert.Equal(CatchmentAssignmentMethod.OutletStructure, result.Routing.AssignmentMethod);
            Assert.Equal(3, result.Hydrology.Count);

            double q1 = result.Hydrology[0].Rational.PeakFlowCfs;
            double q2 = result.Hydrology[1].Rational.PeakFlowCfs;
            double q3 = result.Hydrology[2].Rational.PeakFlowCfs;

            Assert.Equal(q1, result.Routing.PipeFlowCfs["P1"], 2);
            Assert.Equal(q2, result.Routing.PipeFlowCfs["P2"], 2);
            Assert.Equal(q3, result.Routing.PipeFlowCfs["P5"], 2);
            Assert.Equal(q1 + q2, result.Routing.PipeFlowCfs["P3"], 2);
            Assert.Equal(q1 + q2 + q3, result.Routing.PipeFlowCfs["P4"], 2);
        }

        [Fact]
        public void SyntheticFivePipeNetwork_ComputesRationalAndScsPerCatchment()
        {
            SyntheticFixture fixture = LoadFixture();
            NetworkAnalysisResult result = RunFixture(fixture);

            Assert.Equal(3, result.Hydrology.Count);
            foreach (CatchmentHydrologyResult hydro in result.Hydrology)
            {
                Assert.True(hydro.Rational.PeakFlowCfs > 0);
                Assert.True(hydro.Scs.RunoffDepthInches >= 0);
                Assert.True(hydro.Rational.Steps.Count > 0);
                Assert.True(hydro.Scs.Steps.Count > 0);
            }

            double totalRational = result.Hydrology.Sum(h => h.Rational.PeakFlowCfs);
            Assert.Equal(totalRational, result.Routing!.TotalPeakCfs, 2);
        }

        [Fact]
        public void SyntheticFivePipeNetwork_CapacityChecksAllFivePipes()
        {
            SyntheticFixture fixture = LoadFixture();
            NetworkAnalysisResult result = RunFixture(fixture);

            Assert.Equal(5, result.Capacity.Count);
            foreach (PipeCapacityAnalysisResult row in result.Capacity)
            {
                Assert.True(row.DesignFlowCfs > 0);
                Assert.True(row.Capacity.FullFlowCfs > 0);
                Assert.True(row.Capacity.Steps.Count > 0);
                Assert.True(row.NormalDepth.Steps.Count > 0);
            }
        }

        [Fact]
        public void SyntheticFivePipeNetwork_HglProfileDescendsDownstream()
        {
            SyntheticFixture fixture = LoadFixture();
            NetworkAnalysisResult result = RunFixture(fixture);

            Assert.Single(result.HglNetworks);
            NetworkHglResult net = result.HglNetworks[0];
            Assert.Equal(5, net.Profile.Count);

            for (int i = 1; i < net.Profile.Count; i++)
            {
                Assert.True(net.Profile[i - 1].HglFt > net.Profile[i].HglFt,
                    $"HGL should drop downstream at reach {i}");
            }

            Assert.True(net.Profile.All(p => p.HfFt > 0));
        }

        [Fact]
        public void SyntheticFivePipeNetwork_ComputesRusleSedimentPerCatchment()
        {
            SyntheticFixture fixture = LoadFixture();
            NetworkAnalysisResult result = RunFixture(fixture);

            Assert.Equal(3, result.Sediment.Count);
            Assert.All(result.Sediment, r =>
            {
                Assert.True(r.SoilLossTonsPerAcYr > 0);
                Assert.False(string.IsNullOrEmpty(r.RiskLevel));
                Assert.True(r.Steps.Count > 0);
            });
        }

        [Fact]
        public void SyntheticFivePipeNetwork_ComputesWqvAndPlaceholderTreatmentTrain()
        {
            SyntheticFixture fixture = LoadFixture();
            NetworkAnalysisResult result = RunFixture(fixture);

            Assert.NotNull(result.Wqv);
            Assert.True(result.Wqv.WqvCf > 0);
            Assert.True(result.Wqv.Steps.Count > 0);

            Assert.NotNull(result.TreatmentTrain);
            Assert.True(result.TreatmentTrain.ChainLength > 0);
            Assert.True(result.TreatmentTrain.InitialLoadsLbs[Pollutant.Tss] > 0);
            Assert.True(result.TreatmentTrain.OverallRemovalEfficiency[Pollutant.Tss] > 0);
        }

        [Fact]
        public void SyntheticFivePipeNetwork_ProducesComplianceReport()
        {
            SyntheticFixture fixture = LoadFixture();
            NetworkAnalysisResult result = RunFixture(fixture);

            Assert.NotNull(result.Compliance);
            Assert.Equal("NC", result.StateCode);
            Assert.Equal("North Carolina", result.Compliance.State);
            Assert.NotEmpty(result.Compliance.Criteria);
            Assert.True(result.Compliance.Steps.Count > 0);
        }

        [Fact]
        public void SyntheticFivePipeNetwork_OverallPassReflectsSurchargeAndCompliance()
        {
            SyntheticFixture fixture = LoadFixture();
            NetworkAnalysisResult result = RunFixture(fixture);

            bool anySurcharged = result.Capacity.Any(c => c.Surcharged);
            int designErrors = result.DesignReview.Count(f => f.Severity == DesignFindingSeverity.Error);

            bool expected = (result.Compliance?.OverallPass ?? false)
                            && designErrors == 0
                            && !anySurcharged;

            Assert.Equal(expected, result.OverallPass);
            Assert.Contains(result.Steps, s => s.Label == "overall_pass");
        }

        [Fact]
        public void SyntheticFivePipeNetwork_DesignReviewFlagsNoErrorsOnHealthyFixture()
        {
            SyntheticFixture fixture = LoadFixture();
            NetworkAnalysisResult result = RunFixture(fixture);

            int errors = result.DesignReview.Count(f => f.Severity == DesignFindingSeverity.Error);
            Assert.Equal(0, errors);
        }

        [Fact]
        public void Run_RequiresAtLeastOneCatchment()
        {
            var input = new NetworkAnalysisInput
            {
                Catchments = Array.Empty<Catchment>(),
                Pipes = Array.Empty<NetworkAnalysisPipe>(),
                Idf = new IdfCurve(100, 10, 0.8),
            };

            Assert.Throws<ArgumentException>(() => NetworkAnalysisPipeline.Run(input));
        }

        private static NetworkAnalysisResult RunFixture(SyntheticFixture fixture)
        {
            var input = new NetworkAnalysisInput
            {
                Catchments = fixture.ToCatchments(),
                Pipes = fixture.ToAnalysisPipes(),
                StateCode = "NC",
                Idf = fixture.ToIdfCurve(),
                ScsDesignRainfallInches = 4.5,
            };

            return NetworkAnalysisPipeline.Run(input);
        }

        private static SyntheticFixture LoadFixture()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "synthetic_5pipe_network.json");
            string json = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize<SyntheticFixture>(json, JsonOptions);
            Assert.NotNull(doc);
            return doc!;
        }

        private sealed class SyntheticFixture
        {
            public string NetworkName { get; set; } = "";

            public SyntheticIdfRecord Idf { get; set; } = new SyntheticIdfRecord();

            public double StartHglFt { get; set; }

            public List<SyntheticCatchmentRecord> Catchments { get; set; } =
                new List<SyntheticCatchmentRecord>();

            public List<SyntheticPipeRecord> Pipes { get; set; } =
                new List<SyntheticPipeRecord>();

            public IdfCurve ToIdfCurve() => new IdfCurve(Idf.A, Idf.B, Idf.C);

            public List<Catchment> ToCatchments()
            {
                return Catchments.Select(c => new Catchment
                {
                    Name = c.Name,
                    AreaAcres = c.AreaAcres,
                    RunoffC = c.RunoffC,
                    TcMinutes = c.TcMinutes,
                    OutfallStructureId = c.OutfallStructureId,
                    OutfallStructureName = c.OutfallStructureName,
                }).ToList();
            }

            public List<NetworkAnalysisPipe> ToAnalysisPipes()
            {
                return Pipes.Select(p => new NetworkAnalysisPipe
                {
                    PipeKey = p.PipeKey,
                    NetworkName = NetworkName,
                    PipeName = p.PipeName,
                    Link = new NetworkPipeLink
                    {
                        PipeKey = p.PipeKey,
                        NetworkName = NetworkName,
                        PipeName = p.PipeName,
                        UpstreamStructureId = p.UpstreamStructureId,
                        DownstreamStructureId = p.DownstreamStructureId,
                    },
                    Segment = p.ToPipeSegment(),
                    UpstreamNodeId = p.UpstreamStructureId,
                    DownstreamNodeId = p.DownstreamStructureId,
                    LengthFt = p.LengthFt,
                    UpstreamInvertFt = p.StartInvertFt,
                    DownstreamInvertFt = p.EndInvertFt,
                }).ToList();
            }
        }

        private sealed class SyntheticIdfRecord
        {
            public double A { get; set; }
            public double B { get; set; }
            public double C { get; set; }
        }

        private sealed class SyntheticCatchmentRecord
        {
            public string Name { get; set; } = "";
            public double AreaAcres { get; set; }
            public double RunoffC { get; set; }
            public double TcMinutes { get; set; }
            public string? OutfallStructureId { get; set; }
            public string? OutfallStructureName { get; set; }
        }

        private sealed class SyntheticPipeRecord
        {
            public string PipeKey { get; set; } = "";
            public string PipeName { get; set; } = "";
            public string UpstreamStructureId { get; set; } = "";
            public string DownstreamStructureId { get; set; } = "";
            public double DiameterFt { get; set; }
            public double LengthFt { get; set; }
            public double ManningN { get; set; } = 0.013;
            public double Slope { get; set; }
            public double StartInvertFt { get; set; }
            public double EndInvertFt { get; set; }

            public PipeSegment ToPipeSegment() => new PipeSegment
            {
                Name = PipeName,
                DiameterFt = DiameterFt,
                LengthFt = LengthFt,
                ManningN = ManningN,
                Slope = Slope,
                StartInvertFt = StartInvertFt,
                EndInvertFt = EndInvertFt,
            };
        }
    }
}