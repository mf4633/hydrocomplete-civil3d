using System;
using System.Collections.Generic;
using HydroComplete.Engine;

namespace HydroComplete.Civil3D.Reading
{
    /// <summary>Maps Civil 3D <see cref="ReadPipe"/> rows to engine topology links.</summary>
    public static class NetworkPipeLinkMapper
    {
        public static List<NetworkPipeLink> FromReadPipes(IReadOnlyList<ReadPipe> pipes)
        {
            if (pipes == null) throw new ArgumentNullException(nameof(pipes));

            var links = new List<NetworkPipeLink>(pipes.Count);
            foreach (ReadPipe rp in pipes)
            {
                links.Add(new NetworkPipeLink
                {
                    PipeKey = rp.PipeId.Handle.ToString(),
                    NetworkName = rp.NetworkName,
                    PipeName = rp.PipeName,
                    UpstreamStructureId = rp.UpstreamStructureId.Handle.ToString(),
                    DownstreamStructureId = rp.DownstreamStructureId.Handle.ToString(),
                });
            }

            return links;
        }

        public static Dictionary<string, string> StructureNamesFromPipes(IReadOnlyList<ReadPipe> pipes)
        {
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (ReadPipe rp in pipes)
            {
                string us = rp.UpstreamStructureId.Handle.ToString();
                string ds = rp.DownstreamStructureId.Handle.ToString();

                if (!string.IsNullOrWhiteSpace(rp.StartStructureName))
                {
                    if (rp.UpstreamStructureId == rp.StartStructureId)
                        names[us] = rp.StartStructureName;
                    if (rp.DownstreamStructureId == rp.StartStructureId)
                        names[ds] = rp.StartStructureName;
                }

                if (!string.IsNullOrWhiteSpace(rp.EndStructureName))
                {
                    if (rp.UpstreamStructureId == rp.EndStructureId)
                        names[us] = rp.EndStructureName;
                    if (rp.DownstreamStructureId == rp.EndStructureId)
                        names[ds] = rp.EndStructureName;
                }
            }

            return names;
        }
    }
}