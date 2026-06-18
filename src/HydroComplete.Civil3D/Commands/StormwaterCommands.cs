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
    /// <summary>HC_SCS, HC_UNIT_HYDRO, HC_SEDIMENT, HC_WQV stormwater hydrology commands.</summary>
    public sealed class StormwaterCommands
    {
        [CommandMethod("HC_SCS")]
        public void ScsRunoffCommand()
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
            double rainfall = PromptRainfall(ed, state.DesignStormInches,
                $"\nDesign rainfall depth for SCS CN runoff [{state.Code}]");

            ScsRunoff.CompositeRunoffResult composite = ScsRunoff.ComputeComposite(catchments, rainfall);

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: SCS CN runoff ({0}, P={1:0.##} in, {2} catchments) ---",
                state.Name, rainfall, catchments.Count));
            ed.WriteMessage("\nCatchment              A(ac)   CN     Q(in)   Q(cf)");
            foreach (ScsRunoff.CatchmentRunoffResult row in composite.Catchments)
            {
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n{0,-20} {1,6:0.000} {2,5:0} {3,7:0.###} {4,9:0}",
                    Trim(row.CatchmentName, 20), row.AreaAcres, row.CurveNumber,
                    row.RunoffDepthInches, row.RunoffVolumeCf));
            }

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Composite CN = {0:0.#}   runoff depth = {1:0.###} in   volume = {2:0} cf",
                composite.WeightedCurveNumber, composite.CompositeRunoffDepthInches,
                composite.TotalRunoffVolumeCf));
            WriteCalcSteps(ed, composite.Steps);
            ed.WriteMessage("\n");
        }

        [CommandMethod("HC_UNIT_HYDRO")]
        public void UnitHydrographCommand()
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

            double totalArea = catchments.Sum(c => c.AreaAcres);
            double systemTc = catchments.Max(c => c.TcMinutes);
            if (systemTc <= 0) systemTc = CatchmentReader.DefaultTcMinutes;

            ScsUnitHydrograph.UnitHydrographResult uh =
                ScsUnitHydrograph.Generate(totalArea, systemTc);

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: SCS unit hydrograph (A={0:0.000} ac, Tc={1:0.0} min) ---",
                totalArea, systemTc));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Tl = {0:0.###} hr   Tp = {1:0.###} hr ({2:0.0} min)   qp = {3:0.0} cfs (1 in runoff)",
                uh.LagHours, uh.TimeToPeakHours, uh.TimeToPeakMinutes, uh.PeakFlowCfs));
            ed.WriteMessage("\n  t(min)   t/Tp    q/qp    Q(cfs)");
            foreach (ScsUnitHydrograph.HydrographOrdinate ord in uh.Ordinates)
            {
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  {0,6:0.0}  {1,5:0.00}  {2,5:0.00}  {3,8:0.0}",
                    ord.TimeMinutes, ord.TRatio, ord.QRatio, ord.FlowCfs));
            }

            WriteCalcSteps(ed, uh.Steps);
            ed.WriteMessage("\n");
        }

        [CommandMethod("HC_SEDIMENT")]
        public void SedimentCommand()
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
            double slopePct = PromptDouble(ed, "\nAverage catchment slope, percent", 5.0);
            double lengthFt = PromptDouble(ed, "\nRepresentative slope length, ft", 300.0);
            double rFactor = PromptDouble(ed, "\nRUSLE R-factor", state.DefaultRFactor);

            var rows = new List<SedimentEngine.RusleResult>();
            foreach (Catchment cm in catchments)
            {
                rows.Add(SedimentEngine.Rusle(
                    cm.AreaAcres, slopePct, lengthFt, cm.RunoffC, rFactor, name: cm.Name));
            }

            double weighted = SedimentEngine.WeightedAverageSoilLoss(rows);

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: RUSLE soil loss ({0}, R={1:0}, {2} catchments) ---",
                state.Name, rFactor, catchments.Count));
            ed.WriteMessage("\nCatchment              A(ac)   C      LS      A(tons/ac/yr)  Risk");
            foreach (SedimentEngine.RusleResult row in rows)
            {
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n{0,-20} {1,6:0.000} {2,5:0.00} {3,7:0.00} {4,13:0.##}  {5}",
                    Trim(row.Name, 20), row.AreaAcres, row.CFactor, row.LSFactor,
                    row.SoilLossTonsPerAcYr, row.RiskLevel));
            }

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Area-weighted soil loss = {0:0.##} tons/ac/yr (tolerable T = {1:0.#})",
                weighted, state.TolerableSoilLossTonsPerAcYr));
            ed.WriteMessage("\n");
        }

        [CommandMethod("HC_WQV")]
        public void WaterQualityVolumeCommand()
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
            WaterQualityEngine.WqvResult wqv =
                WaterQualityEngine.ComputeWqvFromCatchments(catchments, state.WqVolumeFactorInches);

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: water quality volume ({0}, {1} catchments) ---",
                state.Name, catchments.Count));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Area = {0:0.000} ac   I = {1:0.#}%   Rv = {2:0.###}",
                wqv.TotalAreaAcres, wqv.ImperviousPercent, wqv.RunoffCoefficientRv));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Design storm = {0:0.##} in   WQV = {1:0} cf ({2:0.###} ac-ft, {3:0} gal)",
                wqv.DesignStormInches, wqv.WqvCf, wqv.WqvAcreFt, wqv.WqvGallons));
            WriteCalcSteps(ed, wqv.Steps);
            ed.WriteMessage("\n");
        }

        private static StateComplianceConfig PromptState(Editor ed)
        {
            var opts = new PromptKeywordOptions(
                "\nRegulatory state [NC/SC/VA/FL/TX/CA/NY/DEFAULT]")
            {
                AllowNone = true,
            };
            foreach (string code in StateCompliance.AvailableStateCodes())
                opts.Keywords.Add(code);
            opts.Keywords.Add(StateCompliance.DefaultCode);
            opts.Keywords.Default = "NC";

            PromptResult res = ed.GetKeywords(opts);
            string code = res.Status == PromptStatus.OK ? res.StringResult : "NC";
            return StateCompliance.Get(code);
        }

        private static double PromptRainfall(Editor ed, double defaultIn, string message)
        {
            var opts = new PromptDoubleOptions(message)
            {
                DefaultValue = defaultIn,
                UseDefaultValue = true,
                AllowZero = false,
                AllowNegative = false,
            };
            PromptDoubleResult res = ed.GetDouble(opts);
            return res.Status == PromptStatus.OK ? res.Value : defaultIn;
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

        private static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max - 1) + "~";
        }
    }
}