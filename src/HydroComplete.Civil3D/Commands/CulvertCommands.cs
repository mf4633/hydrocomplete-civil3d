using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using HydroComplete.Civil3D.Reading;
using HydroComplete.Engine;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace HydroComplete.Civil3D.Commands
{
    /// <summary>HC_CULVERT — FHWA HDS-5 simplified circular culvert headwater.</summary>
    public sealed class CulvertCommands
    {
        [CommandMethod("HC_CULVERT")]
        public void CulvertHeadwaterCommand()
        {
            Document doc = Active();
            Editor ed = doc.Editor;
            CivilDocument civilDoc = CivilApplication.ActiveDocument;

            var pipes = PipeNetworkReader.ReadAll(doc.Database, civilDoc);
            ReadPipe? selected = PromptPipe(ed, pipes);

            CulvertHydraulics.CulvertParameters culvert;
            string sourceLabel;
            if (selected != null)
            {
                culvert = new CulvertHydraulics.CulvertParameters
                {
                    DiameterIn = selected.Segment.DiameterFt * 12.0,
                    LengthFt = selected.LengthFt,
                    SlopeFtPerFt = selected.Segment.Slope > 0 ? selected.Segment.Slope : 0.01,
                    ManningN = selected.Segment.ManningN > 0
                        ? selected.Segment.ManningN
                        : PipeNetworkReader.DefaultManningN,
                };
                sourceLabel = $"{selected.NetworkName}/{selected.PipeName}";
            }
            else
            {
                culvert = new CulvertHydraulics.CulvertParameters
                {
                    DiameterIn = PromptDouble(ed, "\nCulvert diameter, in", 24.0),
                    LengthFt = PromptDouble(ed, "\nCulvert length, ft", 100.0),
                    SlopeFtPerFt = PromptDouble(ed, "\nCulvert slope, ft/ft", 0.01),
                    ManningN = PromptDouble(ed, "\nManning n", PipeNetworkReader.DefaultManningN),
                    EntranceLossKe = PromptDouble(ed, "\nEntrance loss Ke", 0.5),
                };
                sourceLabel = "manual entry";
            }

            double tailwaterFt = PromptDouble(ed, "\nTailwater depth above outlet invert, ft", 0.0);
            double designQ = PromptDesignFlow(ed, doc, civilDoc, pipes);

            CulvertHydraulics.HeadwaterResult result =
                CulvertHydraulics.Headwater(designQ, culvert, tailwaterFt);

            double inletElev = selected?.UpstreamInvertFt ?? 0.0;
            double hwElev = inletElev + result.HeadwaterFt;

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: culvert headwater ({0}, FHWA HDS-5) ---",
                sourceLabel));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  D = {0:0.#} in   L = {1:0.##} ft   S = {2:0.#####}   n = {3:0.###}",
                culvert.DiameterIn, culvert.LengthFt, culvert.SlopeFtPerFt, culvert.ManningN));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Q = {0:0.00} cfs   V = {1:0.##} ft/s   TW = {2:0.##} ft",
                result.DischargeCfs, result.VelocityFps, tailwaterFt));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  HW_inlet = {0:0.##} ft   HW_outlet = {1:0.##} ft   HW = {2:0.##} ft ({3} control)",
                result.HeadwaterInletFt, result.HeadwaterOutletFt,
                result.HeadwaterFt, result.Control));
            if (selected != null)
            {
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  Inlet invert = {0:0.##} ft   HW elevation = {1:0.##} ft",
                    inletElev, hwElev));
            }

            WriteCalcSteps(ed, result.Steps);
            ed.WriteMessage("\n");
        }

        private static ReadPipe? PromptPipe(Editor ed, IReadOnlyList<ReadPipe> pipes)
        {
            if (pipes.Count == 0)
                return null;

            if (!PromptYesNo(ed, "\nUse pipe from drawing? [Yes/No]", defaultYes: true))
                return null;

            if (pipes.Count == 1)
                return pipes[0];

            var opts = new PromptKeywordOptions("\nSelect pipe") { AllowNone = false };
            var keyToPipe = new Dictionary<string, ReadPipe>(StringComparer.OrdinalIgnoreCase);
            int shown = Math.Min(pipes.Count, 9);
            for (int i = 0; i < shown; i++)
            {
                string key = (i + 1).ToString(CultureInfo.InvariantCulture);
                opts.Keywords.Add(key);
                keyToPipe[key] = pipes[i];
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  [{0}] {1} / {2}  D={3:0.#} in  L={4:0.##} ft",
                    key, pipes[i].NetworkName, pipes[i].PipeName,
                    pipes[i].Segment.DiameterFt * 12.0, pipes[i].LengthFt));
            }

            opts.Keywords.Default = "1";
            PromptResult res = ed.GetKeywords(opts);
            if (res.Status != PromptStatus.OK)
                return pipes[0];

            return keyToPipe.TryGetValue(res.StringResult, out ReadPipe? pipe) ? pipe : pipes[0];
        }

        private static double PromptDesignFlow(
            Editor ed, Document doc, CivilDocument civilDoc, IReadOnlyList<ReadPipe> pipes)
        {
            var catchments = CatchmentReader.ReadAll(doc.Database, civilDoc);
            if (catchments.Count > 0 && pipes.Count > 0
                && PromptYesNo(ed, "\nCompute design Q from catchments (Rational)? [Yes/No]", defaultYes: true))
            {
                DesignFlowContext flow = DesignFlowResolver.Prompt(ed, doc.Database, civilDoc, pipes, doc.Name);
                return flow.DesignFlowCfs;
            }

            return PromptDouble(ed, "\nDesign flow Q, cfs", 10.0);
        }

        private static double PromptDouble(Editor ed, string message, double defaultValue)
        {
            var opts = new PromptDoubleOptions(message)
            {
                DefaultValue = defaultValue,
                UseDefaultValue = true,
                AllowZero = true,
                AllowNegative = false,
            };
            PromptDoubleResult res = ed.GetDouble(opts);
            return res.Status == PromptStatus.OK ? res.Value : defaultValue;
        }

        private static bool PromptYesNo(Editor ed, string message, bool defaultYes)
        {
            var opts = new PromptKeywordOptions(message) { AllowNone = true };
            opts.Keywords.Add("Yes");
            opts.Keywords.Add("No");
            opts.Keywords.Default = defaultYes ? "Yes" : "No";
            PromptResult res = ed.GetKeywords(opts);
            if (res.Status != PromptStatus.OK) return defaultYes;
            return string.Equals(res.StringResult, "Yes", StringComparison.OrdinalIgnoreCase);
        }

        private static void WriteCalcSteps(Editor ed, IEnumerable<CalcStep> steps)
        {
            ed.WriteMessage("\n  Calculation steps:");
            foreach (CalcStep step in steps)
                ed.WriteMessage("\n    " + step);
        }

        private static Document Active()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) throw new InvalidOperationException("No active drawing.");
            return doc;
        }
    }
}