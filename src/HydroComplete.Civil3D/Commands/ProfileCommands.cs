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
using HydroComplete.Civil3D.Reading;
using HydroComplete.Civil3D.Ui;
using HydroComplete.Civil3D.Writing;
using HydroComplete.Engine;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace HydroComplete.Civil3D.Commands
{
    /// <summary>
    /// HC_PROFILE — Hydraflow-style invert, crown, and optional HGL profile plot
    /// (chainage vs elevation) in model space.
    /// </summary>
    public sealed class ProfileCommands
    {
        [CommandMethod("HC_PROFILE")]
        public void DrawProfile()
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

            var networks = NetworkTopology.BuildOrderedNetworks(pipes);
            if (networks.Count == 0)
            {
                ed.WriteMessage("\nNo ordered pipe networks found in this drawing.\n");
                return;
            }

            double defaultDatum = ProfilePlotWriter.DefaultDatumFt(networks[0]);
            double defaultTw = networks[0].OrderedPipes.Count > 0
                ? networks[0].OrderedPipes[networks[0].OrderedPipes.Count - 1].DownstreamInvertFt
                : 0;

            var profileDialog = new ProfileOptionsDialog(networks, defaultDatum, defaultTw);
            if (HydroDialogHost.Show(profileDialog) != true)
            {
                ed.WriteMessage("\nProfile cancelled.\n");
                return;
            }

            NetworkTopology.OrderedNetwork net = profileDialog.SelectedNetwork!;
            double horizScale = profileDialog.HorizontalScale;
            double vertScale = profileDialog.VerticalScale;
            double datum = profileDialog.DatumFt;
            bool includeHgl = profileDialog.IncludeHgl;

            Dictionary<string, ProfilePlotWriter.HglPipeEnds>? pipeHglEnds = null;
            if (includeHgl)
                pipeHglEnds = ComputeHglEndsWithTailwater(
                    ed, doc.Database, civilDoc, pipes, net, profileDialog.TailwaterFt, doc.Name);

            ProfilePlotWriter.ProfilePlotData plotData = ProfilePlotWriter.BuildPlotData(net, pipeHglEnds);

            Point3d insertion = Point3d.Origin;
            bool drawToDrawing = profileDialog.DrawToDrawing;
            if (drawToDrawing)
            {
                Point3d? ins = PromptInsertionPoint(ed);
                if (!ins.HasValue)
                {
                    ed.WriteMessage("\nProfile draw cancelled (insertion point required).\n");
                    if (!profileDialog.ExportDxf) return;
                    drawToDrawing = false;
                }
                else
                {
                    insertion = ins.Value;
                }
            }

            PrintProfileTable(ed, net, plotData, horizScale, vertScale, datum);

            if (drawToDrawing)
            {
                var plotOptions = new ProfilePlotWriter.ProfilePlotOptions
                {
                    InsertionPoint = insertion,
                    DatumElevationFt = datum,
                    HorizontalScale = horizScale,
                    VerticalScale = vertScale,
                    IncludeHgl = includeHgl,
                };

                ProfilePlotWriter.WriteResult write = ProfilePlotWriter.WriteProfile(doc.Database, plotData, plotOptions);
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n--- Drew {0} profile polyline(s) and {1} station label(s) ---",
                    write.PolylinesDrawn, write.LabelsDrawn));
                ed.WriteMessage($"\n  Layers: {ProfilePlotWriter.InvertLayer} (green), {ProfilePlotWriter.CrownLayer} (cyan)");
                if (includeHgl)
                    ed.WriteMessage($", {ProfilePlotWriter.HglLayer} (magenta)");
                foreach (string err in write.Errors)
                    ed.WriteMessage($"\n  {err}");
            }

            if (profileDialog.ExportDxf)
            {
                string drawingName = ReportWriterCommon.SanitizeFileName(Path.GetFileNameWithoutExtension(doc.Name));
                string netSlug = ReportWriterCommon.SanitizeFileName(net.NetworkName);
                Directory.CreateDirectory(ReportWriterCommon.OutputFolder);
                string dxfPath = Path.Combine(
                    ReportWriterCommon.OutputFolder,
                    $"{drawingName}_{netSlug}_profile.dxf");

                ProfileDxfWriter.Write(dxfPath, ProfilePlotWriter.ToDxfData(plotData),
                    new ProfileDxfWriter.ProfileDxfOptions
                    {
                        DatumElevationFt = datum,
                        HorizontalScale = horizScale,
                        VerticalScale = vertScale,
                        IncludeHgl = includeHgl,
                        TextHeight = Math.Max(0.05, 2.0 / Math.Max(vertScale, 1e-6)),
                    });
                ed.WriteMessage($"\n  DXF: {dxfPath}");
            }

            ed.WriteMessage("\n");
        }

        private static void PrintProfileTable(
            Editor ed,
            NetworkTopology.OrderedNetwork net,
            ProfilePlotWriter.ProfilePlotData plotData,
            double horizScale,
            double vertScale,
            double datum)
        {
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: profile plot '{0}' ({1} station(s), H={2:0.##} V={3:0.##} ft/dwg-ft, datum={4:0.00} ft) ---",
                net.NetworkName,
                plotData.Stations.Count,
                horizScale,
                vertScale,
                datum));
            ed.WriteMessage("\n  Station(ft)  Structure           Invert(ft)  Crown(ft)  HGL(ft)");

            foreach (ProfilePlotWriter.ProfileStation station in plotData.Stations)
            {
                string hglCol = station.HglFt.HasValue
                    ? station.HglFt.Value.ToString("0.00", CultureInfo.InvariantCulture)
                    : "   —   ";
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  {0,10:0.0}  {1,-18} {2,10:0.00}  {3,10:0.00}  {4,9}",
                    station.ChainageFt,
                    Trim(station.StructureName, 18),
                    station.InvertFt,
                    station.CrownFt,
                    hglCol));
            }
        }

        [CommandMethod("HC_PROFILE_DXF")]
        public void ExportProfileDxf()
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

            var networks = NetworkTopology.BuildOrderedNetworks(pipes);
            if (networks.Count == 0)
            {
                ed.WriteMessage("\nNo ordered pipe networks found in this drawing.\n");
                return;
            }

            NetworkTopology.OrderedNetwork? net = PromptNetwork(ed, networks);
            if (net == null || net.OrderedPipes.Count == 0)
            {
                ed.WriteMessage("\nProfile DXF export cancelled or network has no pipes.\n");
                return;
            }

            double horizScale = PromptDouble(ed, "\nHorizontal scale (ft chainage per drawing ft)", 20.0);
            double vertScale = PromptDouble(ed, "Vertical scale (ft elevation per drawing ft)", 20.0);
            double defaultDatum = ProfilePlotWriter.DefaultDatumFt(net);
            double datum = PromptDatum(ed, defaultDatum);
            bool includeHgl = PromptYesNo(ed, "\nInclude HGL", defaultYes: false);

            Dictionary<string, ProfilePlotWriter.HglPipeEnds>? pipeHglEnds = null;
            if (includeHgl)
            {
                pipeHglEnds = ComputeHglEnds(ed, doc.Database, civilDoc, pipes, net, doc.Name);
                if (pipeHglEnds == null)
                {
                    ed.WriteMessage("\nHGL computation cancelled; exporting invert/crown only.\n");
                    includeHgl = false;
                }
            }

            string drawingName = ReportWriterCommon.SanitizeFileName(
                Path.GetFileNameWithoutExtension(doc.Name));
            string netSlug = ReportWriterCommon.SanitizeFileName(net.NetworkName);
            Directory.CreateDirectory(ReportWriterCommon.OutputFolder);
            string defaultPath = Path.Combine(
                ReportWriterCommon.OutputFolder,
                $"{drawingName}_{netSlug}_profile.dxf");

            string outputPath = PromptOutputPath(ed, defaultPath);
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                ed.WriteMessage("\nProfile DXF export cancelled.\n");
                return;
            }

            ProfilePlotWriter.ProfilePlotData plotData = ProfilePlotWriter.BuildPlotData(net, pipeHglEnds);
            ProfileDxfWriter.ProfileDxfData dxfData = ProfilePlotWriter.ToDxfData(plotData);
            var dxfOptions = new ProfileDxfWriter.ProfileDxfOptions
            {
                DatumElevationFt = datum,
                HorizontalScale = horizScale,
                VerticalScale = vertScale,
                IncludeHgl = includeHgl,
                TextHeight = Math.Max(0.05, 2.0 / Math.Max(vertScale, 1e-6)),
            };

            ProfileDxfWriter.Write(outputPath, dxfData, dxfOptions);

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: profile DXF export ---\n  Network: {0}\n  Stations: {1}\n  H={2:0.##} V={3:0.##} ft/dwg-ft  datum={4:0.00} ft\n  File: {5}\n",
                net.NetworkName,
                plotData.Stations.Count,
                horizScale,
                vertScale,
                datum,
                outputPath));
            ed.WriteMessage("\n  Layers: HC-PROFILE-INVERT, HC-PROFILE-CROWN");
            if (includeHgl)
                ed.WriteMessage(", HC-PROFILE-HGL");
            ed.WriteMessage("\n");
        }

        private static string PromptOutputPath(Editor ed, string defaultPath)
        {
            var opts = new PromptStringOptions("\nProfile DXF output file path")
            {
                DefaultValue = defaultPath,
                UseDefaultValue = true,
                AllowSpaces = true,
            };
            PromptResult res = ed.GetString(opts);
            if (res.Status != PromptStatus.OK)
                return "";

            string path = string.IsNullOrWhiteSpace(res.StringResult) ? defaultPath : res.StringResult.Trim();
            if (!path.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase))
                path += ".dxf";
            return path;
        }

        private static Dictionary<string, ProfilePlotWriter.HglPipeEnds>? ComputeHglEndsWithTailwater(
            Editor ed,
            Database db,
            CivilDocument civilDoc,
            IReadOnlyList<ReadPipe> allPipes,
            NetworkTopology.OrderedNetwork net,
            double tailwaterFt,
            string drawingName)
        {
            DesignFlowContext flow = DesignFlowResolver.Prompt(ed, db, civilDoc, allPipes, drawingName);
            bool useMinorLosses = PromptYesNo(ed, "\nInclude HEC-22 junction/exit losses", defaultYes: true);
            bool useMomentumJunction = PromptYesNo(ed, "\nUse momentum junction losses? [Yes/No]", defaultYes: false);

            var hglOptions = new HglProfileOptions
            {
                IncludeJunctionLosses = useMinorLosses,
                IncludeExitLoss = useMinorLosses,
                UseMomentumJunction = useMomentumJunction,
                UseBendLoss = useMinorLosses,
            };

            List<NetworkReach> reaches = flow.IsRouted && flow.PipeFlowCfs != null
                ? NetworkTopology.BuildReaches(net.OrderedPipes, flow.PipeFlowCfs, useMinorLosses)
                : NetworkTopology.BuildReaches(net.OrderedPipes, flow.DesignFlowCfs, useMinorLosses);

            List<HglProfilePoint> profile = Hgl.SteadyBackwaterFromOutfall(reaches, tailwaterFt, hglOptions);

            var pipeHglEnds = new Dictionary<string, ProfilePlotWriter.HglPipeEnds>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < net.OrderedPipes.Count && i < profile.Count; i++)
            {
                HglProfilePoint point = profile[i];
                string reachName = reaches[i].Name;
                pipeHglEnds[reachName] = new ProfilePlotWriter.HglPipeEnds
                {
                    HglUsFt = point.HglUpstreamFt,
                    HglDsFt = point.HglFt,
                };
            }

            return pipeHglEnds;
        }

        private static Dictionary<string, ProfilePlotWriter.HglPipeEnds>? ComputeHglEnds(
            Editor ed,
            Database db,
            CivilDocument civilDoc,
            IReadOnlyList<ReadPipe> allPipes,
            NetworkTopology.OrderedNetwork net,
            string drawingName)
        {
            DesignFlowContext flow = DesignFlowResolver.Prompt(ed, db, civilDoc, allPipes, drawingName);
            bool useMinorLosses = PromptYesNo(ed, "\nInclude HEC-22 junction/exit losses", defaultYes: true);
            bool useMomentumJunction = PromptYesNo(ed, "\nUse momentum junction losses? [Yes/No]", defaultYes: false);

            var hglOptions = new HglProfileOptions
            {
                IncludeJunctionLosses = useMinorLosses,
                IncludeExitLoss = useMinorLosses,
                UseMomentumJunction = useMomentumJunction,
                UseBendLoss = useMinorLosses,
            };

            List<NetworkReach> reaches = flow.IsRouted && flow.PipeFlowCfs != null
                ? NetworkTopology.BuildReaches(net.OrderedPipes, flow.PipeFlowCfs, useMinorLosses)
                : NetworkTopology.BuildReaches(net.OrderedPipes, flow.DesignFlowCfs, useMinorLosses);

            double tailwater = PromptTailwater(ed, net);
            List<HglProfilePoint> profile = Hgl.SteadyBackwaterFromOutfall(reaches, tailwater, hglOptions);

            var pipeHglEnds = new Dictionary<string, ProfilePlotWriter.HglPipeEnds>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < net.OrderedPipes.Count && i < profile.Count; i++)
            {
                ReadPipe rp = net.OrderedPipes[i];
                HglProfilePoint point = profile[i];
                string reachName = reaches[i].Name;
                pipeHglEnds[reachName] = new ProfilePlotWriter.HglPipeEnds
                {
                    HglUsFt = point.HglUpstreamFt,
                    HglDsFt = point.HglFt,
                };
            }

            return pipeHglEnds;
        }

        private static NetworkTopology.OrderedNetwork? PromptNetwork(
            Editor ed,
            IReadOnlyList<NetworkTopology.OrderedNetwork> networks)
        {
            if (networks.Count == 1)
                return networks[0];

            var opts = new PromptKeywordOptions("\nSelect pipe network")
            {
                AllowNone = false,
            };

            var keyToNetwork = new Dictionary<string, NetworkTopology.OrderedNetwork>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < networks.Count; i++)
            {
                NetworkTopology.OrderedNetwork net = networks[i];
                string key = SanitizeKeyword(net.NetworkName, i);
                if (keyToNetwork.ContainsKey(key))
                    key = $"NET{i + 1}";

                keyToNetwork[key] = net;
                opts.Keywords.Add(key);
            }

            opts.Keywords.Default = keyToNetwork.Keys.First();
            PromptResult res = ed.GetKeywords(opts);
            if (res.Status != PromptStatus.OK)
                return null;

            return keyToNetwork.TryGetValue(res.StringResult, out NetworkTopology.OrderedNetwork? selected)
                ? selected
                : null;
        }

        private static string SanitizeKeyword(string networkName, int index)
        {
            if (string.IsNullOrWhiteSpace(networkName))
                return $"NET{index + 1}";

            char[] chars = networkName
                .Replace(' ', '_')
                .Replace('-', '_')
                .ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_')
                    chars[i] = '_';
            }

            string key = new string(chars).Trim('_');
            if (string.IsNullOrEmpty(key))
                key = $"NET{index + 1}";
            if (key.Length > 32)
                key = key.Substring(0, 32);
            return key;
        }

        private static Point3d? PromptInsertionPoint(Editor ed)
        {
            var opts = new PromptPointOptions("\nProfile insertion point (lower-left of plot)")
            {
                AllowNone = false,
            };
            PromptPointResult res = ed.GetPoint(opts);
            return res.Status == PromptStatus.OK ? res.Value : null;
        }

        private static double PromptDatum(Editor ed, double defaultDatum)
        {
            var opts = new PromptDoubleOptions("\nDatum elevation (ft)")
            {
                DefaultValue = defaultDatum,
                UseDefaultValue = true,
                AllowNegative = true,
                AllowZero = true,
            };
            PromptDoubleResult res = ed.GetDouble(opts);
            return res.Status == PromptStatus.OK ? res.Value : defaultDatum;
        }

        private static double PromptDouble(Editor ed, string message, double dflt)
        {
            var opts = new PromptDoubleOptions(message)
            {
                DefaultValue = dflt,
                UseDefaultValue = true,
                AllowNegative = false,
                AllowZero = false,
            };
            PromptDoubleResult res = ed.GetDouble(opts);
            return res.Status == PromptStatus.OK ? res.Value : dflt;
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
            if (res.Status != PromptStatus.OK) return defaultYes;
            return string.Equals(res.StringResult, "Yes", StringComparison.OrdinalIgnoreCase);
        }

        private static double OutfallTailwaterFt(NetworkTopology.OrderedNetwork net)
        {
            return net.OrderedPipes.Count > 0
                ? net.OrderedPipes[net.OrderedPipes.Count - 1].DownstreamInvertFt
                : 0.0;
        }

        private static double PromptTailwater(Editor ed, NetworkTopology.OrderedNetwork net)
        {
            double outfallInvert = OutfallTailwaterFt(net);
            var opts = new PromptDoubleOptions(
                $"\nOutfall tailwater HGL elevation for '{net.NetworkName}'")
            {
                DefaultValue = outfallInvert,
                UseDefaultValue = true,
                AllowNegative = true,
                AllowZero = true,
            };
            PromptDoubleResult res = ed.GetDouble(opts);
            return res.Status == PromptStatus.OK ? res.Value : outfallInvert;
        }

        private static Document Active()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) throw new InvalidOperationException("No active drawing.");
            return doc;
        }

        private static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max - 1) + "~";
        }
    }
}