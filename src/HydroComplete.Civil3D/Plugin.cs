using System;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

// Register the extension entry point and the command class with AutoCAD.
[assembly: ExtensionApplication(typeof(HydroComplete.Civil3D.Plugin))]
[assembly: CommandClass(typeof(HydroComplete.Civil3D.Commands.HydroCommands))]

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
            // Never throw out of Initialize — an exception here can abort the load.
            try
            {
                var doc = AcadApp.DocumentManager?.MdiActiveDocument;
                doc?.Editor.WriteMessage(
                    $"\n{ProductName} for Civil 3D 0.6.1 loaded. Type HC_ABOUT for commands.\n");
            }
            catch
            {
            }

            // The ribbon may not exist yet at load time, or the ribbon subsystem
            // may be entirely absent (e.g. AutoCAD core console / headless). Both
            // are non-fatal — the HC_* commands still work without a ribbon.
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
