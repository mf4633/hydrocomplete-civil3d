using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using HydroComplete.Civil3D.Reading;
using HydroComplete.Engine;

namespace HydroComplete.Civil3D.Writing
{
    /// <summary>
    /// Creates Civil 3D pipe-network geometry from a parsed LandXML import result.
    /// </summary>
    public static class LandXmlNetworkImporter
    {
        public sealed class ImportResult
        {
            public int NetworksCreated { get; set; }
            public int StructuresCreated { get; set; }
            public int PipesCreated { get; set; }
            public int Skipped { get; set; }
            public List<string> Errors { get; } = new List<string>();
            public List<string> Warnings { get; } = new List<string>();
        }

        public static ImportResult ImportToDrawing(
            Database db,
            CivilDocument civilDoc,
            LandXmlImportResult import,
            string? networkNameOverride = null)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (civilDoc == null) throw new ArgumentNullException(nameof(civilDoc));
            if (import == null) throw new ArgumentNullException(nameof(import));

            var result = new ImportResult();
            if (import.Errors.Count > 0)
            {
                result.Errors.AddRange(import.Errors);
                return result;
            }

            result.Warnings.AddRange(import.Warnings);

            var networkGroups = import.Pipes
                .GroupBy(p => string.IsNullOrWhiteSpace(p.NetworkName) ? "Network" : p.NetworkName.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectId partsListId = PartsCatalogResolver.ResolvePartsListId(db, civilDoc, tr);
                if (partsListId.IsNull)
                {
                    result.Errors.Add("No parts list found in the drawing. Add a pipe network or parts list first.");
                    return result;
                }

                PartsCatalogResolver.PartSelection structurePart =
                    PartsCatalogResolver.ResolveStructurePart(tr, partsListId);

                foreach (IGrouping<string, LandXmlPipeRecord> group in networkGroups)
                {
                    string networkName = string.IsNullOrWhiteSpace(networkNameOverride)
                        ? group.Key
                        : networkNameOverride.Trim();

                    try
                    {
                        string createName = networkName;
                        ObjectId networkId = Network.Create(civilDoc, ref createName);
                        var network = (Network)tr.GetObject(networkId, OpenMode.ForWrite);
                        network.PartsListId = partsListId;
                        result.NetworksCreated++;

                        var structureIds = CreateStructures(
                            tr, network, group.Key, group.ToList(), import.Structures, structurePart, result);

                        foreach (LandXmlPipeRecord pipeRecord in group)
                        {
                            try
                            {
                                if (CreatePipe(tr, network, pipeRecord, structureIds, partsListId, result))
                                    result.PipesCreated++;
                                else
                                    result.Skipped++;
                            }
                            catch (Exception ex)
                            {
                                result.Skipped++;
                                result.Errors.Add($"{pipeRecord.Name}: {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"{group.Key}: {ex.Message}");
                    }
                }

                tr.Commit();
            }

            return result;
        }

        private static Dictionary<string, ObjectId> CreateStructures(
            Transaction tr,
            Network network,
            string networkName,
            IReadOnlyList<LandXmlPipeRecord> networkPipes,
            IReadOnlyList<LandXmlStructureRecord> structures,
            PartsCatalogResolver.PartSelection structurePart,
            ImportResult result)
        {
            var ids = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
            var needed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (LandXmlPipeRecord pipe in networkPipes)
            {
                if (!string.IsNullOrWhiteSpace(pipe.StartStructureName))
                    needed.Add(pipe.StartStructureName.Trim());
                if (!string.IsNullOrWhiteSpace(pipe.EndStructureName))
                    needed.Add(pipe.EndStructureName.Trim());
            }

            foreach (LandXmlStructureRecord structure in structures)
            {
                if (!string.Equals(structure.NetworkName, networkName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.IsNullOrWhiteSpace(structure.Name))
                    continue;

                if (!needed.Contains(structure.Name))
                    continue;

                if (!structure.NorthingFt.HasValue || !structure.EastingFt.HasValue)
                {
                    result.Warnings.Add($"Structure '{structure.Name}' has no Center XY — skipped.");
                    continue;
                }

                if (ids.ContainsKey(structure.Name))
                    continue;

                double rim = structure.RimFt ?? structure.InvertFt ?? 0;
                var location = new Point3d(structure.EastingFt.Value, structure.NorthingFt.Value, 0);

                ObjectId structureId = ObjectId.Null;
                network.AddStructure(
                    structurePart.FamilyId,
                    structurePart.SizeId,
                    location,
                    0.0,
                    ref structureId,
                    applyRules: false);

                if (structureId.IsNull)
                {
                    result.Warnings.Add($"Could not create structure '{structure.Name}'.");
                    continue;
                }

                var created = (Structure)tr.GetObject(structureId, OpenMode.ForWrite);
                created.Name = structure.Name;
                created.RimElevation = rim;
                if (structure.InvertFt.HasValue)
                    created.SumpElevation = structure.InvertFt.Value;

                ids[structure.Name] = structureId;
                result.StructuresCreated++;
            }

            return ids;
        }

        private static bool CreatePipe(
            Transaction tr,
            Network network,
            LandXmlPipeRecord record,
            IReadOnlyDictionary<string, ObjectId> structureIds,
            ObjectId partsListId,
            ImportResult result)
        {
            if (string.IsNullOrWhiteSpace(record.StartStructureName)
                || string.IsNullOrWhiteSpace(record.EndStructureName))
            {
                result.Warnings.Add($"Pipe '{record.Name}' missing refStart/refEnd — skipped.");
                return false;
            }

            if (!structureIds.TryGetValue(record.StartStructureName, out ObjectId startId)
                || !structureIds.TryGetValue(record.EndStructureName, out ObjectId endId))
            {
                result.Warnings.Add($"Pipe '{record.Name}' references unknown structures — skipped.");
                return false;
            }

            var startStruct = (Structure)tr.GetObject(startId, OpenMode.ForRead);
            var endStruct = (Structure)tr.GetObject(endId, OpenMode.ForRead);

            PartsCatalogResolver.PartSelection pipePart = PartsCatalogResolver.ResolvePipePart(
                tr,
                partsListId,
                record.Shape,
                record.DiameterFt,
                record.WidthFt,
                record.HeightFt);

            Point3d start = startStruct.Location;
            Point3d end = endStruct.Location;
            double startZ = CenterlineElevation(record, isStart: true);
            double endZ = CenterlineElevation(record, isStart: false);
            var line = new LineSegment3d(
                new Point3d(start.X, start.Y, startZ),
                new Point3d(end.X, end.Y, endZ));

            ObjectId pipeId = ObjectId.Null;
            network.AddLinePipe(
                pipePart.FamilyId,
                pipePart.SizeId,
                line,
                ref pipeId,
                applyRules: false);

            if (pipeId.IsNull)
                return false;

            var pipe = (Pipe)tr.GetObject(pipeId, OpenMode.ForWrite);
            pipe.Name = record.Name;

            ApplyPipeDimensions(pipe, record);

            if (record.Slope > 0)
                pipe.SetSlopeHoldStart(record.Slope);

            pipe.ConnectToStructure(ConnectorPositionType.Start, startId, force: true);
            pipe.ConnectToStructure(ConnectorPositionType.End, endId, force: true);

            return true;
        }

        private static double CenterlineElevation(LandXmlPipeRecord record, bool isStart)
        {
            double invert = isStart ? record.StartInvertFt : record.EndInvertFt;
            if (record.Shape == LandXmlPipeShape.Box || record.Shape == LandXmlPipeShape.Arch)
            {
                double rise = record.HeightFt > 0 ? record.HeightFt : record.DiameterFt;
                return invert + rise / 2.0;
            }

            double diameter = record.DiameterFt > 0 ? record.DiameterFt : 1.0;
            return invert + diameter / 2.0;
        }

        private static void ApplyPipeDimensions(Pipe pipe, LandXmlPipeRecord record)
        {
            switch (record.Shape)
            {
                case LandXmlPipeShape.Box:
                case LandXmlPipeShape.Arch:
                    if (record.WidthFt > 0)
                        pipe.ResizeByInnerDiameterOrWidth(record.WidthFt, useClosestSize: true);
                    break;

                default:
                    if (record.DiameterFt > 0)
                        pipe.ResizeByInnerDiameterOrWidth(record.DiameterFt, useClosestSize: true);
                    break;
            }
        }
    }
}