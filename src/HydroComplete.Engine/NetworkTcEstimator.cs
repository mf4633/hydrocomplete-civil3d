using System;
using System.Collections.Generic;
using System.Linq;

namespace HydroComplete.Engine
{
    /// <summary>How system Tc is estimated along the longest pipe-network path.</summary>
    public enum NetworkTcMethod
    {
        /// <summary>Sum of reach travel times, Tt = L / V (minutes).</summary>
        TravelTime,

        /// <summary>Kirpich on total path length and average slope.</summary>
        Kirpich,
    }

    /// <summary>One gravity pipe for network-based Tc estimation (topology + hydraulics).</summary>
    public sealed class NetworkTcPipe
    {
        public string PipeKey { get; set; } = "";

        public string NetworkName { get; set; } = "";

        public string UpstreamStructureId { get; set; } = "";

        public string DownstreamStructureId { get; set; } = "";

        /// <summary>Plan or center-to-center length, ft.</summary>
        public double LengthFt { get; set; }

        /// <summary>Circular pipe segment (diameter, slope, Manning n).</summary>
        public PipeSegment Segment { get; set; } = new PipeSegment();
    }

    /// <summary>Estimated Tc along the longest path in one storm network.</summary>
    public sealed class NetworkTcResult : TracedResult
    {
        public string NetworkName { get; set; } = "";

        public NetworkTcMethod Method { get; set; }

        public double TcMinutes { get; set; }

        public double LongestPathLengthFt { get; set; }

        public List<string> LongestPathPipeKeys { get; } = new List<string>();
    }

    /// <summary>
    /// Estimates system time of concentration from the longest downstream path
    /// in each pipe network using Kirpich or summed pipe travel times.
    /// </summary>
    public static class NetworkTcEstimator
    {
        /// <summary>Fallback velocity when Manning capacity cannot be evaluated, ft/s.</summary>
        public const double DefaultVelocityFps = 3.0;

        /// <summary>
        /// Maximum Tc (minutes) across all networks' longest paths, or 0 when no
        /// usable pipes are supplied.
        /// </summary>
        public static double EstimateSystemTc(
            IReadOnlyList<NetworkTcPipe> pipes,
            NetworkTcMethod method = NetworkTcMethod.TravelTime)
        {
            if (pipes == null) throw new ArgumentNullException(nameof(pipes));
            if (pipes.Count == 0) return 0.0;

            double systemTc = 0.0;
            foreach (var group in pipes.GroupBy(p => p.NetworkName, StringComparer.OrdinalIgnoreCase))
            {
                NetworkTcResult? result = EstimateNetworkLongestPath(group.ToList(), method);
                if (result != null && result.TcMinutes > systemTc)
                    systemTc = result.TcMinutes;
            }

            return systemTc;
        }

        /// <summary>Estimates Tc for the longest path within one named network.</summary>
        public static NetworkTcResult? EstimateNetworkLongestPath(
            IReadOnlyList<NetworkTcPipe> pipes,
            NetworkTcMethod method = NetworkTcMethod.TravelTime)
        {
            if (pipes == null) throw new ArgumentNullException(nameof(pipes));
            if (pipes.Count == 0) return null;

            string networkName = pipes[0].NetworkName ?? "";
            var pathPipes = FindLongestPathPipes(pipes);
            if (pathPipes.Count == 0) return null;

            double pathLength = pathPipes.Sum(p => p.LengthFt);
            var result = new NetworkTcResult
            {
                NetworkName = networkName,
                Method = method,
                LongestPathLengthFt = pathLength,
            };
            foreach (NetworkTcPipe pipe in pathPipes)
                result.LongestPathPipeKeys.Add(pipe.PipeKey);

            if (method == NetworkTcMethod.Kirpich)
                ApplyKirpich(pathPipes, pathLength, result);
            else
                ApplyTravelTime(pathPipes, result);

            return result;
        }

        private static void ApplyKirpich(
            IReadOnlyList<NetworkTcPipe> pathPipes,
            double pathLength,
            NetworkTcResult result)
        {
            if (pathLength <= 0)
            {
                result.TcMinutes = 0.0;
                return;
            }

            double dropFt = 0.0;
            foreach (NetworkTcPipe pipe in pathPipes)
            {
                double slope = ResolveSlope(pipe);
                if (slope > 0)
                    dropFt += pipe.LengthFt * slope;
            }

            double avgSlope = dropFt / pathLength;
            if (avgSlope <= 0)
            {
                ApplyTravelTime(pathPipes, result);
                result.Method = NetworkTcMethod.TravelTime;
                result.Steps.Add(new CalcStep("Kirpich", 0, "min", "no positive slope — travel-time fallback"));
                return;
            }

            TimeOfConcentration.TcResult kirpich = TimeOfConcentration.Kirpich(pathLength, avgSlope);
            result.TcMinutes = kirpich.TcMinutes;
            foreach (CalcStep step in kirpich.Steps)
                result.Steps.Add(step);
            result.Steps.Add(new CalcStep("L_path", pathLength, "ft", "longest network path"));
            result.Steps.Add(new CalcStep("S_avg", avgSlope, "ft/ft", "sum(L*S)/L_path"));
        }

        private static void ApplyTravelTime(IReadOnlyList<NetworkTcPipe> pathPipes, NetworkTcResult result)
        {
            var reaches = new List<TimeOfConcentration.TravelReach>(pathPipes.Count);
            foreach (NetworkTcPipe pipe in pathPipes)
            {
                string name = string.IsNullOrEmpty(pipe.Segment.Name) ? pipe.PipeKey : pipe.Segment.Name;
                reaches.Add(new TimeOfConcentration.TravelReach
                {
                    Name = name,
                    LengthFt = pipe.LengthFt,
                    VelocityFps = ResolveVelocityFps(pipe),
                });
            }

            TimeOfConcentration.TcResult travel = TimeOfConcentration.FromReaches(reaches);
            result.TcMinutes = travel.TcMinutes;
            foreach (CalcStep step in travel.Steps)
                result.Steps.Add(step);
        }

        private static double ResolveSlope(NetworkTcPipe pipe)
        {
            if (pipe.Segment.Slope > 0) return pipe.Segment.Slope;
            if (pipe.LengthFt <= 0) return 0.0;

            double drop = pipe.Segment.StartInvertFt - pipe.Segment.EndInvertFt;
            return drop > 0 ? drop / pipe.LengthFt : 0.0;
        }

        private static double ResolveVelocityFps(NetworkTcPipe pipe)
        {
            double slope = ResolveSlope(pipe);
            if (pipe.Segment.DiameterFt > 0 && slope > 0 && pipe.Segment.ManningN > 0)
            {
                try
                {
                    return Manning.Capacity(pipe.Segment).FullVelocityFps;
                }
                catch (ArgumentOutOfRangeException)
                {
                    // fall through to default velocity
                }
            }

            return DefaultVelocityFps;
        }

        private static List<NetworkTcPipe> FindLongestPathPipes(IReadOnlyList<NetworkTcPipe> pipes)
        {
            var outgoing = new Dictionary<string, List<NetworkTcPipe>>(StringComparer.OrdinalIgnoreCase);
            var downstreamStructs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (NetworkTcPipe pipe in pipes)
            {
                if (pipe.LengthFt <= 0) continue;

                string us = pipe.UpstreamStructureId ?? "";
                string ds = pipe.DownstreamStructureId ?? "";
                if (string.IsNullOrEmpty(us) || string.IsNullOrEmpty(ds)) continue;

                downstreamStructs.Add(ds);
                if (!outgoing.TryGetValue(us, out List<NetworkTcPipe>? list))
                {
                    list = new List<NetworkTcPipe>();
                    outgoing[us] = list;
                }
                list.Add(pipe);
            }

            if (outgoing.Count == 0)
                return LongestPathByLength(pipes);

            var headwaters = outgoing.Keys
                .Where(id => !downstreamStructs.Contains(id))
                .ToList();

            if (headwaters.Count == 0)
                return LongestPathByLength(pipes);

            var memo = new Dictionary<string, (double lengthFt, List<NetworkTcPipe> pipes)>(
                StringComparer.OrdinalIgnoreCase);

            double bestLength = 0.0;
            List<NetworkTcPipe> bestPath = new List<NetworkTcPipe>();

            foreach (string head in headwaters)
            {
                (double lengthFt, List<NetworkTcPipe> path) = LongestPathFrom(head, outgoing, memo);
                if (lengthFt > bestLength)
                {
                    bestLength = lengthFt;
                    bestPath = path;
                }
            }

            return bestPath;
        }

        private static (double lengthFt, List<NetworkTcPipe> pipes) LongestPathFrom(
            string structureId,
            Dictionary<string, List<NetworkTcPipe>> outgoing,
            Dictionary<string, (double lengthFt, List<NetworkTcPipe> pipes)> memo)
        {
            if (memo.TryGetValue(structureId, out (double lengthFt, List<NetworkTcPipe> pipes) cached))
                return cached;

            if (!outgoing.TryGetValue(structureId, out List<NetworkTcPipe>? edges))
            {
                memo[structureId] = (0.0, new List<NetworkTcPipe>());
                return memo[structureId];
            }

            double bestLength = 0.0;
            var bestPath = new List<NetworkTcPipe>();

            foreach (NetworkTcPipe pipe in edges)
            {
                (double tailLength, List<NetworkTcPipe> tailPath) =
                    LongestPathFrom(pipe.DownstreamStructureId, outgoing, memo);

                double totalLength = pipe.LengthFt + tailLength;
                if (totalLength > bestLength)
                {
                    bestLength = totalLength;
                    bestPath = new List<NetworkTcPipe> { pipe };
                    bestPath.AddRange(tailPath);
                }
            }

            memo[structureId] = (bestLength, bestPath);
            return memo[structureId];
        }

        /// <summary>Fallback when structure links are missing: single longest pipe.</summary>
        private static List<NetworkTcPipe> LongestPathByLength(IReadOnlyList<NetworkTcPipe> pipes)
        {
            NetworkTcPipe? longest = pipes
                .Where(p => p.LengthFt > 0)
                .OrderByDescending(p => p.LengthFt)
                .FirstOrDefault();

            return longest == null
                ? new List<NetworkTcPipe>()
                : new List<NetworkTcPipe> { longest };
        }
    }
}