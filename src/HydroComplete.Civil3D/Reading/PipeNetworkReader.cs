using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using HydroComplete.Engine;

namespace HydroComplete.Civil3D.Reading
{
    /// <summary>An engine pipe paired with the Civil 3D names it came from.</summary>
    public sealed class ReadPipe
    {
        public ObjectId PipeId { get; set; }
        public string NetworkName { get; set; } = "";
        public string PipeName { get; set; } = "";
        public PipeSegment Segment { get; set; } = new PipeSegment();
    }

    /// <summary>
    /// Reads circular gravity pipes out of the active drawing's Civil 3D pipe
    /// networks and maps them onto the engine's <see cref="PipeSegment"/> model.
    /// Diameters are assumed to be in feet (imperial drawing).
    /// </summary>
    public static class PipeNetworkReader
    {
        /// <summary>Default Manning's n; Civil 3D pipes don't carry roughness.</summary>
        public const double DefaultManningN = 0.013;

        public static List<ReadPipe> ReadAll(Database db, CivilDocument civilDoc)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (civilDoc == null) throw new ArgumentNullException(nameof(civilDoc));

            var pipes = new List<ReadPipe>();
            ObjectIdCollection networkIds = civilDoc.GetPipeNetworkIds();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId nid in networkIds)
                {
                    if (!(tr.GetObject(nid, OpenMode.ForRead) is Network net)) continue;
                    string netName = net.Name ?? "";

                    foreach (ObjectId pid in net.GetPipeIds())
                    {
                        if (!(tr.GetObject(pid, OpenMode.ForRead) is Pipe pipe)) continue;

                        pipes.Add(new ReadPipe
                        {
                            PipeId = pid,
                            NetworkName = netName,
                            PipeName = pipe.Name ?? "",
                            Segment = new PipeSegment
                            {
                                Name = pipe.Name ?? "",
                                DiameterFt = pipe.InnerDiameterOrWidth,
                                Slope = Math.Abs(pipe.Slope),
                                ManningN = DefaultManningN,
                            },
                        });
                    }
                }
                tr.Commit();
            }

            return pipes;
        }
    }
}
