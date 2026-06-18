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
    /// <summary>HC_LANDXML — export pipe network geometry and hydraulics to LandXML 1.2.</summary>
    public sealed class LandXmlCommands
    {
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

        private static Document Active()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) throw new InvalidOperationException("No active drawing.");
            return doc;
        }
    }
}