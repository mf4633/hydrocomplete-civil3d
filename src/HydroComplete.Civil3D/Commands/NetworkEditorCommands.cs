using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using HydroComplete.Civil3D.Reading;
using HydroComplete.Civil3D.Storage;
using HydroComplete.Civil3D.Ui;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace HydroComplete.Civil3D.Commands
{
    /// <summary>HC_NETWORK_EDIT — interactive pipe override editor (design Q, Manning n).</summary>
    public sealed class NetworkEditorCommands
    {
        [CommandMethod("HC_NETWORK_EDIT")]
        public void EditNetwork()
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

            List<NetworkOverrideStore.PipeOverride> existing =
                NetworkOverrideStore.Load(doc.Name);
            NetworkOverrideApplier.ApplyToPipes(pipes, existing);

            var window = new NetworkEditorWindow(pipes);
            foreach (PipeEditRow row in window.Rows)
            {
                NetworkOverrideStore.PipeOverride? match = existing.FirstOrDefault(o =>
                    string.Equals(o.PipeKey, row.PipeKey, StringComparison.OrdinalIgnoreCase));
                if (match == null) continue;
                if (match.DesignFlowCfs.HasValue) row.DesignFlowCfs = match.DesignFlowCfs;
                if (match.ManningN.HasValue) row.ManningN = match.ManningN.Value;
                row.Notes = match.Notes;
            }

            bool? ok = HydroDialogHost.Show(window);
            if (ok != true)
            {
                ed.WriteMessage("\nNetwork editor cancelled.\n");
                return;
            }

            var overrides = new List<NetworkOverrideStore.PipeOverride>();
            foreach (PipeEditRow row in window.Rows)
            {
                overrides.Add(new NetworkOverrideStore.PipeOverride
                {
                    PipeKey = row.PipeKey,
                    PipeName = row.PipeName,
                    NetworkName = row.NetworkName,
                    DesignFlowCfs = row.DesignFlowCfs,
                    ManningN = row.ManningN,
                    Notes = row.Notes,
                });
            }

            NetworkOverrideStore.Save(doc.Name, overrides);
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: network overrides saved ---\n  Pipes: {0}\n  File: {1}\n",
                overrides.Count,
                NetworkOverrideStore.FilePathForDrawing(doc.Name)));
            ed.WriteMessage("  Overrides apply to HC_CAPACITY / HC_HGL on next run.\n");
        }

        private static Document Active()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) throw new InvalidOperationException("No active drawing.");
            return doc;
        }
    }
}