using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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
    /// <summary>HC_ROUTE_HYDRO — routed catchment hydrographs through the pipe network.</summary>
    public sealed class HydrographRouterCommands
    {
        [CommandMethod("HC_ROUTE_HYDRO")]
        public void RouteHydrographCommand()
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

            var catchments = CatchmentReader.ReadAll(doc.Database, civilDoc, pipes);
            if (catchments.Count == 0)
            {
                ed.WriteMessage("\nNo catchments found in this drawing.\n");
                return;
            }

            double stormDepth = PromptDouble(ed, "\n24-hour design storm depth, inches", 5.0);
            HydrographConvolution.UnitHydrographMethod uhMethod = PromptUnitHydroMethod(ed);
            bool useMc = PromptYesNo(ed, "\nApply Muskingum-Cunge on long reaches", defaultYes: true);

            var analysisPipes = NetworkPipeLinkMapper.ToAnalysisPipes(pipes);
            var structureNames = NetworkPipeLinkMapper.StructureNamesFromPipes(pipes);

            var options = new HydrographRouter.HydrographRouterOptions
            {
                StormDepthIn = stormDepth,
                TimestepHours = 0.25,
                UnitHydroMethod = uhMethod,
                ApplyMuskingumCunge = useMc,
                DefaultTcMinutes = CatchmentReader.DefaultTcMinutes,
            };

            HydrographRouter.HydrographRouterResult result = HydrographRouter.Route(
                catchments,
                analysisPipes,
                options,
                structureNames);

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: routed hydrographs (P={0:0.##} in, {1}, {2} catchments, {3} pipes) ---",
                stormDepth,
                uhMethod,
                catchments.Count,
                result.PipeHydrographs.Count));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Assignment: {0}", result.AssignmentMethod));
            ed.WriteMessage("\nNetwork / Pipe            Peak Q(cfs)  t_peak(min)  Volume(ac-ft)  Tt(min)  MC");

            foreach (HydrographRouter.PipeHydrographResult row in result.PipeHydrographs.Values
                .OrderBy(p => p.NetworkName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.PipeName, StringComparer.OrdinalIgnoreCase))
            {
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n{0,-24} {1,10:0.0}  {2,11:0.0}  {3,13:0.###}  {4,7:0.0}  {5,2}",
                    Trim(row.NetworkName + "/" + row.PipeName, 24),
                    row.PeakFlowCfs,
                    row.TimeToPeakMinutes,
                    row.VolumeAcreFt,
                    row.TravelTimeMinutes,
                    row.MuskingumCungeApplied ? "Y" : "N"));
            }

            string drawingName = Path.GetFileNameWithoutExtension(doc.Name);
            if (string.IsNullOrWhiteSpace(drawingName))
                drawingName = "untitled";

            string summaryPath = WriteCsv(result, drawingName);
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  CSV exported -> {0}", summaryPath));
            WriteCalcSteps(ed, result.Steps);
            ed.WriteMessage("\n");
        }

        private static string WriteCsv(HydrographRouter.HydrographRouterResult result, string drawingName)
        {
            Directory.CreateDirectory(ReportWriterCommon.OutputFolder);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string safeDrawing = ReportWriterCommon.SanitizeFileName(drawingName);
            string path = Path.Combine(
                ReportWriterCommon.OutputFolder,
                $"hydrograph-route-{safeDrawing}-{stamp}.csv");

            var sb = new StringBuilder();
            sb.AppendLine("section,network,pipe,time_min,flow_cfs,peak_cfs,t_peak_min,volume_acft,travel_min,muskingum_cunge");

            foreach (HydrographRouter.PipeHydrographResult row in result.PipeHydrographs.Values
                .OrderBy(p => p.NetworkName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.PipeName, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "summary,{0},{1},,,{2:0.###},{3:0.###},{4:0.###},{5:0.###},{6}",
                    CsvEscape(row.NetworkName),
                    CsvEscape(row.PipeName),
                    row.PeakFlowCfs,
                    row.TimeToPeakMinutes,
                    row.VolumeAcreFt,
                    row.TravelTimeMinutes,
                    row.MuskingumCungeApplied ? "Y" : "N"));

                foreach (HydrographRouter.RoutedHydrographOrdinate ord in row.Ordinates)
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "ordinate,{0},{1},{2:0.###},{3:0.###},,,,,",
                        CsvEscape(row.NetworkName),
                        CsvEscape(row.PipeName),
                        ord.TimeMinutes,
                        ord.FlowCfs));
                }
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            return path;
        }

        private static string CsvEscape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.IndexOf('"') >= 0 || value.IndexOf(',') >= 0)
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        private static HydrographConvolution.UnitHydrographMethod PromptUnitHydroMethod(Editor ed)
        {
            var opts = new PromptKeywordOptions("\nUnit hydrograph method [SCS/Snyder/Clark]")
            {
                AllowNone = true,
            };
            opts.Keywords.Add("SCS");
            opts.Keywords.Add("Snyder");
            opts.Keywords.Add("Clark");
            opts.Keywords.Default = "SCS";

            PromptResult res = ed.GetKeywords(opts);
            if (res.Status != PromptStatus.OK)
                return HydrographConvolution.UnitHydrographMethod.Scs;

            switch (res.StringResult.ToUpperInvariant())
            {
                case "SNYDER":
                    return HydrographConvolution.UnitHydrographMethod.Snyder;
                case "CLARK":
                    return HydrographConvolution.UnitHydrographMethod.Clark;
                default:
                    return HydrographConvolution.UnitHydrographMethod.Scs;
            }
        }

        private static double PromptDouble(
            Editor ed,
            string message,
            double defaultValue,
            bool allowZero = true)
        {
            var opts = new PromptDoubleOptions(message)
            {
                DefaultValue = defaultValue,
                UseDefaultValue = true,
                AllowZero = allowZero,
                AllowNegative = false,
            };
            PromptDoubleResult res = ed.GetDouble(opts);
            return res.Status == PromptStatus.OK ? res.Value : defaultValue;
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

        private static void WriteCalcSteps(Editor ed, System.Collections.Generic.IEnumerable<CalcStep> steps)
        {
            ed.WriteMessage("\n  Calculation steps:");
            foreach (CalcStep step in steps)
                ed.WriteMessage("\n    " + step);
        }

        private static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max - 1) + "~";
        }

        private static Document Active()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) throw new InvalidOperationException("No active drawing.");
            return doc;
        }
    }
}