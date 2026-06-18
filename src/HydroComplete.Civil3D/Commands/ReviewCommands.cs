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
using HydroComplete.Civil3D.Writing;
using HydroComplete.Engine;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace HydroComplete.Civil3D.Commands
{
    /// <summary>
    /// HC_REVIEW — agency design review (DesignReview) plus state compliance checking.
    /// </summary>
    public sealed class ReviewCommands
    {
        [CommandMethod("HC_REVIEW")]
        public void Review()
        {
            Document doc = Active();
            Editor ed = doc.Editor;
            CivilDocument civilDoc = CivilApplication.ActiveDocument;

            var pipes = PipeNetworkReader.ReadAll(doc.Database, civilDoc);
            var catchments = CatchmentReader.ReadAll(doc.Database, civilDoc, pipes);

            if (pipes.Count == 0 && catchments.Count == 0)
            {
                ed.WriteMessage("\nNo pipe networks or catchments found in this drawing.\n");
                return;
            }

            StateComplianceConfig state = PromptState(ed);
            string devType = PromptDevelopmentType(ed);

            DesignFlowContext? flow = null;
            List<DesignFinding> designFindings = new List<DesignFinding>();
            if (pipes.Count > 0)
            {
                flow = DesignFlowResolver.Prompt(ed, doc.Database, civilDoc, pipes, doc.Name);
                var capacity = CapacityReportBuilder.Build(
                    pipes, flow.DesignFlowCfs, flow.PipeFlowCfs);

                if (capacity.Rows.Count > 0)
                {
                    var nodes = ValidateCommands.ReadStructureNodes(doc.Database, civilDoc, pipes);
                    ValidateCommands.ApplyNodeHglFromProfile(pipes, flow, nodes);
                    var reviewPipes = ValidateCommands.BuildReviewPipes(capacity.Rows);
                    designFindings = DesignReview.ReviewNetwork(reviewPipes, nodes);
                }
            }

            ComplianceAnalysisResults complianceInput = BuildComplianceInput(
                catchments, flow, state);
            ComplianceReport compliance = ComplianceChecker.CheckCompliance(
                complianceInput, state.Code, devType);

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: design review ({0}, {1}) ---",
                state.Name, devType));

            if (designFindings.Count > 0)
            {
                int errors = designFindings.Count(f => f.Severity == DesignFindingSeverity.Error);
                int warnings = designFindings.Count(f => f.Severity == DesignFindingSeverity.Warning);
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  Design criteria: {0} error(s), {1} warning(s)",
                    errors, warnings));
                foreach (DesignFinding finding in designFindings
                             .OrderBy(f => f.Severity).ThenBy(f => f.Id))
                {
                    string tag = finding.Severity == DesignFindingSeverity.Error ? "ERROR" : "WARN ";
                    ed.WriteMessage($"\n    [{tag}] {finding.Message}");
                }
            }
            else if (pipes.Count > 0)
            {
                ed.WriteMessage("\n  Design criteria: all checked rules passed.");
            }

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Regulatory compliance: {0}",
                compliance.OverallPass ? "COMPLIANT" : "NON-COMPLIANT"));
            ed.WriteMessage("\n  Criterion                         Required          Actual            Status");
            foreach (ComplianceCriterion c in compliance.Criteria)
            {
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  {0,-32} {1,-16} {2,-16} {3}",
                    Trim(c.Name, 32), Trim(c.Required, 16), Trim(c.Actual, 16), c.Status));
            }

            if (compliance.Recommendations.Count > 0)
            {
                ed.WriteMessage("\n  Recommendations:");
                foreach (string rec in compliance.Recommendations)
                    ed.WriteMessage($"\n    - {rec}");
            }

            ed.WriteMessage("\n");
        }

        private static ComplianceAnalysisResults BuildComplianceInput(
            IReadOnlyList<Engine.Catchment> catchments,
            DesignFlowContext? flow,
            StateComplianceConfig state)
        {
            var input = new ComplianceAnalysisResults();

            if (catchments.Count > 0)
            {
                WaterQualityEngine.WqvResult wqv =
                    WaterQualityEngine.ComputeWqvFromCatchments(catchments, state.WqVolumeFactorInches);

                double slopePct = 5.0;
                var sedimentRows = catchments.Select(cm =>
                    SedimentEngine.Rusle(cm.AreaAcres, slopePct, 300.0, cm.RunoffC, state.DefaultRFactor, name: cm.Name))
                    .ToList();

                input.WaterQuality = new WaterQualityComplianceInput
                {
                    BmpCount = 0,
                    WqvRequiredCf = wqv.WqvCf,
                    WqvProvidedCf = 0.0,
                };

                input.Sediment = new SedimentComplianceInput
                {
                    TotalSoilLossTonsPerAcYr = SedimentEngine.WeightedAverageSoilLoss(sedimentRows),
                    SedimentControlCount = 0,
                    WatershedResults = sedimentRows.Select(r => new WatershedSedimentInput
                    {
                        Name = r.Name,
                        RiskLevel = r.RiskLevel,
                    }).ToList(),
                };
            }

            if (flow != null)
            {
                input.Hydrology = new HydrologyComplianceInput
                {
                    HasDetention = false,
                    PrePeakCfs = flow.DesignFlowCfs * 0.8,
                    PostPeakCfs = flow.DesignFlowCfs,
                };
            }

            return input;
        }

        private static StateComplianceConfig PromptState(Editor ed)
        {
            var opts = new PromptKeywordOptions(
                "\nRegulatory state [NC/SC/VA/FL/TX/CA/NY/DEFAULT]")
            {
                AllowNone = true,
            };
            foreach (string stateCode in StateCompliance.AvailableStateCodes())
                opts.Keywords.Add(stateCode);
            opts.Keywords.Add(StateCompliance.DefaultCode);
            opts.Keywords.Default = "NC";

            PromptResult res = ed.GetKeywords(opts);
            string selected = res.Status == PromptStatus.OK ? res.StringResult : "NC";
            return StateCompliance.Get(selected);
        }

        private static string PromptDevelopmentType(Editor ed)
        {
            var opts = new PromptKeywordOptions(
                "\nDevelopment type [Residential/Commercial/Industrial/Roadway]")
            {
                AllowNone = true,
            };
            opts.Keywords.Add("Residential");
            opts.Keywords.Add("Commercial");
            opts.Keywords.Add("Industrial");
            opts.Keywords.Add("Roadway");
            opts.Keywords.Default = "Residential";

            PromptResult res = ed.GetKeywords(opts);
            if (res.Status != PromptStatus.OK) return "residential";
            return res.StringResult.ToLowerInvariant();
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