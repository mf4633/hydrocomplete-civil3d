using System;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

// Register the extension entry point and the command class with AutoCAD.
[assembly: ExtensionApplication(typeof(HydroComplete.Civil3D.Plugin))]
[assembly: CommandClass(typeof(HydroComplete.Civil3D.Commands.HydroCommands))]
[assembly: CommandClass(typeof(HydroComplete.Civil3D.Commands.InletCommands))]
[assembly: CommandClass(typeof(HydroComplete.Civil3D.Commands.LandXmlCommands))]
[assembly: CommandClass(typeof(HydroComplete.Civil3D.Commands.MultiRpCommands))]
[assembly: CommandClass(typeof(HydroComplete.Civil3D.Commands.ReviewCommands))]
[assembly: CommandClass(typeof(HydroComplete.Civil3D.Commands.SizingCommands))]
[assembly: CommandClass(typeof(HydroComplete.Civil3D.Commands.StormwaterCommands))]
[assembly: CommandClass(typeof(HydroComplete.Civil3D.Commands.TcCommands))]
[assembly: CommandClass(typeof(HydroComplete.Civil3D.Commands.ValidateCommands))]
[assembly: CommandClass(typeof(HydroComplete.Civil3D.Commands.AdvancedStormwaterCommands))]
[assembly: CommandClass(typeof(HydroComplete.Civil3D.Commands.AnalyzeCommands))]
[assembly: CommandClass(typeof(HydroComplete.Civil3D.Commands.BackgroundCommands))]
[assembly: CommandClass(typeof(HydroComplete.Civil3D.Commands.CostCommands))]
[assembly: CommandClass(typeof(HydroComplete.Civil3D.Commands.CulvertCommands))]
[assembly: CommandClass(typeof(HydroComplete.Civil3D.Commands.GvfCommands))]
[assembly: CommandClass(typeof(HydroComplete.Civil3D.Commands.HydrographCommands))]
[assembly: CommandClass(typeof(HydroComplete.Civil3D.Commands.HydrographRouterCommands))]
[assembly: CommandClass(typeof(HydroComplete.Civil3D.Commands.NetworkEditorCommands))]
[assembly: CommandClass(typeof(HydroComplete.Civil3D.Commands.NetworkDiagramCommands))]
[assembly: CommandClass(typeof(HydroComplete.Civil3D.Commands.PeakControlCommands))]
[assembly: CommandClass(typeof(HydroComplete.Civil3D.Commands.ProfileCommands))]
[assembly: CommandClass(typeof(HydroComplete.Civil3D.Commands.PumpCommands))]
[assembly: CommandClass(typeof(HydroComplete.Civil3D.Commands.WaterQualityCommands))]
[assembly: CommandClass(typeof(HydroComplete.Civil3D.Commands.ContinuousSimCommands))]
[assembly: CommandClass(typeof(HydroComplete.Civil3D.Commands.DiagramCommands))]
#if NET8_0_OR_GREATER
[assembly: CommandClass(typeof(HydroComplete.Civil3D.Commands.DagPanelCommands))]
#endif

namespace HydroComplete.Civil3D
{
    /// <summary>
    /// AutoCAD/Civil 3D entry point. NETLOAD this assembly (or auto-load via a
    /// bundle) and AutoCAD calls <see cref="Initialize"/> once at load.
    /// </summary>
    public sealed class Plugin : IExtensionApplication
    {
        public const string ProductName = "HydroComplete";

        public void Initialize()
        {
            // Never throw out of Initialize â€” an exception here can abort the load.
            try
            {
                var doc = AcadApp.DocumentManager?.MdiActiveDocument;
                doc?.Editor.WriteMessage(
                    $"\n{ProductName} for Civil 3D 1.6.0 loaded. Type HC_ABOUT for commands.\n");
            }
            catch
            {
            }

            // The ribbon may not exist yet at load time, or the ribbon subsystem
            // may be entirely absent (e.g. AutoCAD core console / headless). Both
            // are non-fatal â€” the HC_* commands still work without a ribbon.
            try
            {
                Ribbon.RibbonBuilder.TryAddRibbon();
            }
            catch
            {
            }
        }

        public void Terminate()
        {
            Ribbon.RibbonBuilder.RemoveRibbon();
        }
    }
}
