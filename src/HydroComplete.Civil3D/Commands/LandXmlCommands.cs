using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using HydroComplete.Civil3D.Reading;
using HydroComplete.Civil3D.Writing;
using HydroComplete.Engine;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace HydroComplete.Civil3D.Commands
{
    /// <summary>HC_LANDXML / HC_LANDXML_IMPORT — LandXML 1.2 storm network export and import-read.</summary>
    public sealed class LandXmlCommands
    {
        [CommandMethod("HC_LANDXML_IMPORT")]
        public void ImportLandXml()
        {
            Document doc = Active();
            Editor ed = doc.Editor;
            CivilDocument civilDoc = CivilApplication.ActiveDocument;

            Directory.CreateDirectory(ReportWriterCommon.OutputFolder);
            string defaultPath = ReportWriterCommon.OutputFolder;

            string inputPath = PromptInputPath(ed, defaultPath);
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                ed.WriteMessage("\nLandXML import cancelled.\n");
                return;
            }

            LandXmlImportResult import = LandXmlReader.Parse(inputPath);
            if (import.Errors.Count > 0)
            {
                ed.WriteMessage("\n--- HydroComplete: LandXML import errors ---\n");
                foreach (string error in import.Errors)
                    ed.WriteMessage("  " + error + "\n");
                return;
            }

            var networkGroups = import.Pipes
                .GroupBy(p => string.IsNullOrWhiteSpace(p.NetworkName) ? "Network" : p.NetworkName.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: LandXML import ---\n  Project: {0}\n  Networks: {1}\n",
                string.IsNullOrWhiteSpace(import.ProjectName) ? "(unnamed)" : import.ProjectName,
                networkGroups.Count));

            ed.WriteMessage("\nNetwork                 Pipes  Structs  Length(ft)");
            foreach (IGrouping<string, LandXmlPipeRecord> group in networkGroups)
            {
                int structCount = import.Structures.Count(s =>
                    string.Equals(s.NetworkName, group.Key, StringComparison.OrdinalIgnoreCase));

                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n{0,-22} {1,5}  {2,7}  {3,10:0.0}",
                    Trim(group.Key, 22),
                    group.Count(),
                    structCount,
                    group.Sum(p => p.LengthFt)));
            }

            ed.WriteMessage("\n\n  First pipes (up to 10):");
            ed.WriteMessage("\n  Name              Dia(ft)  Slope      Length(ft)  Start      End");
            foreach (LandXmlPipeRecord pipe in import.Pipes
                .OrderBy(p => p.NetworkName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Take(10))
            {
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  {0,-16} {1,7:0.00}  {2,8:0.####}  {3,10:0.0}  {4,-10} {5,-10}",
                    Trim(pipe.Name, 16),
                    pipe.DiameterFt,
                    pipe.Slope,
                    pipe.LengthFt,
                    Trim(pipe.StartStructureName, 10),
                    Trim(pipe.EndStructureName, 10)));
            }

            var drawingPipes = PipeNetworkReader.ReadAll(doc.Database, civilDoc);
            if (drawingPipes.Count > 0)
            {
                var drawingByNetwork = drawingPipes
                    .GroupBy(p => string.IsNullOrWhiteSpace(p.NetworkName) ? "Network" : p.NetworkName.Trim(),
                        StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

                ed.WriteMessage("\n\n  Drawing comparison (pipe counts):");
                ed.WriteMessage("\n  Network                 Import  Drawing");

                var allNetworks = networkGroups.Select(g => g.Key)
                    .Concat(drawingByNetwork.Keys)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

                foreach (string network in allNetworks)
                {
                    int importCount = import.Pipes.Count(p =>
                        string.Equals(
                            string.IsNullOrWhiteSpace(p.NetworkName) ? "Network" : p.NetworkName.Trim(),
                            network,
                            StringComparison.OrdinalIgnoreCase));
                    drawingByNetwork.TryGetValue(network, out int drawingCount);

                    ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                        "\n  {0,-22} {1,6}  {2,7}",
                        Trim(network, 22),
                        importCount,
                        drawingCount));
                }
            }

            if (import.Warnings.Count > 0)
            {
                ed.WriteMessage("\n\n  Warnings:");
                foreach (string warning in import.Warnings.Take(10))
                    ed.WriteMessage("\n    " + warning);
                if (import.Warnings.Count > 10)
                {
                    ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                        "\n    ... and {0} more warning(s).",
                        import.Warnings.Count - 10));
                }
            }

            if (!PromptYesNo(ed, "\nWrite LandXML geometry into the drawing? [Yes/No]", defaultYes: false))
            {
                ed.WriteMessage("\n  Import preview only — no geometry written.\n");
                return;
            }

            string networkOverride = PromptNetworkName(ed, networkGroups);
            LandXmlNetworkImporter.ImportResult write = LandXmlNetworkImporter.ImportToDrawing(
                doc.Database,
                civilDoc,
                import,
                networkOverride);

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: LandXML import write ---\n  Networks: {0}  Structures: {1}  Pipes: {2}  Skipped: {3}\n",
                write.NetworksCreated,
                write.StructuresCreated,
                write.PipesCreated,
                write.Skipped));

            foreach (string err in write.Errors.Take(10))
                ed.WriteMessage("  " + err + "\n");
            if (write.Errors.Count > 10)
            {
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "  ... and {0} more error(s).\n",
                    write.Errors.Count - 10));
            }

            ed.WriteMessage("\n");
        }

        [CommandMethod("HC_LANDXML")]
        public void ExportLandXml()
        {
            Document doc = Active();
            Editor ed = doc.Editor;
            CivilDocument civilDoc = CivilApplication.ActiveDocument;

            var pipes = PipeNetworkReader.ReadAll(doc.Database, civilDoc);
            if (pipes.Count == 0)
            {
                ed.WriteMessage("\nNo pipe networks found in this drawing.\n");
                return;
            }

            string drawingName = ReportWriterCommon.SanitizeFileName(
                Path.GetFileNameWithoutExtension(doc.Name));
            Directory.CreateDirectory(ReportWriterCommon.OutputFolder);
            string defaultPath = Path.Combine(
                ReportWriterCommon.OutputFolder,
                drawingName + "_network.xml");

            string outputPath = PromptOutputPath(ed, defaultPath);
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                ed.WriteMessage("\nLandXML export cancelled.\n");
                return;
            }

            List<LandXmlPipeRecord> pipeRecords = pipes.Select(MapPipe).ToList();
            List<LandXmlStructureRecord> structureRecords = BuildStructureRecords(
                doc.Database, civilDoc, pipes);

            LandXmlWriter.Write(
                outputPath,
                pipeRecords,
                structureRecords,
                projectName: drawingName);

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: LandXML export ---\n  Pipes: {0}\n  Structures: {1}\n  File: {2}\n",
                pipeRecords.Count,
                structureRecords.Count,
                outputPath));
        }

        private static LandXmlPipeRecord MapPipe(ReadPipe rp)
        {
            double? designQ = rp.Segment.DesignFlowCfs > 0
                ? rp.Segment.DesignFlowCfs
                : (double?)null;

            LandXmlPipeShape shape = PipeShapeResolver.ToLandXmlShape(rp.Segment.Shape);

            return new LandXmlPipeRecord
            {
                Name = rp.PipeName,
                NetworkName = rp.NetworkName,
                LengthFt = rp.LengthFt,
                DiameterFt = rp.Segment.DiameterFt,
                Slope = rp.Segment.Slope,
                StartInvertFt = rp.StartInvertFt,
                EndInvertFt = rp.EndInvertFt,
                ManningN = rp.Segment.ManningN,
                DesignFlowCfs = designQ,
                StartStructureName = rp.StartStructureName,
                EndStructureName = rp.EndStructureName,
                Shape = shape,
                WidthFt = rp.Segment.WidthFt > 0 ? rp.Segment.WidthFt : rp.Segment.SpanFt,
                HeightFt = rp.Segment.HeightFt > 0 ? rp.Segment.HeightFt : rp.Segment.RiseFt,
            };
        }

        private static List<LandXmlStructureRecord> BuildStructureRecords(
            Database db,
            CivilDocument civilDoc,
            IReadOnlyList<ReadPipe> pipes)
        {
            var nodes = ValidateCommands.ReadStructureNodes(db, civilDoc, pipes);
            Dictionary<string, string> names = NetworkPipeLinkMapper.StructureNamesFromPipes(pipes);
            Dictionary<string, (double Northing, double Easting, string NetworkName)> positions =
                ReadStructurePositions(db, civilDoc, pipes);

            var records = new List<LandXmlStructureRecord>();
            foreach (KeyValuePair<string, ReviewNodeInput> pair in nodes)
            {
                string id = pair.Key;
                ReviewNodeInput node = pair.Value;
                positions.TryGetValue(id, out (double Northing, double Easting, string NetworkName) pos);

                string name = names.TryGetValue(id, out string? mappedName) && !string.IsNullOrWhiteSpace(mappedName)
                    ? mappedName
                    : node.Id;

                records.Add(new LandXmlStructureRecord
                {
                    Name = name,
                    NetworkName = pos.NetworkName,
                    RimFt = node.RimFt,
                    InvertFt = node.InvertFt,
                    NorthingFt = pos.Northing != 0 || pos.Easting != 0 ? pos.Northing : (double?)null,
                    EastingFt = pos.Northing != 0 || pos.Easting != 0 ? pos.Easting : (double?)null,
                });
            }

            return records
                .OrderBy(r => r.NetworkName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static Dictionary<string, (double Northing, double Easting, string NetworkName)> ReadStructurePositions(
            Database db,
            CivilDocument civilDoc,
            IReadOnlyList<ReadPipe> pipes)
        {
            var networkByStructure = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (ReadPipe rp in pipes)
            {
                TrackStructureNetwork(networkByStructure, rp.StartStructureId, rp.NetworkName);
                TrackStructureNetwork(networkByStructure, rp.EndStructureId, rp.NetworkName);
                TrackStructureNetwork(networkByStructure, rp.UpstreamStructureId, rp.NetworkName);
                TrackStructureNetwork(networkByStructure, rp.DownstreamStructureId, rp.NetworkName);
            }

            var positions = new Dictionary<string, (double Northing, double Easting, string NetworkName)>(
                StringComparer.OrdinalIgnoreCase);
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

                        string id = sid.Handle.ToString();
                        Point3d location = structure.Location;
                        string networkName = networkByStructure.TryGetValue(id, out string? mappedNet)
                            && !string.IsNullOrWhiteSpace(mappedNet)
                            ? mappedNet
                            : netName;

                        positions[id] = (location.Y, location.X, networkName);
                    }
                }

                tr.Commit();
            }

            return positions;
        }

        private static void TrackStructureNetwork(
            Dictionary<string, string> networkByStructure,
            ObjectId structureId,
            string networkName)
        {
            if (structureId.IsNull || string.IsNullOrWhiteSpace(networkName)) return;
            string id = structureId.Handle.ToString();
            if (!networkByStructure.ContainsKey(id))
                networkByStructure[id] = networkName;
        }

        private static string PromptOutputPath(Editor ed, string defaultPath)
        {
            var opts = new PromptStringOptions("\nLandXML output file path")
            {
                DefaultValue = defaultPath,
                UseDefaultValue = true,
                AllowSpaces = true,
            };
            PromptResult res = ed.GetString(opts);
            if (res.Status != PromptStatus.OK)
                return "";

            string path = string.IsNullOrWhiteSpace(res.StringResult) ? defaultPath : res.StringResult.Trim();
            if (!path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                path += ".xml";
            return path;
        }

        private static string PromptInputPath(Editor ed, string defaultPath)
        {
            var opts = new PromptStringOptions("\nLandXML input file path")
            {
                DefaultValue = defaultPath,
                UseDefaultValue = true,
                AllowSpaces = true,
            };
            PromptResult res = ed.GetString(opts);
            if (res.Status != PromptStatus.OK)
                return "";

            return string.IsNullOrWhiteSpace(res.StringResult) ? defaultPath : res.StringResult.Trim();
        }

        private static string Trim(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Length <= max ? value : value.Substring(0, max - 1) + "~";
        }

        private static bool PromptYesNo(Editor ed, string message, bool defaultYes)
        {
            var opts = new PromptKeywordOptions(message)
            {
                AllowNone = true,
            };
            opts.Keywords.Add("Yes");
            opts.Keywords.Add("No");
            opts.Keywords.Default = defaultYes ? "Yes" : "No";
            PromptResult res = ed.GetKeywords(opts);
            if (res.Status != PromptStatus.OK)
                return defaultYes;
            return string.Equals(res.StringResult, "Yes", StringComparison.OrdinalIgnoreCase);
        }

        private static string? PromptNetworkName(
            Editor ed,
            IReadOnlyList<IGrouping<string, LandXmlPipeRecord>> networkGroups)
        {
            if (networkGroups.Count == 1)
                return networkGroups[0].Key;

            ed.WriteMessage("\n  LandXML contains multiple networks. Enter name for imported network");
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  (default: {0}): ", networkGroups[0].Key));

            var opts = new PromptStringOptions("\nImported network name")
            {
                DefaultValue = networkGroups[0].Key,
                UseDefaultValue = true,
                AllowSpaces = true,
            };
            PromptResult res = ed.GetString(opts);
            if (res.Status != PromptStatus.OK)
                return networkGroups[0].Key;

            return string.IsNullOrWhiteSpace(res.StringResult)
                ? networkGroups[0].Key
                : res.StringResult.Trim();
        }

        private static Document Active()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) throw new InvalidOperationException("No active drawing.");
            return doc;
        }
    }
}