using System;
using System.Globalization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using HydroComplete.Civil3D.Reading;
using HydroComplete.Engine;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace HydroComplete.Civil3D.Commands
{
    /// <summary>HC_HYDROGRAPH — design storm hydrograph from CN, depth, and UH method.</summary>
    public sealed class HydrographCommands
    {
        [CommandMethod("HC_HYDROGRAPH")]
        public void HydrographCommand()
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

            double totalArea = 0.0;
            double weightedCn = 0.0;
            double systemTc = 0.0;
            foreach (Catchment cm in catchments)
            {
                totalArea += cm.AreaAcres;
                double catchmentCn = ScsRunoff.ResolveCurveNumber(cm);
                weightedCn += catchmentCn * cm.AreaAcres;
                systemTc = Math.Max(systemTc, cm.TcMinutes > 0 ? cm.TcMinutes : CatchmentReader.DefaultTcMinutes);
            }
            weightedCn = totalArea > 0 ? weightedCn / totalArea : 75.0;

            double cn = PromptDouble(ed, "\nComposite curve number CN", weightedCn, allowZero: false);
            double stormDepth = PromptDouble(ed, "\n24-hour design storm depth, inches", 5.0);
            HydrographConvolution.UnitHydrographMethod uhMethod = PromptUnitHydroMethod(ed);

            HydrographConvolution.ConvolutionResult hydrograph =
                HydrographConvolution.GenerateTr20Hydrograph(
                    totalArea,
                    cn,
                    systemTc,
                    stormDepth,
                    timestepHours: 0.25,
                    uhMethod);

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: design hydrograph (A={0:0.000} ac, CN={1:0.#}, P={2:0.##} in, {3}) ---",
                totalArea, cn, stormDepth, uhMethod));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Excess rainfall = {0:0.###} in   Peak Q = {1:0.0} cfs at t = {2:0.##} hr",
                hydrograph.TotalExcessRainfallIn, hydrograph.PeakFlowCfs, hydrograph.TimeToPeakHours));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Runoff volume = {0:0.###} ac-ft",
                hydrograph.VolumeAcreFt));

            WriteCalcSteps(ed, hydrograph.Steps);
            ed.WriteMessage("\n");
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

        private static void WriteCalcSteps(Editor ed, System.Collections.Generic.IEnumerable<CalcStep> steps)
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