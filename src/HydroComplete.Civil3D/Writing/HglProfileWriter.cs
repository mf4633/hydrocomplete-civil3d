using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using HydroComplete.Civil3D.Reading;

namespace HydroComplete.Civil3D.Writing
{
    /// <summary>
    /// Writes steady HGL results back into the drawing as 3D polylines on
    /// layer HC-HGL-PROFILE. Vertices use pipe-end plan XY with Z set to the
    /// computed HGL elevation at each upstream/downstream end.
    /// </summary>
    public static class HglProfileWriter
    {
        public const string ProfileLayer = "HC-HGL-PROFILE";

        public sealed class HglPipeEnds
        {
            public double HglUsFt { get; set; }
            public double HglDsFt { get; set; }
        }

        public sealed class WriteResult
        {
            public int NetworksDrawn { get; set; }
            public int VerticesDrawn { get; set; }
            public int Skipped { get; set; }
            public List<string> Errors { get; } = new List<string>();
        }

        public static WriteResult WriteHglProfiles(
            Database db,
            IReadOnlyList<NetworkTopology.OrderedNetwork> networks,
            IReadOnlyDictionary<string, HglPipeEnds> pipeHglEnds)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (networks == null) throw new ArgumentNullException(nameof(networks));
            if (pipeHglEnds == null) throw new ArgumentNullException(nameof(pipeHglEnds));

            var result = new WriteResult();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                EnsureLayer(db, tr);
                ClearExistingProfiles(db, tr);

                BlockTableRecord space = (BlockTableRecord)tr.GetObject(
                    db.CurrentSpaceId, OpenMode.ForWrite);

                foreach (NetworkTopology.OrderedNetwork net in networks)
                {
                    if (net.OrderedPipes.Count == 0) continue;

                    try
                    {
                        var vertices = BuildNetworkVertices(net, pipeHglEnds, result);
                        if (vertices.Count < 2)
                        {
                            result.Skipped++;
                            continue;
                        }

                        var points = new Point3dCollection(vertices.ToArray());
                        var poly = new Polyline3d(Poly3dType.SimplePoly, points, false)
                        {
                            Layer = ProfileLayer,
                        };
                        poly.SetDatabaseDefaults(db);

                        space.AppendEntity(poly);
                        tr.AddNewlyCreatedDBObject(poly, true);
                        result.NetworksDrawn++;
                        result.VerticesDrawn += vertices.Count;
                    }
                    catch (System.Exception ex)
                    {
                        result.Skipped++;
                        result.Errors.Add($"{net.NetworkName}: {ex.Message}");
                    }
                }

                tr.Commit();
            }

            return result;
        }

        private static List<Point3d> BuildNetworkVertices(
            NetworkTopology.OrderedNetwork net,
            IReadOnlyDictionary<string, HglPipeEnds> pipeHglEnds,
            WriteResult result)
        {
            var vertices = new List<Point3d>();

            foreach (ReadPipe rp in net.OrderedPipes)
            {
                string key = PipeKey(rp);
                if (!pipeHglEnds.TryGetValue(key, out HglPipeEnds? ends))
                {
                    result.Skipped++;
                    continue;
                }

                (Point3d usXy, Point3d dsXy) = GetFlowEndPoints(rp);
                var us = new Point3d(usXy.X, usXy.Y, ends.HglUsFt);
                var ds = new Point3d(dsXy.X, dsXy.Y, ends.HglDsFt);

                if (vertices.Count == 0)
                {
                    vertices.Add(us);
                }
                else if (!vertices[vertices.Count - 1].IsEqualTo(us))
                {
                    vertices.Add(us);
                }

                vertices.Add(ds);
            }

            return vertices;
        }

        private static string PipeKey(ReadPipe rp)
        {
            return string.IsNullOrEmpty(rp.PipeName)
                ? rp.PipeId.Handle.ToString()
                : rp.PipeName;
        }

        private static (Point3d Us, Point3d Ds) GetFlowEndPoints(ReadPipe rp)
        {
            bool startIsUpstream = Math.Abs(rp.UpstreamInvertFt - rp.StartInvertFt) < 0.001;
            Point3d us = startIsUpstream ? rp.StartPoint : rp.EndPoint;
            Point3d ds = startIsUpstream ? rp.EndPoint : rp.StartPoint;
            return (us, ds);
        }

        private static void EnsureLayer(Database db, Transaction tr)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(ProfileLayer)) return;

            lt.UpgradeOpen();
            var layer = new LayerTableRecord
            {
                Name = ProfileLayer,
                Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                    Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 6), // magenta — distinct from HC-HGL blue
            };
            lt.Add(layer);
            tr.AddNewlyCreatedDBObject(layer, true);
        }

        private static void ClearExistingProfiles(Database db, Transaction tr)
        {
            var erase = new List<ObjectId>();
            BlockTableRecord space = (BlockTableRecord)tr.GetObject(
                db.CurrentSpaceId, OpenMode.ForRead);

            foreach (ObjectId id in space)
            {
                DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                if (obj is Polyline3d pl3d
                    && string.Equals(pl3d.Layer, ProfileLayer, StringComparison.OrdinalIgnoreCase))
                {
                    erase.Add(id);
                    continue;
                }

                if (obj is Polyline lw
                    && string.Equals(lw.Layer, ProfileLayer, StringComparison.OrdinalIgnoreCase))
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