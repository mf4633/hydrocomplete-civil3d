using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;
using HydroComplete.Civil3D.Reading;
using HydroComplete.Engine;

namespace HydroComplete.Civil3D.Writing
{
    /// <summary>
    /// Writes Manning capacity results back onto Civil 3D pipe objects.
    /// v0.1 stores a short summary on each pipe's Description field (visible in
    /// Prospector) and mirrors the values in HydroComplete XData for later sync.
    /// </summary>
    public static class PipeNetworkWriter
    {
        public const string XDataAppName = "HYDROCOMPLETE";

        public sealed class WriteResult
        {
            public int Updated { get; set; }
            public int Skipped { get; set; }
            public List<string> Errors { get; } = new List<string>();
        }

        public static WriteResult WriteCapacities(
            Database db,
            IReadOnlyList<ReadPipe> pipes,
            IReadOnlyDictionary<ObjectId, Manning.CapacityResult> capacities)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (pipes == null) throw new ArgumentNullException(nameof(pipes));
            if (capacities == null) throw new ArgumentNullException(nameof(capacities));

            var result = new WriteResult();
            EnsureXDataApp(db);

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ReadPipe rp in pipes)
                {
                    if (!capacities.TryGetValue(rp.PipeId, out Manning.CapacityResult? cap))
                    {
                        result.Skipped++;
                        continue;
                    }

                    if (!(tr.GetObject(rp.PipeId, OpenMode.ForWrite) is Pipe pipe))
                    {
                        result.Skipped++;
                        continue;
                    }

                    string summary = string.Format(CultureInfo.InvariantCulture,
                        "HC Qfull={0:0.0} cfs, Vfull={1:0.1f} fps, n={2:0.3f}",
                        cap.FullFlowCfs, cap.FullVelocityFps, rp.Segment.ManningN);

                    bool wrote = false;
                    try
                    {
                        pipe.Description = summary;
                        wrote = true;
                    }
                    catch (System.Exception ex)
                    {
                        result.Errors.Add($"{rp.PipeName} Description: {ex.Message}");
                    }

                    try
                    {
                        WriteXData(pipe, cap.FullFlowCfs, cap.FullVelocityFps);
                        wrote = true;
                    }
                    catch (System.Exception ex)
                    {
                        result.Errors.Add($"{rp.PipeName} XData: {ex.Message}");
                    }

                    if (wrote) result.Updated++;
                    else result.Skipped++;
                }

                tr.Commit();
            }

            return result;
        }

        private static void EnsureXDataApp(Database db)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
                if (!rat.Has(XDataAppName))
                {
                    rat.UpgradeOpen();
                    var rec = new RegAppTableRecord { Name = XDataAppName };
                    rat.Add(rec);
                    tr.AddNewlyCreatedDBObject(rec, true);
                }
                tr.Commit();
            }
        }

        private static void WriteXData(Pipe pipe, double qFullCfs, double vFullFps)
        {
            // XData must begin with the registered application name (1001) or AutoCAD
            // raises eBadDxfSequence.
            pipe.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, XDataAppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, "QFULL"),
                new TypedValue((int)DxfCode.ExtendedDataReal, qFullCfs),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, "VFULL"),
                new TypedValue((int)DxfCode.ExtendedDataReal, vFullFps));
        }
    }
}