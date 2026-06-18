using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using HydroComplete.Civil3D.Writing;
using HydroComplete.Engine;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace HydroComplete.Civil3D.Commands
{
    /// <summary>HC_GVF — gradually varied flow water surface profile (Standard Step Method).</summary>
    public sealed class GvfCommands
    {
        private const int MaxStations = 20;

        [CommandMethod("HC_GVF")]
        public void GraduallyVariedFlowProfile()
        {
            Document doc = Active();
            Editor ed = doc.Editor;

            ed.WriteMessage("\n=== HydroComplete: Gradually Varied Flow (Standard Step) ===");
            ed.WriteMessage("\n  Trapezoidal channel — Manning friction, energy equation station-by-station.\n");

            var channel = new GraduallyVariedFlow.ChannelParameters
            {
                BottomWidthFt = PromptDouble(ed, "\nBottom width b, ft", 10.0),
                SideSlopeZ = PromptDouble(ed, "\nSide slope z (H:V)", 2.0),
                ManningN = PromptDouble(ed, "\nManning n", 0.03),
                BedSlopeFtPerFt = PromptDouble(ed, "\nBed slope S0, ft/ft", 0.001),
            };

            double flowCfs = PromptDouble(ed, "\nDischarge Q, cfs", 100.0);
            var boundary = PromptBoundary(ed, out double knownDepthFt);
            var stations = PromptStations(ed);

            if (stations.Count == 0)
            {
                ed.WriteMessage("\n  No stations entered.\n");
                return;
            }

            GraduallyVariedFlow.ProfileResult result;
            try
            {
                result = GraduallyVariedFlow.ComputeWaterSurfaceProfile(
                    flowCfs, channel, boundary, knownDepthFt, stations);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  GVF error: {0}\n", ex.Message));
                return;
            }

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- GVF profile ({0}) ---", result.ProfileType));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  y_n = {0:0.####} ft   y_c = {1:0.####} ft   march = {2}",
                result.NormalDepthFt, result.CriticalDepthFt,
                result.IsSubcritical ? "upstream (subcritical)" : "downstream (supercritical)"));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Boundary depth = {0:0.####} ft ({1})\n",
                result.BoundaryDepthFt, result.BoundaryType));

            ed.WriteMessage("\n  Station      Invert     Depth      WSE        V        Fr       Regime");
            foreach (GraduallyVariedFlow.ProfilePoint pt in result.Profile)
            {
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  {0,8:0.##}   {1,8:0.##}   {2,8:0.####}   {3,8:0.##}   {4,6:0.##}   {5,6:0.###}   {6}",
                    pt.StationFt, pt.InvertElevFt, pt.DepthFt, pt.WaterSurfaceElevFt,
                    pt.VelocityFps, pt.FroudeNumber, pt.FlowRegime));
            }

            WriteCalcSteps(ed, result.Steps);

            OfferGvfHtmlExport(ed, doc, flowCfs, channel, boundary, knownDepthFt, result);
            ed.WriteMessage("\n");
        }

        private static void OfferGvfHtmlExport(
            Editor ed,
            Document doc,
            double flowCfs,
            GraduallyVariedFlow.ChannelParameters channel,
            GraduallyVariedFlow.GvfBoundaryType boundary,
            double knownDepthFt,
            GraduallyVariedFlow.ProfileResult result)
        {
            if (!PromptYesNo(ed, "\nExport GVF section to HTML report? [Yes/No]", defaultYes: false))
                return;

            string boundaryDesc = boundary switch
            {
                GraduallyVariedFlow.GvfBoundaryType.Normal => "normal depth",
                GraduallyVariedFlow.GvfBoundaryType.Critical => "critical depth",
                _ => string.Format(CultureInfo.InvariantCulture, "known depth = {0:0.####} ft", knownDepthFt),
            };

            var data = new GvfReportData
            {
                FlowCfs = flowCfs,
                BottomWidthFt = channel.BottomWidthFt,
                SideSlopeZ = channel.SideSlopeZ,
                ManningN = channel.ManningN,
                BedSlopeFtPerFt = channel.BedSlopeFtPerFt,
                BoundaryDescription = boundaryDesc,
                Result = result,
            };

            string path = HtmlReportWriter.WriteGvf(doc.Name, data);
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  HTML report written: {0}", path));
        }

        private static List<GraduallyVariedFlow.Station> PromptStations(Editor ed)
        {
            var stations = new List<GraduallyVariedFlow.Station>();
            ed.WriteMessage("\n  Enter profile stations (distance + invert elevation). Type Done when finished.\n");

            while (stations.Count < MaxStations)
            {
                if (stations.Count > 0 && PromptDone(ed))
                    break;

                int n = stations.Count + 1;
                double distance = PromptDouble(ed, $"\nStation {n} chainage, ft", stations.Count * 100.0);
                double invert = PromptDouble(ed, $"\nStation {n} invert elevation, ft", 100.0 - stations.Count * 0.1);
                stations.Add(new GraduallyVariedFlow.Station
                {
                    DistanceFt = distance,
                    InvertElevFt = invert,
                });
            }

            return stations.OrderBy(s => s.DistanceFt).ToList();
        }

        private static GraduallyVariedFlow.GvfBoundaryType PromptBoundary(Editor ed, out double knownDepthFt)
        {
            knownDepthFt = 0.0;
            var opts = new PromptKeywordOptions("\nDownstream boundary condition [Normal/Critical/Known]")
            {
                AllowNone = false,
            };
            opts.Keywords.Add("Normal");
            opts.Keywords.Add("Critical");
            opts.Keywords.Add("Known");
            opts.Keywords.Default = "Normal";

            PromptResult res = ed.GetKeywords(opts);
            string choice = res.Status == PromptStatus.OK ? res.StringResult : "Normal";

            if (string.Equals(choice, "Critical", StringComparison.OrdinalIgnoreCase))
                return GraduallyVariedFlow.GvfBoundaryType.Critical;

            if (string.Equals(choice, "Known", StringComparison.OrdinalIgnoreCase))
            {
                knownDepthFt = PromptDouble(ed, "\nKnown boundary depth, ft", 2.0);
                return GraduallyVariedFlow.GvfBoundaryType.Known;
            }

            return GraduallyVariedFlow.GvfBoundaryType.Normal;
        }

        private static bool PromptDone(Editor ed)
        {
            var opts = new PromptKeywordOptions("\nAnother station or Done [Next/Done]")
            {
                AllowNone = true,
            };
            opts.Keywords.Add("Next");
            opts.Keywords.Add("Done");
            opts.Keywords.Default = "Next";

            PromptResult res = ed.GetKeywords(opts);
            return res.Status == PromptStatus.OK
                && string.Equals(res.StringResult, "Done", StringComparison.OrdinalIgnoreCase);
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
            ed.WriteMessage("\n  Calculation steps (boundary):");
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