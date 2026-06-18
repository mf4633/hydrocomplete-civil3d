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

            source.Items.Add(MakeButton("Network\nSummary", "HC_NETWORK", "Per-network pipe count, length, invert/diameter range, and structure count."));
            source.Items.Add(MakeButton("Pipe\nCapacity", "HC_PIPES", "Manning capacity of every pipe in the drawing's pipe networks."));
            source.Items.Add(MakeButton("Write\nCapacity", "HC_PIPES_WRITE", "Label Qfull and Vfull on layer HC-CAPACITY."));
            source.Items.Add(MakeButton("Design\nCapacity", "HC_CAPACITY", "Design Q vs Q_full check with d/D and surcharge flag."));
            source.Items.Add(MakeButton("Write\nOverload", "HC_CAPACITY_WRITE", "Label overloaded pipes on layer HC-CAPACITY."));
            source.Items.Add(MakeButton("Pipe\nSizing", "HC_SIZE", "Recommend smallest standard pipe (12-72 in) for design Q."));
            source.Items.Add(MakeButton("Validate\nDesign", "HC_VALIDATE", "Design-criteria review: slope, capacity, velocity, cover, size progression, HGL."));
            source.Items.Add(MakeButton("Design\nReview", "HC_REVIEW", "Design review plus state regulatory compliance (TSS, WQV, peaks, erosion)."));
            source.Items.Add(MakeButton("SCS\nRunoff", "HC_SCS", "SCS curve-number runoff from catchments."));
            source.Items.Add(MakeButton("Unit\nHydro", "HC_UNIT_HYDRO", "SCS synthetic unit hydrograph ordinate table."));
            source.Items.Add(MakeButton("Sediment\nRUSLE", "HC_SEDIMENT", "RUSLE soil loss from catchment slopes."));
            source.Items.Add(MakeButton("Water\nQuality Vol", "HC_WQV", "Water quality volume from state WQ storm."));
            source.Items.Add(MakeButton("Detention\nRouting", "HC_DETENTION", "Modified Puls pond routing with SCS UH inflow and orifice/weir outlets."));
            source.Items.Add(MakeButton("BMP\nSize", "HC_BMP_SIZE", "WQV-based BMP sizing (bioretention, pond, filter, swale)."));
            source.Items.Add(MakeButton("WQ\nTrain", "HC_WQ_TRAIN", "Sequential BMP treatment train with EMC pollutant loads."));
            source.Items.Add(MakeButton("Sediment\nBasin", "HC_SEDIMENT_BASIN", "NCDEQ sediment basin sizing from peak Q and RUSLE yield."));
            source.Items.Add(MakeButton("HGL\nProfile", "HC_HGL", "Steady HGL profile polyline on HC-HGL-PROFILE and labels on HC-HGL."));
            source.Items.Add(MakeButton("Chainage\nProfile", "HC_PROFILE", "Invert, crown, and optional HGL vs chainage on HC-PROFILE-* layers."));
            source.Items.Add(MakeButton("HTML\nReport", "HC_REPORT", "Export formula-transparent Manning + HGL HTML report."));
            source.Items.Add(MakeButton("PDF\nReport", "HC_REPORT_PDF", "Export formula-transparent Manning + HGL PDF report."));
            source.Items.Add(MakeButton("Rational\nQ", "HC_RATIONAL", "Rational peak flow from catchments + Atlas 14 IDF."));
            source.Items.Add(MakeButton("Multi-RP\nCapacity", "HC_MULTIRP", "Per-pipe Q and d/D for 2/10/25/100-yr return periods."));
            source.Items.Add(MakeButton("TR-55\nTc", "HC_TC", "TR-55 segmented time-of-concentration worksheet."));
            source.Items.Add(MakeButton("Inlet\nCheck", "HC_INLETS", "HEC-22 inlet check: grate, sag, or curb opening."));
            source.Items.Add(MakeButton("LandXML\nExport", "HC_LANDXML", "Export pipe network to LandXML 1.2."));
            source.Items.Add(MakeButton("LandXML\nImport", "HC_LANDXML_IMPORT", "Import LandXML 1.2 and compare to drawing."));
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
