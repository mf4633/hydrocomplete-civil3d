using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;
using HydroComplete.Engine;

namespace HydroComplete.Civil3D.Reading
{
    /// <summary>
    /// Resolves Civil 3D part-family and part-size ObjectIds from a network parts list
    /// for LandXML import (circular, box, arch) and structure junctions.
    /// </summary>
    internal static class PartsCatalogResolver
    {
        internal sealed class PartSelection
        {
            public ObjectId FamilyId { get; set; }
            public ObjectId SizeId { get; set; }
            public string FamilyName { get; set; } = "";
            public string SizeName { get; set; } = "";
        }

        public static PartSelection ResolvePipePart(
            Transaction tr,
            ObjectId partsListId,
            LandXmlPipeShape shape,
            double diameterFt,
            double widthFt,
            double heightFt)
        {
            if (partsListId.IsNull)
                throw new InvalidOperationException("No parts list available for LandXML import.");

            var partsList = (PartsList)tr.GetObject(partsListId, OpenMode.ForRead);
            SweptShapeType targetShape = shape switch
            {
                LandXmlPipeShape.Box => SweptShapeType.Rectangular,
                LandXmlPipeShape.Arch => SweptShapeType.Arched,
                _ => SweptShapeType.Circular,
            };

            PartSelection? best = null;
            double bestScore = double.MaxValue;

            foreach (ObjectId familyId in partsList.GetPartFamilyIdsByDomain(DomainType.Pipe))
            {
                var family = (PartFamily)tr.GetObject(familyId, OpenMode.ForRead);
                if (!FamilyMatchesShape(family.Name, targetShape))
                    continue;

                for (int i = 0; i < family.PartSizeCount; i++)
                {
                    ObjectId sizeId = family[i];
                    var size = (PartSize)tr.GetObject(sizeId, OpenMode.ForRead);
                    double score = ScoreSize(shape, diameterFt, widthFt, heightFt, size);
                    if (score >= double.MaxValue / 2)
                        continue;

                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = new PartSelection
                        {
                            FamilyId = familyId,
                            SizeId = sizeId,
                            FamilyName = family.Name ?? "",
                            SizeName = size.Name ?? "",
                        };
                    }
                }
            }

            if (best == null)
                throw new InvalidOperationException(
                    $"No catalog pipe part found for {shape} (dia={diameterFt:0.##} ft). Check the drawing parts list.");

            return best;
        }

        public static PartSelection ResolveStructurePart(Transaction tr, ObjectId partsListId)
        {
            if (partsListId.IsNull)
                throw new InvalidOperationException("No parts list available for structure import.");

            var partsList = (PartsList)tr.GetObject(partsListId, OpenMode.ForRead);
            foreach (ObjectId familyId in partsList.GetPartFamilyIdsByDomain(DomainType.Structure))
            {
                var family = (PartFamily)tr.GetObject(familyId, OpenMode.ForRead);
                string name = family.Name ?? "";
                if (name.IndexOf("junction", StringComparison.OrdinalIgnoreCase) < 0
                    && name.IndexOf("manhole", StringComparison.OrdinalIgnoreCase) < 0
                    && name.IndexOf("catch", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                if (family.PartSizeCount <= 0) continue;

                ObjectId sizeId = family[0];
                var size = (PartSize)tr.GetObject(sizeId, OpenMode.ForRead);
                return new PartSelection
                {
                    FamilyId = familyId,
                    SizeId = sizeId,
                    FamilyName = name,
                    SizeName = size.Name ?? "",
                };
            }

            foreach (ObjectId familyId in partsList.GetPartFamilyIdsByDomain(DomainType.Structure))
            {
                var family = (PartFamily)tr.GetObject(familyId, OpenMode.ForRead);
                if (family.PartSizeCount <= 0) continue;
                ObjectId sizeId = family[0];
                var size = (PartSize)tr.GetObject(sizeId, OpenMode.ForRead);
                return new PartSelection
                {
                    FamilyId = familyId,
                    SizeId = sizeId,
                    FamilyName = family.Name ?? "",
                    SizeName = size.Name ?? "",
                };
            }

            throw new InvalidOperationException("No structure part found in the drawing parts list.");
        }

        public static ObjectId ResolvePartsListId(Database db, CivilDocument civilDoc, Transaction tr)
        {
            ObjectIdCollection networkIds = civilDoc.GetPipeNetworkIds();
            foreach (ObjectId nid in networkIds)
            {
                if (!(tr.GetObject(nid, OpenMode.ForRead) is Network net)) continue;
                if (!net.PartsListId.IsNull)
                    return net.PartsListId;
            }

            foreach (ObjectId listId in civilDoc.Styles.PartsListSet)
            {
                if (!listId.IsNull)
                    return listId;
            }

            return ObjectId.Null;
        }

        private static bool FamilyMatchesShape(string familyName, SweptShapeType target)
        {
            string name = familyName ?? "";
            if (target == SweptShapeType.Rectangular)
                return name.IndexOf("box", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("rect", StringComparison.OrdinalIgnoreCase) >= 0;

            if (target == SweptShapeType.Arched)
                return name.IndexOf("arch", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("egg", StringComparison.OrdinalIgnoreCase) >= 0;

            return name.IndexOf("box", StringComparison.OrdinalIgnoreCase) < 0
                && name.IndexOf("rect", StringComparison.OrdinalIgnoreCase) < 0
                && name.IndexOf("arch", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static double ScoreSize(
            LandXmlPipeShape shape,
            double diameterFt,
            double widthFt,
            double heightFt,
            PartSize size)
        {
            double targetD = diameterFt > 0 ? diameterFt : widthFt;
            double targetW = widthFt > 0 ? widthFt : diameterFt;
            double targetH = heightFt > 0 ? heightFt : diameterFt;

            if (shape == LandXmlPipeShape.Circular)
            {
                double partD = ReadDoubleField(size, "PID", "Inner Pipe Diameter", "Pipe Diameter");
                if (partD <= 0) return double.MaxValue;
                return Math.Abs(partD - targetD);
            }

            if (shape == LandXmlPipeShape.Box || shape == LandXmlPipeShape.Arch)
            {
                double partW = ReadDoubleField(size, "B", "W", "Width", "Span", "Pipe Width");
                double partH = ReadDoubleField(size, "H", "Height", "Rise", "Pipe Height");
                if (partW <= 0 && partH <= 0)
                {
                    double partD = ReadDoubleField(size, "PID", "Inner Pipe Diameter");
                    if (partD <= 0) return double.MaxValue;
                    return Math.Abs(partD - targetD);
                }

                double wErr = partW > 0 ? Math.Abs(partW - targetW) : 0;
                double hErr = partH > 0 ? Math.Abs(partH - targetH) : 0;
                return wErr + hErr;
            }

            return double.MaxValue;
        }

        private static double ReadDoubleField(PartSize size, params string[] names)
        {
            PartDataRecord data = size.SizeDataRecord;
            if (data == null) return 0;

            foreach (string name in names)
            {
                try
                {
                    PartDataField field = data.GetDataFieldBy(name);
                    if (field?.Value == null) continue;
                    return Convert.ToDouble(field.Value, CultureInfo.InvariantCulture);
                }
                catch
                {
                }
            }

            return 0;
        }
    }
}