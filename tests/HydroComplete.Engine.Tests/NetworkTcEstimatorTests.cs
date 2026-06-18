using System.Collections.Generic;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class NetworkTcEstimatorTests
    {
        private static NetworkTcPipe Link(
            string key, string us, string ds, double lengthFt, double slope,
            double diameterFt = 2.0, string network = "NET")
        {
            return new NetworkTcPipe
            {
                PipeKey = key,
                NetworkName = network,
                UpstreamStructureId = us,
                DownstreamStructureId = ds,
                LengthFt = lengthFt,
                Segment = new PipeSegment
                {
                    Name = key,
                    DiameterFt = diameterFt,
                    Slope = slope,
                    ManningN = 0.013,
                    LengthFt = lengthFt,
                },
            };
        }

        [Fact]
        public void LongestPath_YBranch_PicksLongerArm()
        {
            // S1 -P1(400ft)-> S3 -P3(200ft)-> S4
            // S2 -P2(1000ft)-> S3
            var pipes = new List<NetworkTcPipe>
            {
                Link("P1", "S1", "S3", 400.0, 0.01),
                Link("P2", "S2", "S3", 1000.0, 0.01),
                Link("P3", "S3", "S4", 200.0, 0.01),
            };

            NetworkTcResult? result = NetworkTcEstimator.EstimateNetworkLongestPath(
                pipes, NetworkTcMethod.TravelTime);

            Assert.NotNull(result);
            Assert.Equal(1200.0, result.LongestPathLengthFt, 1);
            Assert.Equal(new[] { "P2", "P3" }, result.LongestPathPipeKeys);
        }

        [Fact]
        public void Kirpich_OnLongestPath_MatchesHandCalc()
        {
            var pipes = new List<NetworkTcPipe>
            {
                Link("P1", "S1", "S2", 1000.0, 0.01),
            };

            NetworkTcResult? result = NetworkTcEstimator.EstimateNetworkLongestPath(
                pipes, NetworkTcMethod.Kirpich);

            Assert.NotNull(result);
            Assert.Equal(9.37, result.TcMinutes, 1);
            Assert.Equal(NetworkTcMethod.Kirpich, result.Method);
        }

        [Fact]
        public void TravelTime_SumsLengthOverVelocity()
        {
            var pipes = new List<NetworkTcPipe>
            {
                new NetworkTcPipe
                {
                    PipeKey = "P1",
                    NetworkName = "NET",
                    UpstreamStructureId = "S1",
                    DownstreamStructureId = "S2",
                    LengthFt = 1200.0,
                    Segment = new PipeSegment
                    {
                        Name = "P1",
                        DiameterFt = 2.0,
                        Slope = 0.0,
                        ManningN = 0.013,
                    },
                },
            };

            // slope = 0 -> default velocity 3 fps -> 1200/3/60 = 6.667 min
            NetworkTcResult? result = NetworkTcEstimator.EstimateNetworkLongestPath(
                pipes, NetworkTcMethod.TravelTime);

            Assert.NotNull(result);
            Assert.Equal(6.667, result.TcMinutes, 2);
        }

        [Fact]
        public void EstimateSystemTc_UsesMaxAcrossNetworks()
        {
            var pipes = new List<NetworkTcPipe>
            {
                Link("A1", "H1", "O1", 500.0, 0.01, network: "SHORT"),
                Link("B1", "H2", "O2", 1000.0, 0.01, network: "LONG"),
            };

            double systemTc = NetworkTcEstimator.EstimateSystemTc(pipes, NetworkTcMethod.Kirpich);

            Assert.Equal(9.37, systemTc, 1);
        }

        [Fact]
        public void EmptyPipes_ReturnsZero()
        {
            Assert.Equal(0.0, NetworkTcEstimator.EstimateSystemTc(new List<NetworkTcPipe>()));
        }

        [Fact]
        public void Kirpich_ZeroSlope_FallsBackToTravelTime()
        {
            var pipes = new List<NetworkTcPipe>
            {
                new NetworkTcPipe
                {
                    PipeKey = "P1",
                    NetworkName = "NET",
                    UpstreamStructureId = "S1",
                    DownstreamStructureId = "S2",
                    LengthFt = 600.0,
                    Segment = new PipeSegment
                    {
                        Name = "P1",
                        DiameterFt = 2.0,
                        Slope = 0.0,
                        ManningN = 0.013,
                    },
                },
            };

            NetworkTcResult? result = NetworkTcEstimator.EstimateNetworkLongestPath(
                pipes, NetworkTcMethod.Kirpich);

            Assert.NotNull(result);
            Assert.Equal(NetworkTcMethod.TravelTime, result.Method);
            Assert.Equal(3.333, result.TcMinutes, 2); // 600/3/60
        }
    }
}