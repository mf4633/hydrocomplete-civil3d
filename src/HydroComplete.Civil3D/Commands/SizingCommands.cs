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
    /// <summary>HC_SIZE — standard catalog pipe sizing against design criteria.</summary>
    public sealed class SizingCommands
    {
        [CommandMethod("HC_SIZE")]
        public void SizePipes()
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

            DesignFlowContext flow = DesignFlowResolver.Prompt(ed, doc.Database, civilDoc, pipes, doc.Name);
            var criteria = DesignCriteria.Municipal();

            string qHeader = flow.IsRouted
                ? string.Format(CultureInfo.InvariantCulture,
                    "routed Q, system total={0:0.0} cfs", flow.DesignFlowCfs)
                : string.Format(CultureInfo.InvariantCulture, "Q={0:0.0} cfs", flow.DesignFlowCfs);

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: standard pipe sizing ({0}, {1} pipes) ---",
                qHeader, pipes.Count));
            ed.WriteMessage("\nCriteria: V={0:0.0}-{1:0.0} ft/s, max {2:P0} full, open-channel required",
                criteria.MinVelocity, criteria.MaxVelocity, criteria.MaxPctFull);
            ed.WriteMessage("\nNetwork / Pipe            Q(cfs)  Slope   Curr   Rec    V(fps)  %Full  Outcome");

            int sized = 0;
            int adequate = 0;
            int noSolution = 0;
            int skipped = 0;

            foreach (ReadPipe rp in pipes.OrderBy(p => p.NetworkName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.PipeName, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    double q = ResolveDesignFlow(rp, flow);
                    if (q <= 0.0)
                    {
                        skipped++;
                        continue;
                    }

                    double slope = rp.Segment.Slope;
                    double n = rp.Segment.ManningN;
                    double current = rp.Segment.DiameterFt;

                    if (slope <= 0.0 || current <= 0.0 || n <= 0.0)
                    {
                        ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                            "\n{0,-24} (skipped: invalid geometry)",
                            Trim(rp.NetworkName + "/" + rp.PipeName, 24)));
                        skipped++;
                        continue;
                    }

                    PipeSizeResult result = PipeSizing.SizePipe(q, slope, n, current, criteria);

                    switch (result.Outcome)
                    {
                        case SizeOutcome.Sized:
                            sized++;
                            break;
                        case SizeOutcome.Adequate:
                            adequate++;
                            break;
                        case SizeOutcome.NoSolution:
                            noSolution++;
                            break;
                    }

                    ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                        "\n{0,-24} {1,6:0.0}  {2,6:0.0000}  {3,4}  {4,4}  {5,6:0.00}  {6,5:0.0}  {7}",
                        Trim(rp.NetworkName + "/" + rp.PipeName, 24),
                        q,
                        slope,
                        PipeSizing.FormatDiameterIn(current),
                        PipeSizing.FormatDiameterIn(result.RecommendedDiameterFt),
                        result.VelocityFps,
                        result.PctFull * 100.0,
                        result.Outcome));
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                        "\n{0,-24} (skipped: {1})",
                        Trim(rp.NetworkName + "/" + rp.PipeName, 24),
                        ex.Message));
                    skipped++;
                }
            }

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Summary: {0} adequate, {1} upsize, {2} no solution",
                adequate, sized, noSolution));
            if (skipped > 0)
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    ", {0} skipped", skipped));
            ed.WriteMessage(".\n");
        }

        private static double ResolveDesignFlow(ReadPipe rp, DesignFlowContext flow)
        {
            if (flow.IsRouted && flow.PipeFlowCfs != null)
            {
                string key = rp.PipeId.Handle.ToString();
                if (flow.PipeFlowCfs.TryGetValue(key, out double routed) && routed > 0.0)
                    return routed;
            }

            return flow.DesignFlowCfs;
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