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

            AddPanel("Network",
                ("Network\nSummary", "HC_NETWORK", "Per-network pipe count, length, invert/diameter range, and structure count."),
                ("Pipe\nCapacity", "HC_PIPES", "Manning capacity of every pipe in the drawing's pipe networks."),
                ("Write\nCapacity", "HC_PIPES_WRITE", "Label Qfull and Vfull on layer HC-CAPACITY."),
                ("Design\nCapacity", "HC_CAPACITY", "Design Q vs Q_full check with d/D and surcharge flag."),
                ("Write\nOverload", "HC_CAPACITY_WRITE", "Label overloaded pipes on layer HC-CAPACITY."),
                ("Pipe\nSizing", "HC_SIZE", "Recommend smallest standard pipe (12-72 in) for design Q."),
                ("Validate\nDesign", "HC_VALIDATE", "Design-criteria review: slope, capacity, velocity, cover, size progression, HGL."),
                ("Network\nEdit", "HC_NETWORK_EDIT", "Edit per-pipe Q and Manning n overrides (saved per drawing)."),
                ("Network\nDiagram", "HC_NETWORK_DIAGRAM", "Export HTML/SVG pipe network schematic from plan topology."),
                ("Multi-RP\nCapacity", "HC_MULTIRP", "Per-pipe Q and d/D for 2/10/25/100-yr return periods."));

            AddPanel("Hydrology",
                ("Rational\nQ", "HC_RATIONAL", "Rational peak flow from catchments + Atlas 14 IDF."),
                ("Atlas 14\nIDF", "HC_ATLAS14", "List NOAA Atlas 14 IDF presets by city."),
                ("TR-55\nTc", "HC_TC", "TR-55 segmented time-of-concentration worksheet."),
                ("SCS\nRunoff", "HC_SCS", "SCS curve-number runoff from catchments."),
                ("Unit\nHydro", "HC_UNIT_HYDRO", "SCS synthetic unit hydrograph ordinate table."),
                ("Hydro-\ngraph", "HC_HYDROGRAPH", "Design storm hydrograph from CN, depth, and UH method."),
                ("Route\nHydro", "HC_ROUTE_HYDRO", "Route catchment hydrographs through the pipe network with junction superposition."),
                ("Loss\nMethod", "HC_LOSS", "Green-Ampt / Horton / CN incremental loss on a SCS Type II storm."),
                ("Continuous\nSim", "HC_CONTINUOUS", "Multi-year continuous simulation with Hargreaves ET and pollutant loads."),
                ("Soil\nLookup", "HC_SOIL", "Live SSURGO soil lookup by drawing geo or map unit name."));

            AddPanel("Stormwater",
                ("Detention\nRouting", "HC_DETENTION", "Modified Puls pond routing with SCS UH inflow and orifice/weir outlets."),
                ("BMP\nSize", "HC_BMP_SIZE", "WQV-based BMP sizing (bioretention, pond, filter, swale)."),
                ("WQ\nTrain", "HC_WQ_TRAIN", "Sequential BMP treatment train with EMC pollutant loads."),
                ("Water\nQuality Vol", "HC_WQV", "Water quality volume from state WQ storm."),
                ("WQ\nDiagram", "HC_WQ_DIAGRAM", "HTML/SVG BMP treatment-train node diagram with removal labels."),
                ("Pre/Post\nPeaks", "HC_PREPOST", "Pre/post-development peak flow comparison across state storm suite."),
                ("BMP\nOptimize", "HC_OPTIMIZE", "Top 3 lowest-cost BMP treatment trains meeting state removal targets."),
                ("Sediment\nRUSLE", "HC_SEDIMENT", "RUSLE soil loss from catchment slopes."),
                ("Sediment\nBasin", "HC_SEDIMENT_BASIN", "NCDEQ sediment basin sizing from peak Q and RUSLE yield."),
                ("Bio-\nretention", "HC_BIORETENTION", "Bioretention routing with underdrain/outlet."),
                ("Wetland\nRouting", "HC_WETLAND", "Wetland detention routing."));

            AddPanel("Hydraulics",
                ("HGL\nProfile", "HC_HGL", "Steady HGL profile polyline on HC-HGL-PROFILE and labels on HC-HGL."),
                ("GVF\nProfile", "HC_GVF", "Gradually varied flow water surface profile (Standard Step, trapezoidal channel)."),
                ("Culvert\nHW", "HC_CULVERT", "FHWA HDS-5 culvert headwater from pipe or manual geometry."),
                ("Chainage\nProfile", "HC_PROFILE", "Invert, crown, and optional HGL vs chainage on HC-PROFILE-* layers."),
                ("Profile\nDXF", "HC_PROFILE_DXF", "Export invert, crown, and optional HGL profile to DXF."),
                ("Inlet\nCheck", "HC_INLETS", "HEC-22 inlet check: grate, sag, or curb opening (modal dialog)."),
                ("Pump\nStation", "HC_PUMP", "Pump duty-point check: system head vs pump curve."));

            AddPanel("Analysis",
                ("Full\nAnalysis", "HC_ANALYZE", "Full-network analysis: hydrology, routing, capacity, HGL, sediment, WQV, compliance."),
                ("Design\nReview", "HC_REVIEW", "Design review plus state regulatory compliance (TSS, WQV, peaks, erosion)."),
                ("HTML\nReport", "HC_REPORT", "Export formula-transparent Manning + HGL HTML report."),
                ("PDF\nReport", "HC_REPORT_PDF", "Export formula-transparent Manning + HGL PDF report (Pro)."));

#if NET8_0_OR_GREATER
            AddPanel("Model Builder",
                ("Open\nDAG", "HC_DAG", "Visual model builder — drag-and-drop node DAG editor (WebView2 panel)."),
                ("Save\nDAG", "HC_DAG_SAVE", "Save the current DAG to a .hcdag file alongside the drawing."),
                ("Load\nDAG", "HC_DAG_LOAD", "Load a .hcdag file and open it in the DAG editor."));
#endif

            AddPanel("Tools",
                ("LandXML\nExport", "HC_LANDXML", "Export pipe network to LandXML 1.2."),
                ("LandXML\nImport", "HC_LANDXML_IMPORT", "Import LandXML 1.2 and compare to drawing."),
                ("Background\nMap", "HC_BACKGROUND", "Attach georeferenced raster image on HC-BACKGROUND."),
                ("Pipe\nCost", "HC_COST", "Roll up pipe costs from diameter catalog ($/LF)."),
                ("Activate\nPro", "HC_ACTIVATE", "Activate Pro with email and hc_live_ beta token."),
                ("License", "HC_LICENSE", "Show Free/Pro status and activation info."),
                ("About", "HC_ABOUT", "List HydroComplete commands."));

            ribbon.Tabs.Add(_tab);
        }

        private static void AddPanel(string title, params (string Text, string Command, string Tooltip)[] buttons)
        {
            var source = new RibbonPanelSource { Title = title };
            var panel = new RibbonPanel { Source = source };
            foreach (var (text, command, tooltip) in buttons)
                source.Items.Add(MakeButton(text, command, tooltip));
            _tab!.Panels.Add(panel);
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

            public event EventHandler? CanExecuteChanged
            {
                add { }
                remove { }
            }

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