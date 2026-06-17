using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using HydroComplete.Civil3D.Reading;
using HydroComplete.Civil3D.Writing;
using HydroComplete.Engine;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace HydroComplete.Civil3D.Commands
{
    /// <summary>
    /// The HC_* command set. v0 reads geometry from the active Civil 3D drawing,
    /// runs the engine, and prints a formula-transparent report to the command
    /// line. Writing results back to the drawing as labels/profiles is the next
    /// increment.
    /// </summary>
    public sealed class HydroCommands
    {
        [CommandMethod("HC_ABOUT")]
        public void About()
        {
            Editor ed = Active().Editor;
            ed.WriteMessage("\n=== HydroComplete for Civil 3D 0.3.0 ===");
            ed.WriteMessage("\n  HC_PIPES       Manning capacity of every pipe-network pipe");
            ed.WriteMessage("\n  HC_PIPES_WRITE Label Qfull/Vfull on layer HC-CAPACITY");
            ed.WriteMessage("\n  HC_HGL         Steady HGL + HEC-22 junction losses + HC-HGL labels");
            ed.WriteMessage("\n  HC_REPORT      Export formula-transparent HTML Manning report");
            ed.WriteMessage("\n  HC_RATIONAL    Rational Q from catchments + NOAA Atlas 14 IDF presets");
            ed.WriteMessage("\n  HC_ATLAS14     List embedded Atlas 14 IDF presets by city");
            ed.WriteMessage("\n  HC_ABOUT       This list");
            ed.WriteMessage("\n  Engine: Rational, SCS Tc, Manning, IDF, HEC-22 — public-domain, fully shown.\n");
        }

        [CommandMethod("HC_ATLAS14")]
        public void Atlas14List()
        {
            IdfPrompts.WriteAtlas14List(Active().Editor);
        }

        [CommandMethod("HC_PIPES")]
        public void Pipes()
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

            ed.WriteMessage($"\n--- HydroComplete: Manning capacity ({pipes.Count} pipes) ---");
            ed.WriteMessage("\nNetwork / Pipe            Dia(ft)  Slope    Q_full(cfs)  V_full(fps)");
            foreach (var rp in pipes)
            {
                try
                {
                    var cap = Manning.Capacity(rp.Segment);
                    ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                        "\n{0,-24} {1,6:0.00}  {2,6:0.0000}  {3,10:0.00}  {4,10:0.00}",
                        Trim(rp.NetworkName + "/" + rp.PipeName, 24),
                        rp.Segment.DiameterFt, rp.Segment.Slope,
                        cap.FullFlowCfs, cap.FullVelocityFps));
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\n{Trim(rp.PipeName, 24),-24} (skipped: {ex.Message})");
                }
            }
            ed.WriteMessage("\n");
        }

        [CommandMethod("HC_PIPES_WRITE")]
        public void PipesWrite()
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

            var capacities = new Dictionary<ObjectId, Manning.CapacityResult>();
            foreach (var rp in pipes)
            {
                try
                {
                    capacities[rp.PipeId] = Manning.Capacity(rp.Segment);
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\nSkipping {rp.PipeName}: {ex.Message}");
                }
            }

            var write = PipeNetworkWriter.WriteCapacities(doc.Database, pipes, capacities);
            ed.WriteMessage($"\n--- HydroComplete: wrote capacity to {write.Updated} pipe(s) ---");
            if (write.Skipped > 0)
                ed.WriteMessage($"\n  Skipped {write.Skipped} pipe(s).");
            foreach (string err in write.Errors)
                ed.WriteMessage($"\n  {err}");
            ed.WriteMessage("\n  Labels placed on layer HC-CAPACITY at each pipe midpoint.\n");
        }

        [CommandMethod("HC_HGL")]
        public void HglProfile()
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

            double designQ = PromptDesignFlow(ed, doc, civilDoc);
            bool useMinorLosses = PromptYesNo(ed, "\nInclude HEC-22 junction/exit losses", defaultYes: true);

            var hglOptions = new HglProfileOptions
            {
                IncludeJunctionLosses = useMinorLosses,
                IncludeExitLoss = useMinorLosses,
            };

            var networks = NetworkTopology.BuildOrderedNetworks(pipes);
            var allMidHgl = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            string lossNote = useMinorLosses ? " + HEC-22" : "";
            ed.WriteMessage($"\n--- HydroComplete: steady HGL{lossNote} (Q={designQ:0.0} cfs) ---");
            ed.WriteMessage("\nNetwork                 Pipe              HGL_US(ft)  HGL_DS(ft)  HGL_mid(ft)  h_m(ft)");

            foreach (var net in networks)
            {
                if (net.OrderedPipes.Count == 0) continue;

                var reaches = NetworkTopology.BuildReaches(
                    net.OrderedPipes, designQ, useMinorLosses);

                double startHgl = net.MaxUpstreamInvertFt + 1.0;
                var profile = Hgl.SteadyNetworkHglProfile(reaches, startHgl, hglOptions);
                double hglUs = startHgl;

                for (int i = 0; i < net.OrderedPipes.Count && i < profile.Count; i++)
                {
                    ReadPipe rp = net.OrderedPipes[i];
                    HglProfilePoint point = profile[i];
                    string reachName = reaches[i].Name;

                    double hglDs = point.HglFt;
                    double hglMid = 0.5 * (hglUs + hglDs);
                    allMidHgl[reachName] = hglMid;

                    ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                        "\n{0,-22} {1,-16} {2,10:0.00}  {3,10:0.00}  {4,10:0.00}  {5,8:0.00}",
                        Trim(net.NetworkName, 22),
                        Trim(rp.PipeName, 16),
                        hglUs, hglDs, hglMid, point.HmFt));

                    hglUs = hglDs;
                }
            }

            var write = HglLabelWriter.WriteHglLabels(doc.Database, pipes, allMidHgl);
            ed.WriteMessage($"\n--- Wrote HGL labels to {write.Updated} pipe(s) on layer HC-HGL ---");
            if (write.Skipped > 0)
                ed.WriteMessage($"\n  Skipped {write.Skipped} pipe(s).");
            foreach (string err in write.Errors)
                ed.WriteMessage($"\n  {err}");
            ed.WriteMessage("\n");
        }

        [CommandMethod("HC_REPORT")]
        public void Report()
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

            var capacities = new Dictionary<ObjectId, Manning.CapacityResult>();
            foreach (var rp in pipes)
            {
                try
                {
                    capacities[rp.PipeId] = Manning.Capacity(rp.Segment);
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\nSkipping {rp.PipeName}: {ex.Message}");
                }
            }

            string drawingName = Path.GetFileNameWithoutExtension(doc.Name);
            if (string.IsNullOrWhiteSpace(drawingName))
                drawingName = "untitled";

            string reportPath = HtmlReportWriter.Write(drawingName, pipes, capacities);
            ed.WriteMessage($"\n--- HydroComplete: HTML report written ---");
            ed.WriteMessage($"\n  {reportPath}\n");
        }

        [CommandMethod("HC_RATIONAL")]
        public void RationalMethod()
        {
            Document doc = Active();
            Editor ed = doc.Editor;
            CivilDocument civilDoc = CivilApplication.ActiveDocument;

            var catchments = CatchmentReader.ReadAll(doc.Database, civilDoc);
            if (catchments.Count == 0)
            {
                ed.WriteMessage("\nNo catchments found in this drawing.\n");
                return;
            }

            Atlas14Presets.Preset? preset = IdfPrompts.PromptPreset(ed);
            Rational.PeakFlowResult q;
            double systemTc = 0.0;
            foreach (var cm in catchments) systemTc = Math.Max(systemTc, cm.TcMinutes);

            if (preset != null)
            {
                q = Atlas14Presets.PeakFromCatchments(catchments, preset);
                ed.WriteMessage($"\n  IDF preset: {preset.DisplayName} ({preset.Key}, {preset.ReturnPeriodYears}-yr)\n");
            }
            else
            {
                IdfCurve idf = IdfPrompts.PromptCustomIdfCurve(ed);
                var intensity = idf.Intensity(systemTc);
                q = Rational.Peak(catchments, intensity.IntensityInHr);
            }

            ed.WriteMessage($"\n--- HydroComplete: Rational method ({catchments.Count} catchments) ---");
            foreach (var cm in catchments)
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  {0,-20} A={1,7:0.000} ac  C={2,4:0.00}  Tc={3,5:0.0} min",
                    Trim(cm.Name, 20), cm.AreaAcres, cm.RunoffC, cm.TcMinutes));

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  System Tc = {0:0.0} min  ->  i = {1:0.000} in/hr", systemTc, q.IntensityInHr));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Composite C = {0:0.000}   Total area = {1:0.000} ac", q.CompositeC, q.TotalAreaAcres));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  PEAK FLOW Q = {0:0.00} cfs   (Q = C*i*A)\n", q.PeakFlowCfs));
        }

        private static Document Active()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) throw new InvalidOperationException("No active drawing.");
            return doc;
        }

        private static double PromptDesignFlow(Editor ed, Document doc, CivilDocument civilDoc)
        {
            var catchments = CatchmentReader.ReadAll(doc.Database, civilDoc);
            if (catchments.Count > 0 && PromptYesNo(ed, "\nUse Rational Q from catchments + Atlas 14 IDF", defaultYes: true))
            {
                Atlas14Presets.Preset? preset = IdfPrompts.PromptPreset(ed);
                if (preset == null)
                {
                    IdfCurve idf = IdfPrompts.PromptCustomIdfCurve(ed);
                    double systemTc = 0.0;
                    foreach (var cm in catchments) systemTc = Math.Max(systemTc, cm.TcMinutes);
                    var intensity = idf.Intensity(systemTc);
                    var q = Rational.Peak(catchments, intensity.IntensityInHr);
                    ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                        "\n  Rational design Q = {0:0.00} cfs ({1} catchments, Tc={2:0.0} min)\n",
                        q.PeakFlowCfs, catchments.Count, systemTc));
                    return q.PeakFlowCfs;
                }

                var peak = Atlas14Presets.PeakFromCatchments(catchments, preset);
                double tc = 0.0;
                foreach (var cm in catchments) tc = Math.Max(tc, cm.TcMinutes);
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  Rational design Q = {0:0.00} cfs ({1}, {2} catchments, Tc={3:0.0} min)\n",
                    peak.PeakFlowCfs, preset.DisplayName, catchments.Count, tc));
                return peak.PeakFlowCfs;
            }

            return PromptDouble(ed, "\nUniform design flow Q (cfs)", 10.0);
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

        private static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max - 1) + "~";
        }
    }
}
