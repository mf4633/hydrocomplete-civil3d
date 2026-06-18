using System;
using System.Windows.Input;
using Autodesk.Windows;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace HydroComplete.Civil3D.Ribbon
{
    /// <summary>
    /// Builds the "HydroComplete" ribbon tab. The ribbon control may not exist
    /// when the plugin loads, so this defers to ComponentManager.ItemInitialized
    /// and builds as soon as the ribbon is available.
    /// </summary>
    internal static class RibbonBuilder
    {
        private const string TabId = "HYDROCOMPLETE_TAB";
        private static RibbonTab? _tab;

        public static void TryAddRibbon()
        {
            RibbonControl ribbon = ComponentManager.Ribbon;
            if (ribbon == null)
            {
                ComponentManager.ItemInitialized += OnItemInitialized;
                return;
            }
            Build(ribbon);
        }

        private static void OnItemInitialized(object? sender, RibbonItemEventArgs e)
        {
            RibbonControl ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return;
            ComponentManager.ItemInitialized -= OnItemInitialized;
            Build(ribbon);
        }

        private static void Build(RibbonControl ribbon)
        {
            if (_tab != null || ribbon.FindTab(TabId) != null) return;

            _tab = new RibbonTab { Title = "HydroComplete", Id = TabId };

            var source = new RibbonPanelSource { Title = "Analysis" };
            var panel = new RibbonPanel { Source = source };
            _tab.Panels.Add(panel);

            source.Items.Add(MakeButton("Pipe\nCapacity", "HC_PIPES", "Manning capacity of every pipe in the drawing's pipe networks."));
            source.Items.Add(MakeButton("Write\nCapacity", "HC_PIPES_WRITE", "Label Qfull and Vfull on layer HC-CAPACITY."));
            source.Items.Add(MakeButton("Design\nCapacity", "HC_CAPACITY", "Design Q vs Q_full check with d/D and surcharge flag."));
            source.Items.Add(MakeButton("Write\nOverload", "HC_CAPACITY_WRITE", "Label overloaded pipes on layer HC-CAPACITY."));
            source.Items.Add(MakeButton("HGL\nProfile", "HC_HGL", "Steady HGL profile polyline on HC-HGL-PROFILE and labels on HC-HGL."));
            source.Items.Add(MakeButton("HTML\nReport", "HC_REPORT", "Export formula-transparent Manning + HGL HTML report."));
            source.Items.Add(MakeButton("PDF\nReport", "HC_REPORT_PDF", "Export formula-transparent Manning + HGL PDF report."));
            source.Items.Add(MakeButton("Rational\nQ", "HC_RATIONAL", "Rational peak flow from catchments + Atlas 14 IDF."));
            source.Items.Add(MakeButton("Atlas 14\nIDF", "HC_ATLAS14", "List NOAA Atlas 14 IDF presets by city."));
            source.Items.Add(MakeButton("Activate\nPro", "HC_ACTIVATE", "Activate Pro with email and hc_live_ beta token."));
            source.Items.Add(MakeButton("License", "HC_LICENSE", "Show Free/Pro status and activation info."));
            source.Items.Add(MakeButton("About", "HC_ABOUT", "List HydroComplete commands."));

            ribbon.Tabs.Add(_tab);
        }

        public static void RemoveRibbon()
        {
            try
            {
                RibbonControl ribbon = ComponentManager.Ribbon;
                if (ribbon != null && _tab != null) ribbon.Tabs.Remove(_tab);
            }
            catch { }
            finally { _tab = null; }
        }

        private static RibbonButton MakeButton(string text, string command, string tooltip)
        {
            return new RibbonButton
            {
                Text = text,
                ShowText = true,
                ShowImage = false,
                Size = RibbonItemSize.Large,
                CommandParameter = command + " ",
                CommandHandler = CommandRelay.Instance,
                ToolTip = tooltip,
            };
        }

        /// <summary>Relays a ribbon click to the command line via SendStringToExecute.</summary>
        private sealed class CommandRelay : ICommand
        {
            public static readonly CommandRelay Instance = new CommandRelay();

            public event EventHandler? CanExecuteChanged;

            public bool CanExecute(object? parameter) => true;

            public void Execute(object? parameter)
            {
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                if (parameter is string cmd && !string.IsNullOrEmpty(cmd))
                    doc.SendStringToExecute(cmd, true, false, true);
            }
        }
    }
}
