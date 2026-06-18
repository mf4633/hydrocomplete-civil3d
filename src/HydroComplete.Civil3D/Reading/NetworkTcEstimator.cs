using System;
using System.Collections.Generic;
using HydroComplete.Engine;

namespace HydroComplete.Civil3D.Reading
{
    /// <summary>
    /// Reads ordered pipe networks from Civil 3D and estimates system Tc using the
    /// engine's longest-path methods.
    /// </summary>
    public static class NetworkTcEstimator
    {
        /// <summary>
        /// Estimates system Tc (minutes) from drawing pipes. Returns 0 when no
        /// usable geometry is available.
        /// </summary>
        public static double EstimateSystemTc(
            IReadOnlyList<ReadPipe> pipes,
            NetworkTcMethod method = NetworkTcMethod.TravelTime)
        {
            if (pipes == null) throw new ArgumentNullException(nameof(pipes));
            if (pipes.Count == 0) return 0.0;

            var enginePipes = ToEnginePipes(pipes);
            return Engine.NetworkTcEstimator.EstimateSystemTc(enginePipes, method);
        }

        private static List<NetworkTcPipe> ToEnginePipes(IReadOnlyList<ReadPipe> pipes)
        {
            var result = new List<NetworkTcPipe>(pipes.Count);
            foreach (ReadPipe rp in pipes)
            {
                double lengthFt = rp.LengthFt > 0 ? rp.LengthFt : rp.Segment.LengthFt;
                result.Add(new NetworkTcPipe
                {
                    PipeKey = rp.PipeId.Handle.ToString(),
                    NetworkName = rp.NetworkName,
                    UpstreamStructureId = rp.UpstreamStructureId.Handle.ToString(),
                    DownstreamStructureId = rp.DownstreamStructureId.Handle.ToString(),
                    LengthFt = lengthFt,
                    Segment = rp.Segment,
                });
            }

            return result;
        }
    }
}