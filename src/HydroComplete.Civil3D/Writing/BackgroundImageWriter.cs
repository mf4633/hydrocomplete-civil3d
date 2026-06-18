using System;
using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace HydroComplete.Civil3D.Writing
{
    /// <summary>Attaches a georeferenced raster image on layer HC-BACKGROUND.</summary>
    public static class BackgroundImageWriter
    {
        public const string LayerName = "HC-BACKGROUND";

        public sealed class AttachResult
        {
            public bool Success { get; set; }
            public string Error { get; set; } = "";
        }

        public static AttachResult AttachImage(
            Database db,
            string imagePath,
            Point3d insertionPoint,
            double widthDrawingUnits)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                return new AttachResult { Error = "Image file not found." };
            if (widthDrawingUnits <= 0)
                return new AttachResult { Error = "Width must be positive." };

            var result = new AttachResult();
            string fullPath = Path.GetFullPath(imagePath);

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    EnsureLayer(db, tr);
                    ObjectId dictId = RasterImageDef.CreateImageDictionary(db);
                    var dict = (DBDictionary)tr.GetObject(dictId, OpenMode.ForRead);
                    string key = Path.GetFileName(fullPath);

                    ObjectId imageDefId;
                    if (dict.Contains(key))
                    {
                        imageDefId = dict.GetAt(key);
                    }
                    else
                    {
                        dict.UpgradeOpen();
                        var imageDef = new RasterImageDef();
                        imageDef.SourceFileName = fullPath;
                        imageDef.Load();
                        imageDefId = dict.SetAt(key, imageDef);
                        tr.AddNewlyCreatedDBObject(imageDef, true);
                    }

                    var imageDefObj = (RasterImageDef)tr.GetObject(imageDefId, OpenMode.ForRead);
                    Vector2d pixelSize = imageDefObj.Size;
                    double imageWidth = Math.Max(pixelSize.X, 1e-6);
                    double imageHeight = Math.Max(pixelSize.Y, 1e-6);
                    double scale = widthDrawingUnits / imageWidth;
                    Vector3d u = Vector3d.XAxis * widthDrawingUnits;
                    Vector3d v = Vector3d.YAxis * (imageHeight * scale);

                    BlockTableRecord space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    var raster = new RasterImage();
                    raster.SetDatabaseDefaults(db);
                    raster.Layer = LayerName;
                    raster.ImageDefId = imageDefId;
                    raster.Orientation = new CoordinateSystem3d(insertionPoint, u, v);
                    raster.ShowImage = true;
                    raster.ImageTransparency = false;

                    space.AppendEntity(raster);
                    tr.AddNewlyCreatedDBObject(raster, true);
                    raster.AssociateRasterDef(imageDefObj);

                    tr.Commit();
                    result.Success = true;
                }
                catch (Exception ex)
                {
                    result.Error = ex.Message;
                }
            }

            return result;
        }

        private static void EnsureLayer(Database db, Transaction tr)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(LayerName)) return;

            lt.UpgradeOpen();
            var layer = new LayerTableRecord
            {
                Name = LayerName,
                Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                    Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 8),
            };
            lt.Add(layer);
            tr.AddNewlyCreatedDBObject(layer, true);
        }
    }
}