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
    /// HC_ANALYZE — full-network analysis orchestrator (.NET equivalent of runFullAnalysis).
    /// </summary>
    public sealed class AnalyzeCommands
    {
        [CommandMethod("HC_ANALYZE")]
        public void Analyze()
        {
            Document doc = Active();
            Editor ed = doc.Editor;
            CivilDocument civilDoc = CivilApplication.ActiveDocument;

            var readPipes = PipeNetworkReader.ReadAll(doc.Database, civilDoc);
            var catchments = CatchmentReader.ReadAll(doc.Database, civilDoc, readPipes);
            DesignFlowResolver.ApplyDefaultTcFallback(catchments, readPipes);

            if (catchments.Count == 0 && readPipes.Count == 0)
            {
                ed.WriteMessage("\nNo catchments or pipe networks found in this drawing.\n");
                return;
            }

            if (catchments.Count == 0)
            {
                ed.WriteMessage("\nNo catchments found — hydrology and routing require at least one catchment.\n");
                return;
            }

            StateComplianceConfig state = PromptState(ed, doc.Database);
            Atlas14Resolution? resolution = IdfPrompts.PromptPreset(ed, doc.Database);
            IdfCurve idf = resolution?.ToCurve() ?? IdfPrompts.PromptCustomIdfCurve(ed);

            var analysisPipes = NetworkPipeLinkMapper.ToAnalysisPipes(readPipes);
            var structureNames = NetworkPipeLinkMapper.StructureNamesFromPipes(readPipes);
            Dictionary<string, ReviewNodeInput>? reviewNodes = readPipes.Count > 0
                ? ValidateCommands.ReadStructureNodes(doc.Database, civilDoc, readPipes)
                : null;

            var input = new NetworkAnalysisInput
            {
                Catchments = catchments,
                Pipes = analysisPipes,
                StateCode = state.Code,
                DevelopmentType = "residential",
                Idf = idf,
                ScsDesignRainfallInches = state.DesignStormInches,
                StructureIdToName = structureNames,
                ReviewNodes = reviewNodes,
            };

            NetworkAnalysisResult result;
            try
            {
                result = NetworkAnalysisPipeline.Run(input);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nAnalysis failed: {ex.Message}\n");
                return;
            }

            WriteSummary(ed, result, resolution);

            if (PromptYesNo(ed, "\nExport full analysis HTML report", defaultYes: true))
            {
                string drawingName = Path.GetFileNameWithoutExtension(doc.Name);
                if (string.IsNullOrWhiteSpace(drawingName))
                    drawingName = "untitled";

                string reportPath = HtmlReportWriter.WriteAnalysis(drawingName, result, resolution?.DisplayLabel);
                ed.WriteMessage($"\n--- HydroComplete: analysis report written ---");
                ed.WriteMessage($"\n  {reportPath}");
                OfferOpenReport(ed, reportPath);
            }
            else
            {
                ed.WriteMessage("\n");
            }
        }

        private static void WriteSummary(
            Editor ed,
            NetworkAnalysisResult result,
            Atlas14Resolution? resolution)
        {
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: full network analysis ({0}) ---",
                result.StateCode));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Overall: {0}",
                result.OverallPass ? "PASS" : "FAIL"));

            if (resolution != null)
            {
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  IDF: {0} [{1}, {2}-yr]",
                    resolution.DisplayLabel, resolution.SourceLabel, resolution.ReturnPeriodYears));
            }

            ed.WriteMessage("\n\n  Catchment          Area(ac)  C      Tc(min)  Q(cfs)   SCS Q(in)");
            foreach (CatchmentHydrologyResult hydro in result.Hydrology)
            {
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  {0,-18} {1,7:0.000}  {2,4:0.00}  {3,7:0.0}  {4,6:0.00}  {5,6:0.###}",
                    Trim(hydro.Catchment.Name, 18),
                    hydro.Catchment.AreaAcres,
                    hydro.Catchment.RunoffC,
                    hydro.Catchment.TcMinutes,
                    hydro.Rational.PeakFlowCfs,
                    hydro.Scs.RunoffDepthInches));
            }

            if (result.Routing != null)
            {
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n\n  Routed Q: {0:0.00} cfs total ({1})",
                    result.Routing.TotalPeakCfs,
                    DescribeAssignment(result.Routing.AssignmentMethod)));
            }

            if (result.Capacity.Count > 0)
            {
                ed.WriteMessage("\n\n  Network / Pipe            Q(cfs)  Q/Qfull  d/D   SURCH");
                foreach (PipeCapacityAnalysisResult row in result.Capacity
                             .OrderBy(r => r.Pipe.NetworkName, StringComparer.OrdinalIgnoreCase)
                             .ThenBy(r => r.Pipe.PipeName, StringComparer.OrdinalIgnoreCase))
                {
                    string label = string.IsNullOrEmpty(row.Pipe.NetworkName)
                        ? row.Pipe.PipeName
                        : row.Pipe.NetworkName + "/" + row.Pipe.PipeName;
                    string dOverD = row.Surcharged
                        ? "SURCH"
                        : row.NormalDepth.RelativeDepth.ToString("0.00", CultureInfo.InvariantCulture);

                    ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                        "\n  {0,-24} {1,6:0.0}  {2,7:0.00}  {3,5}  {4,5}",
                        Trim(label, 24),
                        row.DesignFlowCfs,
                        row.FlowRatio,
                        dOverD,
                        row.Surcharged ? "*" : ""));
                }
            }

            if (result.HglNetworks.Count > 0)
            {
                ed.WriteMessage("\n\n  HGL networks:");
                foreach (NetworkHglResult net in result.HglNetworks)
                {
                    ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                        "\n    {0}: {1} reach(es), tailwater {2:0.00} ft",
                        net.NetworkName, net.Profile.Count, net.TailwaterFt));
                }
            }

            if (result.Sediment.Count > 0)
            {
                double avgLoss = SedimentEngine.WeightedAverageSoilLoss(result.Sediment);
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n\n  RUSLE avg soil loss: {0:0.##} tons/ac/yr", avgLoss));
            }

            if (result.Wqv != null)
            {
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  WQV required: {0:0} cf ({1:0.##} ac-ft)",
                    result.Wqv.WqvCf, result.Wqv.WqvAcreFt));
            }

            if (result.TreatmentTrain != null
                && result.TreatmentTrain.OverallRemovalEfficiency.TryGetValue(Pollutant.Tss, out double tssEta))
            {
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  Placeholder BMP train TSS removal: {0:0.#}%", tssEta * 100.0));
            }

            if (result.Compliance != null)
            {
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n\n  Compliance: {0}",
                    result.Compliance.OverallPass ? "COMPLIANT" : "NON-COMPLIANT"));
                ed.WriteMessage("\n  Criterion                         Required          Actual            Status");
                foreach (ComplianceCriterion c in result.Compliance.Criteria)
                {
                    ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                        "\n  {0,-32} {1,-16} {2,-16} {3}",
                        Trim(c.Name, 32), Trim(c.Required, 16), Trim(c.Actual, 16), c.Status));
                }
            }

            int designErrors = result.DesignReview.Count(f => f.Severity == DesignFindingSeverity.Error);
            int designWarnings = result.DesignReview.Count(f => f.Severity == DesignFindingSeverity.Warning);
            if (designErrors > 0 || designWarnings > 0)
            {
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n\n  Design review: {0} error(s), {1} warning(s)",
                    designErrors, designWarnings));
                foreach (DesignFinding finding in result.DesignReview
                             .OrderBy(f => f.Severity).ThenBy(f => f.Id).Take(8))
                {
                    string tag = finding.Severity == DesignFindingSeverity.Error ? "ERROR" : "WARN ";
                    ed.WriteMessage($"\n    [{tag}] {finding.Message}");
                }

                if (result.DesignReview.Count > 8)
                    ed.WriteMessage($"\n    ... and {result.DesignReview.Count - 8} more finding(s).");
            }

            foreach (string warning in result.Warnings)
                ed.WriteMessage($"\n  Warning: {warning}");
            foreach (string error in result.Errors)
                ed.WriteMessage($"\n  Error: {error}");
        }

        private static StateComplianceConfig PromptState(Editor ed, Database db)
        {
            string defaultCode = ResolveDefaultStateCode(db);
            var opts = new PromptKeywordOptions(
                $"\nRegulatory state [NC/SC/VA/FL/TX/CA/NY/DEFAULT] (default {defaultCode})")
            {
                AllowNone = true,
            };
            foreach (string stateCode in StateCompliance.AvailableStateCodes())
                opts.Keywords.Add(stateCode);
            opts.Keywords.Add(StateCompliance.DefaultCode);
            opts.Keywords.Default = defaultCode;

            PromptResult res = ed.GetKeywords(opts);
            string selected = res.Status == PromptStatus.OK ? res.StringResult : defaultCode;
            return StateCompliance.Get(selected);
        }

        private static string ResolveDefaultStateCode(Database db)
        {
            DrawingGeolocation.Result? geo = DrawingGeolocation.TryRead(db);
            if (geo != null)
            {
                Atlas14Presets.Preset preset = Atlas14Presets.Nearest(geo.Lat, geo.Lon);
                if (!string.IsNullOrWhiteSpace(preset.State))
                    return preset.State.ToUpperInvariant();
            }

            return "NC";
        }

        private static string DescribeAssignment(CatchmentAssignmentMethod method)
        {
            switch (method)
            {
                case CatchmentAssignmentMethod.OutletStructure:
                    return "outlet structures";
                case CatchmentAssignmentMethod.AreaWeightedHeadwater:
                    return "area-weighted headwaters";
                default:
                    return "uniform fallback";
            }
        }

        private static Document Active()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) throw new InvalidOperationException("No active drawing.");
            return doc;
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

        private static void OfferOpenReport(Editor ed, string reportPath)
        {
            if (!PromptYesNo(ed, "\nOpen report now?", defaultYes: true))
            {
                ed.WriteMessage("\n");
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(reportPath) { UseShellExecute = true });
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n  Could not open report: {ex.Message}");
            }

            ed.WriteMessage("\n");
        }

        private static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max - 1) + "~";
        }
    }
}