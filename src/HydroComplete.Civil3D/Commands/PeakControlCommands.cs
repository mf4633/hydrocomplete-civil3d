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
    /// <summary>HC_PREPOST and HC_OPTIMIZE — peak control and BMP treatment-train optimization.</summary>
    public sealed class PeakControlCommands
    {
        [CommandMethod("HC_PREPOST")]
        public void PrePostComparisonCommand()
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

            StateComplianceConfig state = PromptState(ed);
            IReadOnlyDictionary<string, double> storms = StateCompliance.GetPeakStormSuite(state);

            PrePostComparison.WatershedInput postDev = BuildWatershedFromCatchments(catchments);
            double defaultPreCn = Math.Max(55.0, postDev.CurveNumber - 15.0);
            double preCn = PromptDouble(ed,
                $"\nPre-development composite CN [{state.Code}]",
                defaultPreCn);

            var preDev = new PrePostComparison.WatershedInput
            {
                AreaAcres = postDev.AreaAcres,
                CurveNumber = preCn,
                TcHours = postDev.TcHours,
            };

            PrePostComparison.PondConfiguration? pond = null;
            if (PromptYesNo(ed, "\nRoute post-development peaks through detention pond? [Yes/No]", defaultYes: false))
            {
                pond = new PrePostComparison.PondConfiguration
                {
                    MaxStorageFt3 = PromptDouble(ed, "\nPrismatic pond max storage, ft³", 50_000.0),
                    AvgDepthFt = PromptDouble(ed, "\nAverage pond depth, ft", 8.0),
                    Outlets = new List<OutletStructures.OutletDefinition>
                    {
                        new OutletStructures.OrificeOutlet
                        {
                            Name = "primary",
                            DiameterInches = PromptDouble(ed, "\nPrimary orifice diameter, in", 4.0),
                            Cd = 0.6,
                        },
                    },
                };
            }

            PrePostComparison.PrePostComparisonResult result = PrePostComparison.Run(
                preDev, postDev, storms, pond);

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: pre/post peak comparison ({0}, {1:0.###} ac) ---",
                state.Name, postDev.AreaAcres));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Pre-dev CN = {0:0.#}   Post-dev CN = {1:0.#}   Tc = {2:0.##} hr",
                preDev.CurveNumber, postDev.CurveNumber, postDev.TcHours));
            ed.WriteMessage("\n  Storm          P(in)  Q_pre(cfs)  Q_post unr(cfs)  Q_post rt(cfs)  PASS  Margin(cfs)");

            foreach (PrePostComparison.StormComparisonRow row in result.Rows)
            {
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  {0,-12} {1,5:0.##} {2,11:0.0} {3,16:0.0} {4,15:0.0} {5,5} {6,10:0.0}",
                    row.ReturnPeriod,
                    row.RainfallIn,
                    row.PreDevelopment.PeakFlowCfs,
                    row.PostDevelopment.PeakUnroutedCfs,
                    row.PostDevelopment.PeakRoutedCfs,
                    row.Pass ? "OK" : "FAIL",
                    row.MarginCfs));
            }

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Overall: {0} ({1} of {2} storms pass, tolerance ×{3:0.##})",
                result.AllPass ? "PASS" : "FAIL",
                result.Rows.Count(r => r.Pass),
                result.Rows.Count,
                PrePostComparison.PassToleranceFactor));
            WriteCalcSteps(ed, result.Steps);
            ed.WriteMessage("\n");
        }

        [CommandMethod("HC_OPTIMIZE")]
        public void OptimizeTreatmentTrainCommand()
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

            StateComplianceConfig state = PromptState(ed);
            double totalArea = catchments.Sum(c => c.AreaAcres);
            WaterQualityEngine.WqvResult wqv =
                WaterQualityEngine.ComputeWqvFromCatchments(catchments, state.WqVolumeFactorInches);

            var site = new BmpOptimizer.SiteData
            {
                AreaAcres = totalArea,
                ImperviousPercent = wqv.ImperviousPercent,
                RainfallDepthIn = state.WqVolumeFactorInches,
                AnnualRainfallIn = 45.0,
                TssConcentrationMgPerL = 80.0,
            };

            var targetRemoval = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                [Pollutant.Tss] = state.TssRemovalPercent / 100.0,
            };
            if (state.TnRemovalPercent > 0)
                targetRemoval[Pollutant.Tn] = state.TnRemovalPercent / 100.0;
            if (state.TpRemovalPercent > 0)
                targetRemoval[Pollutant.Tp] = state.TpRemovalPercent / 100.0;

            BmpOptimizer.TreatmentTrainResult trains = BmpOptimizer.OptimizeTreatmentTrain(site, targetRemoval);

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: BMP treatment-train optimization ({0}) ---",
                state.Name));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Site = {0:0.###} ac   I = {1:0.#}%   WQV storm = {2:0.##} in",
                totalArea, wqv.ImperviousPercent, state.WqVolumeFactorInches));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Targets: TSS {0:0.#}%", state.TssRemovalPercent));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Evaluated {0} valid train(s). Top 3 by lifecycle NPV:",
                trains.TotalEvaluated));

            int rank = 0;
            foreach (BmpOptimizer.TreatmentTrainEntry train in trains.AllTrains.Take(3))
            {
                rank++;
                string chain = string.Join(" → ", train.Names);
                double tssEta = train.CombinedRemoval.TryGetValue(Pollutant.Tss, out double eta)
                    ? eta * 100.0
                    : 0.0;
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  #{0}  {1}  (${2:N0} NPV, TSS η={3:0.#}%)",
                    rank, chain, train.TotalCost, tssEta));
            }

            if (trains.BestTrain != null)
            {
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  Best: {0} (${1:N0} NPV)",
                    string.Join(" → ", trains.BestTrain.Names),
                    trains.BestTrain.TotalCost));
            }
            else
            {
                ed.WriteMessage("\n  No train met all pollutant targets.");
            }

            WriteCalcSteps(ed, trains.Steps);
            ed.WriteMessage("\n");
        }

        private static PrePostComparison.WatershedInput BuildWatershedFromCatchments(
            IReadOnlyList<Catchment> catchments)
        {
            double area = catchments.Sum(c => c.AreaAcres);
            if (area <= 0) area = 1.0;

            double tcMin = catchments.Max(c => c.TcMinutes);
            if (tcMin <= 0) tcMin = CatchmentReader.DefaultTcMinutes;

            ScsRunoff.CompositeRunoffResult composite =
                ScsRunoff.ComputeComposite(catchments, rainfallInches: 1.0);

            return new PrePostComparison.WatershedInput
            {
                AreaAcres = area,
                CurveNumber = composite.WeightedCurveNumber > 0
                    ? composite.WeightedCurveNumber
                    : 75.0,
                TcHours = tcMin / 60.0,
            };
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