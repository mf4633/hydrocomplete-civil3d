using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using HydroComplete.Civil3D.Reading;
using HydroComplete.Civil3D.Writing;
using HydroComplete.Engine;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace HydroComplete.Civil3D.Commands
{
    /// <summary>HC_NETWORK_DIAGRAM — HTML/SVG pipe network schematic from plan topology.</summary>
    public sealed class NetworkDiagramCommands
    {
        [CommandMethod("HC_NETWORK_DIAGRAM")]
        public void NetworkDiagram()
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

            string networkName = PromptNetwork(ed, pipes);
            var structureNames = NetworkPipeLinkMapper.StructureNamesFromPipes(pipes);

            Dictionary<string, NetworkDiagramWriter.PipeDiagramStats>? stats = null;
            if (PromptYesNo(ed, "\nColor pipes by routed capacity (requires catchments)? [Yes/No]", defaultYes: true))
                stats = TryBuildCapacityStats(doc, civilDoc, pipes, structureNames);

            string drawingName = Path.GetFileNameWithoutExtension(doc.Name) ?? "drawing";
            string path = NetworkDiagramWriter.Write(drawingName, networkName, pipes, structureNames, stats);

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: network diagram ---\n  Network: {0}\n  Pipes: {1}\n  HTML: {2}\n",
                networkName,
                pipes.Count(p => string.Equals(p.NetworkName, networkName, StringComparison.OrdinalIgnoreCase)),
                path));
        }

        private static Dictionary<string, NetworkDiagramWriter.PipeDiagramStats>? TryBuildCapacityStats(
            Document doc,
            CivilDocument civilDoc,
            IReadOnlyList<ReadPipe> pipes,
            Dictionary<string, string> structureNames)
        {
            var catchments = CatchmentReader.ReadAll(doc.Database, civilDoc, pipes);
            if (catchments.Count == 0)
                return null;

            DesignFlowResolver.ApplyDefaultTcFallback(catchments, pipes);
            IdfCurve idf = ResolveDefaultIdf(doc.Database);
            var links = NetworkPipeLinkMapper.FromReadPipes(pipes);
            CatchmentFlowRouterResult route = CatchmentFlowRouter.Route(catchments, links, idf, structureNames);
            Dictionary<string, double> qByPipe = route.PipeFlowCfs;

            var stats = new Dictionary<string, NetworkDiagramWriter.PipeDiagramStats>(StringComparer.OrdinalIgnoreCase);
            foreach (ReadPipe pipe in pipes)
            {
                string key = pipe.PipeId.Handle.ToString();
                if (!qByPipe.TryGetValue(key, out double q) || q <= 0)
                    continue;

                Manning.CapacityResult cap = Manning.Capacity(pipe.Segment);
                Manning.NormalDepthResult nd = Manning.NormalDepth(pipe.Segment, q);
                stats[key] = new NetworkDiagramWriter.PipeDiagramStats
                {
                    DesignFlowCfs = q,
                    FlowRatio = cap.FullFlowCfs > 0 ? q / cap.FullFlowCfs : 0,
                    Surcharged = nd.Surcharged,
                };
            }

            return stats.Count > 0 ? stats : null;
        }

        private static IdfCurve ResolveDefaultIdf(Autodesk.AutoCAD.DatabaseServices.Database db)
        {
            DrawingGeolocation.Result? geo = DrawingGeolocation.TryRead(db);
            if (geo != null)
            {
                try
                {
                    return Atlas14Service.Resolve(geo.Lat, geo.Lon).ToCurve();
                }
                catch
                {
                }
            }

            return Atlas14Presets.Nearest(35.23, -80.84).ToCurve();
        }

        private static string PromptNetwork(Editor ed, IReadOnlyList<ReadPipe> pipes)
        {
            var names = pipes.Select(p => p.NetworkName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (names.Count == 1)
                return names[0];

            var opts = new PromptKeywordOptions("\nSelect pipe network")
            {
                AllowNone = true,
            };
            foreach (string name in names)
                opts.Keywords.Add(name);

            opts.Keywords.Default = names[0];
            PromptResult res = ed.GetKeywords(opts);
            return res.Status == PromptStatus.OK ? res.StringResult : names[0];
        }

        private static bool PromptYesNo(Editor ed, string message, bool defaultYes)
        {
            var opts = new PromptKeywordOptions(message) { AllowNone = true };
            opts.Keywords.Add("Yes");
            opts.Keywords.Add("No");
            opts.Keywords.Default = defaultYes ? "Yes" : "No";
            PromptResult res = ed.GetKeywords(opts);
            if (res.Status != PromptStatus.OK)
                return defaultYes;
            return string.Equals(res.StringResult, "Yes", StringComparison.OrdinalIgnoreCase);
        }

        private static Document Active()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) throw new InvalidOperationException("No active drawing.");
            return doc;
        }
    }
}