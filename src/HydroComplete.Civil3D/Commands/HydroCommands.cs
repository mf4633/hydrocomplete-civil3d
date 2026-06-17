using System;
using System.Collections.Generic;
using System.Globalization;
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
            ed.WriteMessage("\n=== HydroComplete for Civil 3D 0.1.1 ===");
            ed.WriteMessage("\n  HC_PIPES       Manning capacity of every pipe-network pipe");
            ed.WriteMessage("\n  HC_PIPES_WRITE Label Qfull/Vfull on layer HC-CAPACITY");
            ed.WriteMessage("\n  HC_RATIONAL    Composite Rational-method peak flow from catchments");
            ed.WriteMessage("\n  HC_ABOUT       This list");
            ed.WriteMessage("\n  Engine: Rational, SCS Tc, Manning, IDF — public-domain, fully shown.\n");
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

            // IDF coefficients i = a/(t+b)^c. Defaults are placeholders — replace
            // with NOAA Atlas 14 values for the project site and return period.
            double a = PromptDouble(ed, "\nIDF coefficient a", 120.0);
            double b = PromptDouble(ed, "IDF coefficient b", 12.0);
            double c = PromptDouble(ed, "IDF coefficient c", 0.85);
            var idf = new IdfCurve(a, b, c);

            // System Tc governs the design intensity: use the longest catchment Tc.
            double systemTc = 0.0;
            foreach (var cm in catchments) systemTc = Math.Max(systemTc, cm.TcMinutes);

            var intensity = idf.Intensity(systemTc);
            var q = Rational.Peak(catchments, intensity.IntensityInHr);

            ed.WriteMessage($"\n--- HydroComplete: Rational method ({catchments.Count} catchments) ---");
            foreach (var cm in catchments)
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  {0,-20} A={1,7:0.000} ac  C={2,4:0.00}  Tc={3,5:0.0} min",
                    Trim(cm.Name, 20), cm.AreaAcres, cm.RunoffC, cm.TcMinutes));

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  System Tc = {0:0.0} min  ->  i = {1:0.000} in/hr", systemTc, intensity.IntensityInHr));
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

        private static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max - 1) + "~";
        }
    }
}
