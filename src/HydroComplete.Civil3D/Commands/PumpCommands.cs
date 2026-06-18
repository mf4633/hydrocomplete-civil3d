using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using HydroComplete.Civil3D.Reading;
using HydroComplete.Engine;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace HydroComplete.Civil3D.Commands
{
    /// <summary>HC_PUMP — pump station duty-point check (Hydraflow-style).</summary>
    public sealed class PumpCommands
    {
        [CommandMethod("HC_PUMP")]
        public void PumpDutyCheck()
        {
            Document doc = Active();
            Editor ed = doc.Editor;
            CivilDocument civilDoc = CivilApplication.ActiveDocument;

            List<PumpStationReader.PumpLocation> pumps =
                PumpStationReader.ReadAll(doc.Database, civilDoc);
            if (pumps.Count == 0)
            {
                ed.WriteMessage("\nNo pump structures found (name contains 'PUMP' or 'PS-').\n");
                ed.WriteMessage("  Tag a junction structure as e.g. PS-1 or Main Pump.\n");
                return;
            }

            double designQ = PromptDouble(ed, "\nPump design flow Q (cfs)", 30.0);
            double forceMainLen = PromptDouble(ed, "Force main length (ft)", 200.0);
            double forceMainDia = PromptDouble(ed, "Force main diameter (ft)", 1.5);
            double dischargeElev = PromptDouble(ed, "Discharge rim/invert elevation (ft)",
                pumps[0].RimFt > 0 ? pumps[0].RimFt : pumps[0].InvertFt + 5);

            IReadOnlyList<PumpStation.CurvePoint> curve = PumpStation.DefaultCurve();

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: pump duty check ({0} station(s)) ---\n",
                pumps.Count));
            ed.WriteMessage("  Station             Network             Suction    System H   Pump H    Margin   PASS");

            int pass = 0;
            foreach (PumpStationReader.PumpLocation pump in pumps)
            {
                PumpStation.DutyResult duty = PumpStation.CheckDuty(
                    designQ,
                    pump.InvertFt,
                    dischargeElev,
                    forceMainLen,
                    forceMainDia,
                    0.013,
                    curve);
                if (duty.Ok) pass++;

                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  {0,-18} {1,-18} {2,8:0.00}  {3,8:0.00}  {4,7:0.00}  {5,7:0.00}  {6}",
                    Trim(pump.Name, 18),
                    Trim(pump.NetworkName, 18),
                    pump.InvertFt,
                    duty.SystemHeadFt,
                    duty.PumpHeadFt,
                    duty.HeadMarginFt,
                    duty.Ok ? "OK" : "FAIL"));
            }

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  {0} pass, {1} fail.  Default pump curve used — customize in a future release.\n",
                pass, pumps.Count - pass));
        }

        private static double PromptDouble(Editor ed, string message, double dflt)
        {
            var opts = new PromptDoubleOptions(message)
            {
                DefaultValue = dflt,
                UseDefaultValue = true,
                AllowNegative = true,
                AllowZero = true,
            };
            PromptDoubleResult res = ed.GetDouble(opts);
            return res.Status == PromptStatus.OK ? res.Value : dflt;
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