using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

namespace HydroComplete.Civil3D.Reading
{
    /// <summary>Detects pump stations from structure names / part families in pipe networks.</summary>
    internal static class PumpStationReader
    {
        internal sealed class PumpLocation
        {
            public string Name { get; set; } = "";
            public string NetworkName { get; set; } = "";
            public ObjectId StructureId { get; set; }
            public double RimFt { get; set; }
            public double InvertFt { get; set; }
            public Point3d Location { get; set; }
        }

        public static List<PumpLocation> ReadAll(Database db, CivilDocument civilDoc)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (civilDoc == null) throw new ArgumentNullException(nameof(civilDoc));

            var pumps = new List<PumpLocation>();
            ObjectIdCollection networkIds = civilDoc.GetPipeNetworkIds();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId nid in networkIds)
                {
                    if (!(tr.GetObject(nid, OpenMode.ForRead) is Network net)) continue;
                    string netName = net.Name ?? "";

                    foreach (ObjectId sid in net.GetStructureIds())
                    {
                        if (!(tr.GetObject(sid, OpenMode.ForRead) is Structure structure)) continue;
                        string name = structure.Name ?? "";
                        if (!LooksLikePump(name)) continue;

                        pumps.Add(new PumpLocation
                        {
                            Name = name,
                            NetworkName = netName,
                            StructureId = sid,
                            RimFt = structure.RimElevation,
                            InvertFt = structure.SumpElevation,
                            Location = structure.Location,
                        });
                    }
                }

                tr.Commit();
            }

            return pumps;
        }

        private static bool LooksLikePump(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            return name.IndexOf("pump", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("ps-", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("ps_", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}