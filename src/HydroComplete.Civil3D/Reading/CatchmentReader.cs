using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using HydroComplete.Engine;

namespace HydroComplete.Civil3D.Reading
{
    /// <summary>
    /// Reads Civil 3D catchments into the engine's <see cref="Catchment"/> model.
    /// Civil 3D stores catchment area in square feet (imperial); we convert to
    /// acres. Runoff coefficient and time of concentration are pulled from the
    /// catchment when present, otherwise sensible defaults are used.
    /// </summary>
    public static class CatchmentReader
    {
        public const double SqFtPerAcre = 43560.0;
        public const double DefaultRunoffC = 0.5;
        public const double DefaultTcMinutes = 10.0;

        public static List<Engine.Catchment> ReadAll(Database db, CivilDocument civilDoc)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (civilDoc == null) throw new ArgumentNullException(nameof(civilDoc));

            var result = new List<Engine.Catchment>();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            using (CatchmentGroupCollection groupIds = civilDoc.GetCatchmentGroups())
            {
                foreach (ObjectId gid in groupIds)
                {
                    if (!(tr.GetObject(gid, OpenMode.ForRead) is CatchmentGroup group)) continue;

                    foreach (ObjectId cid in group.GetAllCatchmentIds())
                    {
                        if (!(tr.GetObject(cid, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.Catchment c))
                            continue;

                        double c_runoff = DefaultRunoffC;
                        double c_tc = DefaultTcMinutes;
                        try { if (c.RunoffCoefficient > 0) c_runoff = c.RunoffCoefficient; } catch { }
                        try { if (c.TimeOfConcentration > 0) c_tc = c.TimeOfConcentration; } catch { }

                        result.Add(new Engine.Catchment
                        {
                            Name = c.Name ?? "",
                            AreaAcres = c.Area2d / SqFtPerAcre,
                            RunoffC = Math.Min(1.0, Math.Max(0.0, c_runoff)),
                            TcMinutes = c_tc,
                        });
                    }
                }
                tr.Commit();
            }

            return result;
        }
    }
}
