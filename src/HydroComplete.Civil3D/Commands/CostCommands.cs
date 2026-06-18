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
    /// <summary>HC_COST — pipe network cost roll-up from catalog $/LF (Hydraflow-style).</summary>
    public sealed class CostCommands
    {
        [CommandMethod("HC_COST")]
        public void PipeCostRollup()
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

            string material = PromptMaterial(ed);
            var items = new List<(string Network, string Pipe, double LengthFt, double DiameterFt, string Material)>();
            foreach (ReadPipe pipe in pipes)
            {
                string mat = pipe.Segment.Shape == PipeShape.Box ? "BOX" : material;
                items.Add((pipe.NetworkName, pipe.PipeName, pipe.LengthFt, pipe.Segment.DiameterFt, mat));
            }

            List<PipeCostCatalog.NetworkCostRollup> rollups = PipeCostCatalog.RollupByNetwork(items);
            double grandTotal = rollups.Sum(r => r.TotalCost);

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: pipe cost estimate ({0} material) ---\n",
                material));
            ed.WriteMessage("  Network                 Length(ft)   Cost ($)");
            foreach (PipeCostCatalog.NetworkCostRollup rollup in rollups)
            {
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  {0,-22} {1,10:0.0}  {2,10:N0}",
                    Trim(rollup.NetworkName, 22),
                    rollup.TotalLengthFt,
                    rollup.TotalCost));
            }

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Grand total: ${0:N0}  ({1} pipes, catalog $/LF — not bid pricing)\n",
                grandTotal, pipes.Count));
        }

        private static string PromptMaterial(Editor ed)
        {
            var opts = new PromptKeywordOptions("\nPipe material [RCP/PVC/HDPE]")
            {
                AllowNone = true,
            };
            opts.Keywords.Add("RCP");
            opts.Keywords.Add("PVC");
            opts.Keywords.Add("HDPE");
            opts.Keywords.Default = "RCP";
            PromptResult res = ed.GetKeywords(opts);
            if (res.Status != PromptStatus.OK) return "RCP";
            return res.StringResult ?? "RCP";
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