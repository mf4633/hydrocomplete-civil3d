using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using HydroComplete.Civil3D.DagHost;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace HydroComplete.Civil3D.Commands
{
    /// <summary>HC_DAG — opens the HydroComplete Model Builder DAG editor panel.</summary>
    public sealed class DagPanelCommands
    {
        private static DagPaletteSet? _palette;

        [CommandMethod("HC_DAG", CommandFlags.Session)]
        public void OpenDagPanel()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            if (_palette != null && _palette.Visible)
            {
                _palette.Visible = true;
                ed.WriteMessage("\nHydroComplete Model Builder already open.\n");
                return;
            }

            try
            {
                _palette = new DagPaletteSet();
                _palette.OnRunRequested += async (dagJson, orderJson) =>
                    await RunDagAsync(doc, dagJson, orderJson);
                _palette.Visible = true;
                ed.WriteMessage("\nHydroComplete Model Builder opened. Drag nodes from the palette onto the canvas.\n");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nError opening DAG panel: {ex.Message}\n");
            }
        }

        private static async Task RunDagAsync(Document doc, string dagJson, string orderJson)
        {
            try
            {
                DagExecutor executor = new DagExecutor();
                string resultJson = executor.Execute(dagJson, orderJson);
                if (_palette != null)
                    await _palette.SendResultAsync(resultJson);
                doc.Editor.WriteMessage("\nDAG model executed — results sent to diagram.\n");
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage($"\nDAG execution error: {ex.Message}\n");
            }
        }
    }
}
