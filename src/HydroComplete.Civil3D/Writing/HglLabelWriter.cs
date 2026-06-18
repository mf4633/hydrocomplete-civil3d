using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;
using HydroComplete.Civil3D.Reading;

namespace HydroComplete.Civil3D.Writing
{
    /// <summary>
    /// Writes steady HGL results back into the drawing as MText labels on
    /// layer HC-HGL at each pipe midpoint.
    /// </summary>
    public static class HglLabelWriter
    {
        public const string LabelLayer = "HC-HGL";

        public sealed class WriteResult
        {
            public int Updated { get; set; }
            public int Skipped { get; set; }
            public List<string> Errors { get; } = new List<string>();
        }

        public static WriteResult WriteHglLabels(
            Database db,
            IReadOnlyList<ReadPipe> pipes,
            IReadOnlyDictionary<string, double> midpointHglFt,
            ICollection<string>? surchargedKeys = null)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (pipes == null) throw new ArgumentNullException(nameof(pipes));
            if (midpointHglFt == null) throw new ArgumentNullException(nameof(midpointHglFt));

            var result = new WriteResult();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                EnsureLayer(db, tr);
                ClearExistingLabels(db, tr);

                BlockTableRecord space = (BlockTableRecord)tr.GetObject(
                    db.CurrentSpaceId, OpenMode.ForWrite);

                foreach (ReadPipe rp in pipes)
                {
                    string key = string.IsNullOrEmpty(rp.PipeName)
                        ? rp.PipeId.Handle.ToString()
                        : rp.PipeName;

                    if (!midpointHglFt.TryGetValue(key, out double hgl))
                    {
                        result.Skipped++;
                        continue;
                    }

                    try
                    {
                        if (!(tr.GetObject(rp.PipeId, OpenMode.ForRead) is Pipe pipe))
                        {
                            result.Skipped++;
                            continue;
                        }

                        bool surcharged = surchargedKeys != null && surchargedKeys.Contains(key);
                        string text = string.Format(CultureInfo.InvariantCulture,
                            surcharged ? "HGL={0:0.00} ft SURCH" : "HGL={0:0.00} ft", hgl);

                        Point3d mid = Midpoint(pipe.StartPoint, pipe.EndPoint);
                        double height = Math.Max(0.5, rp.Segment.DiameterFt * 0.15);

                        var label = new MText
                        {
                            Location = mid,
                            TextHeight = height,
                            Layer = LabelLayer,
                            Contents = text,
                            Attachment = AttachmentPoint.MiddleCenter,
                        };
                        label.SetDatabaseDefaults(db);

                        space.AppendEntity(label);
                        tr.AddNewlyCreatedDBObject(label, true);
                        result.Updated++;
                    }
                    catch (System.Exception ex)
                    {
                        result.Skipped++;
                        result.Errors.Add($"{rp.PipeName}: {ex.Message}");
                    }
                }

                tr.Commit();
            }

            return result;
        }

        private static Point3d Midpoint(Point3d a, Point3d b)
        {
            return new Point3d(
                (a.X + b.X) * 0.5,
                (a.Y + b.Y) * 0.5,
                (a.Z + b.Z) * 0.5);
        }

        private static void EnsureLayer(Database db, Transaction tr)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(LabelLayer)) return;

            lt.UpgradeOpen();
            var layer = new LayerTableRecord
            {
                Name = LabelLayer,
                Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                    Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 5), // blue
            };
            lt.Add(layer);
            tr.AddNewlyCreatedDBObject(layer, true);
        }

        private static void ClearExistingLabels(Database db, Transaction tr)
        {
            var erase = new List<ObjectId>();
            BlockTableRecord space = (BlockTableRecord)tr.GetObject(
                db.CurrentSpaceId, OpenMode.ForRead);

            foreach (ObjectId id in space)
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is MText mt)) continue;
                if (string.Equals(mt.Layer, LabelLayer, StringComparison.OrdinalIgnoreCase))
                    erase.Add(id);
            }

            foreach (ObjectId id in erase)
            {
                var ent = tr.GetObject(id, OpenMode.ForWrite);
                ent.Erase();
            }
        }
    }
}