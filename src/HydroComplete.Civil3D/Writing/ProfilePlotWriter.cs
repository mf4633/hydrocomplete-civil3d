using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using HydroComplete.Civil3D.Reading;
using HydroComplete.Engine;

namespace HydroComplete.Civil3D.Writing
{
    /// <summary>
    /// Draws Hydraflow-style invert, crown, and optional HGL profile polylines
    /// (chainage vs elevation) in model-space WCS on HC-PROFILE-* layers.
    /// </summary>
    public static class ProfilePlotWriter
    {
        public const string InvertLayer = "HC-PROFILE-INVERT";
        public const string CrownLayer = "HC-PROFILE-CROWN";
        public const string HglLayer = "HC-PROFILE-HGL";

        public sealed class HglPipeEnds
        {
            public double HglUsFt { get; set; }
            public double HglDsFt { get; set; }
        }

        public sealed class ProfilePlotOptions
        {
            public Point3d InsertionPoint { get; set; }
            public double DatumElevationFt { get; set; }

            /// <summary>Real feet of chainage per drawing foot horizontally (20 = compress 20:1).</summary>
            public double HorizontalScale { get; set; } = 20.0;

            /// <summary>Real feet of elevation per drawing foot vertically (20 = compress 20:1).</summary>
            public double VerticalScale { get; set; } = 20.0;

            public bool IncludeHgl { get; set; }
        }

        public sealed class ProfileStation
        {
            public double ChainageFt { get; set; }
            public string StructureName { get; set; } = "";
            public double InvertFt { get; set; }
            public double CrownFt { get; set; }
            public double? HglFt { get; set; }
        }

        public sealed class ProfilePlotData
        {
            public string NetworkName { get; set; } = "";
            public List<ProfileStation> Stations { get; } = new List<ProfileStation>();
            public List<Point2d> InvertPoints { get; } = new List<Point2d>();
            public List<Point2d> CrownPoints { get; } = new List<Point2d>();
            public List<Point2d> HglPoints { get; } = new List<Point2d>();
        }

        public sealed class WriteResult
        {
            public int PolylinesDrawn { get; set; }
            public int LabelsDrawn { get; set; }
            public List<string> Errors { get; } = new List<string>();
        }

        public static ProfilePlotData BuildPlotData(
            NetworkTopology.OrderedNetwork net,
            IReadOnlyDictionary<string, HglPipeEnds>? pipeHglEnds = null)
        {
            if (net == null) throw new ArgumentNullException(nameof(net));

            var data = new ProfilePlotData { NetworkName = net.NetworkName };
            if (net.OrderedPipes.Count == 0) return data;

            double chainage = 0.0;
            bool includeHgl = pipeHglEnds != null && pipeHglEnds.Count > 0;

            for (int i = 0; i < net.OrderedPipes.Count; i++)
            {
                ReadPipe rp = net.OrderedPipes[i];
                string pipeKey = PipeKey(rp);
                double? hglUs = null;
                double? hglDs = null;
                if (includeHgl
                    && pipeHglEnds != null
                    && pipeHglEnds.TryGetValue(pipeKey, out HglPipeEnds? ends))
                {
                    hglUs = ends.HglUsFt;
                    hglDs = ends.HglDsFt;
                }

                if (i == 0)
                {
                    AddProfilePoint(
                        data, chainage, rp, rp.UpstreamInvertFt, rp.UpstreamStructureId,
                        hglUs);
                }

                chainage += rp.LengthFt;

                AddProfilePoint(
                    data, chainage, rp, rp.DownstreamInvertFt, rp.DownstreamStructureId,
                    hglDs);
            }

            return data;
        }

        public static WriteResult WriteProfile(
            Database db,
            ProfilePlotData data,
            ProfilePlotOptions options)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (options == null) throw new ArgumentNullException(nameof(options));

            var result = new WriteResult();
            double hScale = Math.Max(options.HorizontalScale, 1e-6);
            double vScale = Math.Max(options.VerticalScale, 1e-6);

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                EnsureLayers(db, tr);
                BlockTableRecord space = (BlockTableRecord)tr.GetObject(
                    db.CurrentSpaceId, OpenMode.ForWrite);

                try
                {
                    if (data.InvertPoints.Count >= 2)
                    {
                        DrawPolyline(db, space, tr, data.InvertPoints, options, hScale, vScale, InvertLayer);
                        result.PolylinesDrawn++;
                    }

                    if (data.CrownPoints.Count >= 2)
                    {
                        DrawPolyline(db, space, tr, data.CrownPoints, options, hScale, vScale, CrownLayer);
                        result.PolylinesDrawn++;
                    }

                    if (options.IncludeHgl && data.HglPoints.Count >= 2)
                    {
                        DrawPolyline(db, space, tr, data.HglPoints, options, hScale, vScale, HglLayer);
                        result.PolylinesDrawn++;
                    }

                    foreach (ProfileStation station in data.Stations)
                    {
                        Point3d labelPt = ToWcs(station.ChainageFt, station.InvertFt, options, hScale, vScale);
                        string hglText = station.HglFt.HasValue
                            ? $"\nHGL {station.HglFt.Value:0.00}"
                            : "";
                        var mtext = new MText
                        {
                            Location = labelPt,
                            TextHeight = Math.Max(0.5, 2.0 / vScale),
                            Layer = InvertLayer,
                            Contents = $"{station.StructureName}\nSTA {station.ChainageFt:0.0}{hglText}",
                        };
                        mtext.SetDatabaseDefaults(db);
                        space.AppendEntity(mtext);
                        tr.AddNewlyCreatedDBObject(mtext, true);
                        result.LabelsDrawn++;
                    }
                }
                catch (System.Exception ex)
                {
                    result.Errors.Add(ex.Message);
                }

                tr.Commit();
            }

            return result;
        }

        public static double DefaultDatumFt(NetworkTopology.OrderedNetwork net)
        {
            double minInvert = double.PositiveInfinity;
            foreach (ReadPipe rp in net.OrderedPipes)
            {
                minInvert = Math.Min(minInvert, rp.UpstreamInvertFt);
                minInvert = Math.Min(minInvert, rp.DownstreamInvertFt);
            }

            if (double.IsPositiveInfinity(minInvert))
                return 0.0;

            return minInvert - 5.0;
        }

        private static void AddProfilePoint(
            ProfilePlotData data,
            double chainageFt,
            ReadPipe rp,
            double invertFt,
            ObjectId structureId,
            double? hglFt)
        {
            double crownFt = CrownElevationFt(rp, invertFt);
            data.InvertPoints.Add(new Point2d(chainageFt, invertFt));
            data.CrownPoints.Add(new Point2d(chainageFt, crownFt));

            if (hglFt.HasValue)
                data.HglPoints.Add(new Point2d(chainageFt, hglFt.Value));

            data.Stations.Add(new ProfileStation
            {
                ChainageFt = chainageFt,
                StructureName = StructureName(rp, structureId),
                InvertFt = invertFt,
                CrownFt = crownFt,
                HglFt = hglFt,
            });
        }

        private static double CrownElevationFt(ReadPipe rp, double invertFt)
        {
            if (rp.Segment.Shape == PipeShape.Box && rp.Segment.HeightFt > 0)
                return invertFt + rp.Segment.HeightFt;

            if (rp.Segment.Shape == PipeShape.Arch)
            {
                double rise = rp.Segment.RiseFt > 0 ? rp.Segment.RiseFt : rp.Segment.HeightFt;
                if (rise > 0) return invertFt + rise;
            }

            return invertFt + rp.Segment.DiameterFt;
        }

        private static string StructureName(ReadPipe rp, ObjectId structureId)
        {
            if (structureId == rp.StartStructureId) return NameOrDash(rp.StartStructureName);
            if (structureId == rp.EndStructureId) return NameOrDash(rp.EndStructureName);
            if (structureId == rp.UpstreamStructureId)
            {
                return rp.UpstreamStructureId == rp.StartStructureId
                    ? NameOrDash(rp.StartStructureName)
                    : NameOrDash(rp.EndStructureName);
            }

            return rp.DownstreamStructureId == rp.StartStructureId
                ? NameOrDash(rp.StartStructureName)
                : NameOrDash(rp.EndStructureName);
        }

        private static string NameOrDash(string name)
            => string.IsNullOrWhiteSpace(name) ? "—" : name;

        private static string PipeKey(ReadPipe rp)
            => string.IsNullOrEmpty(rp.PipeName)
                ? rp.PipeId.Handle.ToString()
                : rp.PipeName;

        private static void DrawPolyline(
            Database db,
            BlockTableRecord space,
            Transaction tr,
            IReadOnlyList<Point2d> profilePoints,
            ProfilePlotOptions options,
            double hScale,
            double vScale,
            string layer)
        {
            var poly = new Polyline();
            poly.SetDatabaseDefaults(db);
            poly.Layer = layer;

            for (int i = 0; i < profilePoints.Count; i++)
            {
                Point2d pt = profilePoints[i];
                Point3d wcs = ToWcs(pt.X, pt.Y, options, hScale, vScale);
                poly.AddVertexAt(i, new Point2d(wcs.X, wcs.Y), 0.0, 0.0, 0.0);
            }

            space.AppendEntity(poly);
            tr.AddNewlyCreatedDBObject(poly, true);
        }

        private static Point3d ToWcs(
            double chainageFt,
            double elevationFt,
            ProfilePlotOptions options,
            double hScale,
            double vScale)
        {
            double x = options.InsertionPoint.X + chainageFt / hScale;
            double y = options.InsertionPoint.Y + (elevationFt - options.DatumElevationFt) / vScale;
            return new Point3d(x, y, options.InsertionPoint.Z);
        }

        private static void EnsureLayers(Database db, Transaction tr)
        {
            EnsureLayer(db, tr, InvertLayer, 3);   // green
            EnsureLayer(db, tr, CrownLayer, 4);    // cyan
            EnsureLayer(db, tr, HglLayer, 6);      // magenta
        }

        private static void EnsureLayer(Database db, Transaction tr, string name, short aciColor)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return;

            lt.UpgradeOpen();
            var layer = new LayerTableRecord
            {
                Name = name,
                Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                    Autodesk.AutoCAD.Colors.ColorMethod.ByAci, aciColor),
            };
            lt.Add(layer);
            tr.AddNewlyCreatedDBObject(layer, true);
        }
    }
}