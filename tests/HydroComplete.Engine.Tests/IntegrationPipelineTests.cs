using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    /// <summary>
    /// End-to-end engine pipeline: JSON network → CatchmentFlowRouter → HGL profile.
    /// No Civil 3D / AutoCAD types.
    /// </summary>
    public class IntegrationPipelineTests
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        // Topology (5 pipes):
        //   C1 -> S1 -P1-> S3 -P3-> S5 -P4-> S6
        //   C2 -> S2 -P2-> S3
        //   C3 -> S4 -P5-> S5

        [Fact]
        public void SyntheticFivePipeNetwork_EndToEnd_RoutesFlowsAndComputesDescendingHgl()
        {
            SyntheticNetworkDocument doc = LoadFixture("synthetic_5pipe_network.json");
            IdfCurve idf = doc.ToIdfCurve();

            var catchments = doc.ToCatchments();
            var links = doc.ToPipeLinks();
            var route = CatchmentFlowRouter.Route(catchments, links, idf);

            double q1 = Rational.Peak(catchments[0], idf).PeakFlowCfs;
            double q2 = Rational.Peak(catchments[1], idf).PeakFlowCfs;
            double q3 = Rational.Peak(catchments[2], idf).PeakFlowCfs;
            double total = q1 + q2 + q3;

            Assert.Equal(CatchmentAssignmentMethod.OutletStructure, route.AssignmentMethod);
            Assert.Equal(3, route.CatchmentFlows.Count);
            Assert.Equal(total, route.TotalPeakCfs, 2);
            Assert.Equal(q1, route.PipeFlowCfs["P1"], 2);
            Assert.Equal(q2, route.PipeFlowCfs["P2"], 2);
            Assert.Equal(q3, route.PipeFlowCfs["P5"], 2);
            Assert.Equal(q1 + q2, route.PipeFlowCfs["P3"], 2);
            Assert.Equal(total, route.PipeFlowCfs["P4"], 2);
            Assert.Equal(total, route.StructureInflowCfs["S6"], 2);

            List<SyntheticPipeRecord> ordered = OrderPipesDownstream(doc.Pipes);
            Assert.Equal(5, ordered.Count);

            List<NetworkReach> reaches = BuildReaches(ordered, route.PipeFlowCfs, includeJunctionLosses: true);
            Assert.Equal(5, reaches.Count);
            Assert.Equal(q1, reaches.First(r => r.Name == "P1").FlowCfs, 2);
            Assert.Equal(total, reaches.First(r => r.Name == "P4").FlowCfs, 2);

            double startHgl = doc.StartHglFt;
            var hglOptions = new HglProfileOptions
            {
                IncludeJunctionLosses = true,
                IncludeExitLoss = true,
            };

            List<HglProfilePoint> profile = Hgl.SteadyNetworkHglProfile(reaches, startHgl, hglOptions);

            Assert.Equal(5, profile.Count);
            Assert.True(startHgl > profile[0].HglFt);
            for (int i = 1; i < profile.Count; i++)
            {
                Assert.True(profile[i - 1].HglFt > profile[i].HglFt,
                    $"HGL should drop downstream at reach {i}");
                Assert.True(profile[i].CumLengthFt > profile[i - 1].CumLengthFt);
            }

            Assert.True(profile.All(p => p.HfFt > 0));
            Assert.True(profile.Sum(p => p.HmFt) > 0);
            Assert.Equal(ordered.Sum(p => p.LengthFt), profile[^1].CumLengthFt, 1);
            Assert.DoesNotContain(profile, p => p.FlowSurcharged);
        }

        [Fact]
        public void SyntheticFivePipeNetwork_JsonFixture_HasFivePipesAndThreeCatchments()
        {
            SyntheticNetworkDocument doc = LoadFixture("synthetic_5pipe_network.json");

            Assert.Equal("SYN-5", doc.NetworkName);
            Assert.Equal(5, doc.Pipes.Count);
            Assert.Equal(3, doc.Catchments.Count);
            Assert.Equal(105.0, doc.StartHglFt);
            Assert.Equal(100.0, doc.Idf.A);
        }

        private static SyntheticNetworkDocument LoadFixture(string fileName)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
            string json = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize<SyntheticNetworkDocument>(json, JsonOptions);
            Assert.NotNull(doc);
            return doc!;
        }

        private static List<SyntheticPipeRecord> OrderPipesDownstream(IReadOnlyList<SyntheticPipeRecord> pipes)
        {
            var byUpstream = new Dictionary<string, List<SyntheticPipeRecord>>(StringComparer.OrdinalIgnoreCase);
            var downstreamStructs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (SyntheticPipeRecord pipe in pipes)
            {
                downstreamStructs.Add(pipe.DownstreamStructureId);
                if (!byUpstream.TryGetValue(pipe.UpstreamStructureId, out List<SyntheticPipeRecord>? list))
                {
                    list = new List<SyntheticPipeRecord>();
                    byUpstream[pipe.UpstreamStructureId] = list;
                }
                list.Add(pipe);
            }

            var headwaters = byUpstream.Keys
                .Where(id => !downstreamStructs.Contains(id))
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var ordered = new List<SyntheticPipeRecord>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>(headwaters);

            while (queue.Count > 0)
            {
                string structId = queue.Dequeue();
                if (!byUpstream.TryGetValue(structId, out List<SyntheticPipeRecord>? outgoing))
                    continue;

                foreach (SyntheticPipeRecord pipe in outgoing.OrderBy(p => p.PipeName, StringComparer.OrdinalIgnoreCase))
                {
                    if (!visited.Add(pipe.PipeKey))
                        continue;

                    ordered.Add(pipe);
                    if (byUpstream.ContainsKey(pipe.DownstreamStructureId))
                        queue.Enqueue(pipe.DownstreamStructureId);
                }
            }

            foreach (SyntheticPipeRecord pipe in pipes)
            {
                if (!visited.Contains(pipe.PipeKey))
                    ordered.Add(pipe);
            }

            return ordered;
        }

        private static List<NetworkReach> BuildReaches(
            IReadOnlyList<SyntheticPipeRecord> orderedPipes,
            IReadOnlyDictionary<string, double> pipeFlowCfs,
            bool includeJunctionLosses)
        {
            var reaches = new List<NetworkReach>(orderedPipes.Count);

            for (int i = 0; i < orderedPipes.Count; i++)
            {
                SyntheticPipeRecord pipe = orderedPipes[i];
                double designQ = pipeFlowCfs[pipe.PipeKey];
                var segment = pipe.ToPipeSegment();
                NetworkReach reach = ReachFactory.FromNormalDepth(
                    segment, designQ, pipe.LengthFt, pipe.PipeName);

                if (includeJunctionLosses && i < orderedPipes.Count - 1)
                {
                    SyntheticPipeRecord next = orderedPipes[i + 1];
                    if (string.Equals(
                            pipe.DownstreamStructureId,
                            next.UpstreamStructureId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        reach.JunctionLossK = Hec22.DefaultManholeK;
                    }
                }

                reaches.Add(reach);
            }

            return reaches;
        }

        private sealed class SyntheticNetworkDocument
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

            public List<NetworkPipeLink> ToPipeLinks()
            {
                return Pipes.Select(p => new NetworkPipeLink
                {
                    PipeKey = p.PipeKey,
                    NetworkName = NetworkName,
                    PipeName = p.PipeName,
                    UpstreamStructureId = p.UpstreamStructureId,
                    DownstreamStructureId = p.DownstreamStructureId,
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