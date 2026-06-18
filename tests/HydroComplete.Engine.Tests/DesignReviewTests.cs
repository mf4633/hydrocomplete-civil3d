using System.Collections.Generic;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class DesignReviewTests
    {
        private static bool Has(IReadOnlyList<DesignFinding> findings, DesignFindingSeverity sev, string needle)
        {
            foreach (DesignFinding f in findings)
            {
                if (f.Severity == sev && f.Message.Contains(needle))
                    return true;
            }
            return false;
        }

        [Fact]
        public void FlagsAdverseSlope()
        {
            var pipes = new List<ReviewPipeInput>
            {
                new ReviewPipeInput
                {
                    Id = "P1",
                    UpstreamNodeId = "N1",
                    DownstreamNodeId = "OUT",
                    DiameterFt = 2.0,
                    Slope = -0.01,
                    DesignFlowCfs = 5.0,
                    FullCapacityCfs = 22.0,
                    VelocityFps = 3.0,
                    UpstreamInvertFt = 98.0,
                    DownstreamInvertFt = 100.0,
                },
            };

            var findings = DesignReview.ReviewNetwork(pipes, nodes: null);
            Assert.True(Has(findings, DesignFindingSeverity.Error, "adverse slope"));
        }

        [Fact]
        public void FlagsSurcharge()
        {
            var pipes = new List<ReviewPipeInput>
            {
                new ReviewPipeInput
                {
                    Id = "P1",
                    UpstreamNodeId = "N1",
                    DownstreamNodeId = "OUT",
                    DiameterFt = 2.0,
                    Slope = 0.01,
                    DesignFlowCfs = 50.0,
                    FullCapacityCfs = 22.0,
                    VelocityFps = 8.0,
                    Surcharged = true,
                    UpstreamInvertFt = 100.0,
                    DownstreamInvertFt = 98.0,
                },
            };

            var findings = DesignReview.ReviewNetwork(pipes, nodes: null);
            Assert.True(Has(findings, DesignFindingSeverity.Error, "surcharged"));
        }

        [Fact]
        public void FlagsPipeSizeDecreaseDownstream()
        {
            var pipes = new List<ReviewPipeInput>
            {
                new ReviewPipeInput
                {
                    Id = "P1",
                    UpstreamNodeId = "N1",
                    DownstreamNodeId = "N2",
                    DiameterFt = 2.0,
                    Slope = 0.005,
                    DesignFlowCfs = 5.0,
                    FullCapacityCfs = 30.0,
                    VelocityFps = 3.0,
                },
                new ReviewPipeInput
                {
                    Id = "P2",
                    UpstreamNodeId = "N2",
                    DownstreamNodeId = "OUT",
                    DiameterFt = 1.5,
                    Slope = 0.005,
                    DesignFlowCfs = 5.0,
                    FullCapacityCfs = 20.0,
                    VelocityFps = 3.5,
                },
            };

            var findings = DesignReview.ReviewNetwork(pipes, nodes: null);
            Assert.True(Has(findings, DesignFindingSeverity.Warning, "smaller than upstream pipe"));
        }

        [Fact]
        public void FlagsInsufficientCover()
        {
            var pipes = new List<ReviewPipeInput>
            {
                new ReviewPipeInput
                {
                    Id = "P1",
                    UpstreamNodeId = "N1",
                    DownstreamNodeId = "OUT",
                    DiameterFt = 1.5,
                    Slope = 0.035,
                    DesignFlowCfs = 2.0,
                    FullCapacityCfs = 25.0,
                    VelocityFps = 2.5,
                    UpstreamInvertFt = 99.5,
                    DownstreamInvertFt = 92.5,
                },
            };

            var nodes = new Dictionary<string, ReviewNodeInput>
            {
                ["N1"] = new ReviewNodeInput { Id = "N1", RimFt = 100.0, InvertFt = 99.5 },
            };

            var findings = DesignReview.ReviewNetwork(pipes, nodes);
            Assert.True(Has(findings, DesignFindingSeverity.Warning, "cover at upstream node N1"));
        }

        [Fact]
        public void WellFormedNetwork_HasNoErrors()
        {
            var pipes = new List<ReviewPipeInput>
            {
                new ReviewPipeInput
                {
                    Id = "P1",
                    UpstreamNodeId = "N1",
                    DownstreamNodeId = "N2",
                    DiameterFt = 1.5,
                    Slope = 0.005,
                    DesignFlowCfs = 3.0,
                    FullCapacityCfs = 30.0,
                    VelocityFps = 3.0,
                    UpstreamInvertFt = 100.0,
                    DownstreamInvertFt = 98.5,
                },
                new ReviewPipeInput
                {
                    Id = "P2",
                    UpstreamNodeId = "N2",
                    DownstreamNodeId = "OUT",
                    DiameterFt = 1.75,
                    Slope = 0.006,
                    DesignFlowCfs = 3.0,
                    FullCapacityCfs = 35.0,
                    VelocityFps = 3.5,
                    UpstreamInvertFt = 98.5,
                    DownstreamInvertFt = 97.0,
                },
            };

            var nodes = new Dictionary<string, ReviewNodeInput>
            {
                ["N1"] = new ReviewNodeInput { Id = "N1", RimFt = 110.0, InvertFt = 100.0 },
                ["N2"] = new ReviewNodeInput { Id = "N2", RimFt = 109.0, InvertFt = 98.5 },
                ["OUT"] = new ReviewNodeInput { Id = "OUT", RimFt = 108.0, InvertFt = 97.0 },
            };

            var findings = DesignReview.ReviewNetwork(pipes, nodes);
            Assert.DoesNotContain(findings, f => f.Severity == DesignFindingSeverity.Error);
        }
    }
}