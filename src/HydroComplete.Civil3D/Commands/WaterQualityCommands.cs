using System;
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
    /// <summary>
    /// HC_BIORETENTION, HC_WETLAND, HC_SOIL — physics-based BMP routing and soil lookup.
    /// </summary>
    public sealed class WaterQualityCommands
    {
        [CommandMethod("HC_BIORETENTION")]
        public void BioretentionRoutingCommand()
        {
            Document doc = Active();
            Editor ed = doc.Editor;
            CivilDocument civilDoc = CivilApplication.ActiveDocument;

            var catchments = CatchmentReader.ReadAll(doc.Database, civilDoc);
            if (catchments.Count == 0)
            {
                ed.WriteMessage("\nNo catchments found in this drawing.\n");
                return;
            }

            StateComplianceConfig state = PromptState(ed);
            double rainfall = PromptRainfall(ed, state.DesignStormInches,
                $"\nDesign rainfall depth for bioretention routing [{state.Code}]");

            double totalArea = catchments.Sum(c => c.AreaAcres);
            double weightedCn = catchments.Sum(c => c.CurveNumber * c.AreaAcres) / Math.Max(totalArea, 1e-9);
            if (weightedCn <= 0) weightedCn = 75.0;

            WaterQualityEngine.ScsRunoffResult runoff =
                WaterQualityEngine.CalculateScsRunoff(rainfall, weightedCn, antecedentDryDays: 3);
            double designVolumeCf = runoff.RunoffDepthIn * totalArea * BmpLibrary.SqFtPerAcre / BmpLibrary.InchesPerFoot;

            double surfaceArea = PromptDouble(ed,
                "\nBioretention surface area, sf (0 = 5% of site)",
                totalArea * BmpLibrary.SqFtPerAcre * 0.05);
            if (surfaceArea <= 0)
                surfaceArea = totalArea * BmpLibrary.SqFtPerAcre * 0.05;

            var config = new BioretentionRouting.BioretentionConfig
            {
                KsatInPerHr = PromptDouble(ed, "\nMedia Ksat, in/hr", 1.0),
                MediaDepthFt = PromptDouble(ed, "\nMedia depth, ft", 2.5),
                PondingDepthFt = PromptDouble(ed, "\nMax ponding depth, ft", 1.0),
            };

            BioretentionRouting.BioretentionRoutingResult result =
                BioretentionRouting.Route(config, designVolumeCf, surfaceArea);

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: bioretention routing ({0}, P={1:0.##} in) ---",
                state.Name, rainfall));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  A = {0:0} ac   CN = {1:0.#}   Q = {2:0.###} in   V_design = {3:0} cf",
                totalArea, weightedCn, runoff.RunoffDepthIn, designVolumeCf));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  V_treated = {0:0} cf   V_bypass = {1:0} cf ({2:0.1}% bypass)",
                result.TreatedVolumeCf, result.OverflowVolumeCf, result.BypassFractionPercent));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  t_res = {0:0.1} hr   drawdown = {1:0.1} hr",
                result.ResidenceTimeHr, result.DrawdownTimeHr));

            foreach (var kv in result.RemovalEfficiency)
            {
                if (kv.Key == Pollutant.Tss || kv.Key == Pollutant.Tn || kv.Key == Pollutant.Tp)
                {
                    ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                        "\n  E_{0} = {1:0.1}% treated / {2:0.1}% blended",
                        kv.Key, kv.Value.TreatedPercent, kv.Value.BlendedPercent));
                }
            }

            WriteCalcSteps(ed, result.Steps);
            ed.WriteMessage("\n");
        }

        [CommandMethod("HC_WETLAND")]
        public void WetlandRoutingCommand()
        {
            Document doc = Active();
            Editor ed = doc.Editor;
            CivilDocument civilDoc = CivilApplication.ActiveDocument;

            var catchments = CatchmentReader.ReadAll(doc.Database, civilDoc);
            if (catchments.Count == 0)
            {
                ed.WriteMessage("\nNo catchments found in this drawing.\n");
                return;
            }

            StateComplianceConfig state = PromptState(ed);
            double rainfall = PromptRainfall(ed, state.DesignStormInches,
                $"\nDesign rainfall depth for wetland routing [{state.Code}]");

            double totalArea = catchments.Sum(c => c.AreaAcres);
            double weightedCn = catchments.Sum(c => c.CurveNumber * c.AreaAcres) / Math.Max(totalArea, 1e-9);
            if (weightedCn <= 0) weightedCn = 75.0;

            WaterQualityEngine.ScsRunoffResult runoff =
                WaterQualityEngine.CalculateScsRunoff(rainfall, weightedCn, antecedentDryDays: 3);
            double designVolumeCf = runoff.RunoffDepthIn * totalArea * BmpLibrary.SqFtPerAcre / BmpLibrary.InchesPerFoot;

            double surfaceArea = PromptDouble(ed,
                "\nConstructed wetland surface area, sf",
                Math.Max(10_000.0, totalArea * BmpLibrary.SqFtPerAcre * 0.08));

            WetlandRouting.ConstructedWetlandRoutingResult result =
                WetlandRouting.RouteConstructedWetland(
                    new WetlandRouting.WetlandConfig(),
                    designVolumeCf,
                    surfaceArea);

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: constructed wetland routing ({0}, P={1:0.##} in) ---",
                state.Name, rainfall));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  V_design = {0:0} cf   A_wetland = {1:0} sf   zones = {2}",
                designVolumeCf, surfaceArea, result.ZoneCount));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Method: {0}", result.Method));

            foreach (var kv in result.RemovalEfficiency)
            {
                if (kv.Key == Pollutant.Tss || kv.Key == Pollutant.Tn || kv.Key == Pollutant.Tp)
                {
                    ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                        "\n  E_{0} = {1:0.1}%", kv.Key, kv.Value.TreatedPercent));
                }
            }

            if (result.RemovalEfficiency.TryGetValue(Pollutant.Tss, out WetlandRouting.PollutantRemovalEfficiency? tssEff))
            {
                ed.WriteMessage("\n  Zone breakdown (TSS):");
                foreach (WetlandRouting.ZoneTreatmentStep zone in tssEff.Zones)
                {
                    ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                        "\n    {0}: C_in={1:0.##} -> C_out={2:0.##} mg/L ({3:0.1}% removal)",
                        zone.Zone, zone.InfluentConcentration, zone.EffluentConcentration, zone.RemovalPercent));
                }
            }

            WriteCalcSteps(ed, result.Steps);
            ed.WriteMessage("\n");
        }

        [CommandMethod("HC_SOIL")]
        public void SoilLookupCommand()
        {
            Document doc = Active();
            Editor ed = doc.Editor;

            SsugroResolution? liveResolution = null;
            SoilDatabase.SoilProperties soil;

            DrawingGeolocation.Result? geo = DrawingGeolocation.TryRead(doc.Database);
            if (geo != null)
            {
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  Drawing geolocation: {0:0.####}, {1:0.####} ({2})",
                    geo.Lat, geo.Lon, geo.Source));
                liveResolution = SsugroService.Resolve(geo.Lat, geo.Lon);
                soil = liveResolution.ToSoilProperties();
            }
            else
            {
                var modeOpts = new PromptKeywordOptions("\nSoil lookup mode [Live/Name]")
                {
                    AllowNone = true,
                };
                modeOpts.Keywords.Add("Live");
                modeOpts.Keywords.Add("Name");
                modeOpts.Keywords.Default = "Name";
                PromptResult modeRes = ed.GetKeywords(modeOpts);
                string mode = modeRes.Status == PromptStatus.OK ? modeRes.StringResult : "Name";

                if (string.Equals(mode, "Live", StringComparison.OrdinalIgnoreCase))
                {
                    double lat = PromptDouble(ed, "\nLatitude, degrees", 35.23);
                    double lon = PromptDouble(ed, "\nLongitude, degrees", -80.84);
                    liveResolution = SsugroService.Resolve(lat, lon);
                    soil = liveResolution.ToSoilProperties();
                }
                else
                {
                    var opts = new PromptStringOptions("\nSoil map unit / series name")
                    {
                        AllowSpaces = true,
                    };
                    PromptResult nameRes = ed.GetString(opts);
                    if (nameRes.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(nameRes.StringResult))
                    {
                        ed.WriteMessage("\nSoil lookup cancelled.\n");
                        return;
                    }

                    try
                    {
                        soil = SoilDatabase.Lookup(nameRes.StringResult);
                    }
                    catch (ArgumentException ex)
                    {
                        ed.WriteMessage($"\n{ex.Message}\n");
                        return;
                    }
                }
            }

            var bmpOpts = new PromptKeywordOptions("\nEvaluate BMP suitability for [Bioretention/WetPond/Wetland]")
            {
                AllowNone = true,
            };
            bmpOpts.Keywords.Add("Bioretention");
            bmpOpts.Keywords.Add("WetPond");
            bmpOpts.Keywords.Add("Wetland");
            bmpOpts.Keywords.Default = "Bioretention";
            PromptResult bmpRes = ed.GetKeywords(bmpOpts);
            string bmpType = bmpRes.Status == PromptStatus.OK ? bmpRes.StringResult : "Bioretention";
            if (string.Equals(bmpType, "Wetland", StringComparison.OrdinalIgnoreCase))
                bmpType = "constructed-wetland";
            else if (string.Equals(bmpType, "WetPond", StringComparison.OrdinalIgnoreCase))
                bmpType = BmpType.WetPond;

            SoilDatabase.BmpSuggestionResult suggestion = SoilDatabase.SuggestBmp(soil, bmpType);

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: soil lookup ---"));
            if (liveResolution != null)
            {
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  Source: {0}{1}",
                    liveResolution.Source,
                    liveResolution.MapUnit.IsFallback ? " (fallback)" : ""));
                if (!string.IsNullOrWhiteSpace(liveResolution.MapUnit.Warning))
                    ed.WriteMessage("\n  Warning: " + liveResolution.MapUnit.Warning);
                SsugroSurfaceHorizon? hz = liveResolution.MapUnit.SurfaceHorizon;
                if (hz != null && hz.PctSand != null && hz.PctSilt != null && hz.PctClay != null)
                {
                    ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                        "\n  PSD: sand {0:0.#}%  silt {1:0.#}%  clay {2:0.#}%",
                        hz.PctSand, hz.PctSilt, hz.PctClay));
                }
            }

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  {0} ({1})", soil.Name, soil.Key));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Region: {0}   Texture: {1}", soil.Region, soil.Texture));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  HSG: {0}   K-factor: {1:0.##}   fc: {2:0.##} in/hr",
                soil.HydrologicSoilGroup, soil.KFactor, soil.InfiltrationRateInPerHr));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Drainage: {0}", soil.Drainage));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  BMP '{0}' suitability: {1}", suggestion.BmpType, suggestion.Suitability));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  {0}", suggestion.Rationale));
            if (suggestion.Alternatives.Count > 0)
            {
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  Alternatives: {0}", string.Join(", ", suggestion.Alternatives)));
            }

            ed.WriteMessage("\n");
        }

        private static StateComplianceConfig PromptState(Editor ed)
        {
            var opts = new PromptKeywordOptions(
                "\nRegulatory state [NC/SC/VA/FL/TX/CA/NY/DEFAULT]")
            {
                AllowNone = true,
            };
            foreach (string stateCode in StateCompliance.AvailableStateCodes())
                opts.Keywords.Add(stateCode);
            opts.Keywords.Add(StateCompliance.DefaultCode);
            opts.Keywords.Default = "NC";

            PromptResult res = ed.GetKeywords(opts);
            string selected = res.Status == PromptStatus.OK ? res.StringResult : "NC";
            return StateCompliance.Get(selected);
        }

        private static double PromptRainfall(Editor ed, double defaultIn, string message)
        {
            var opts = new PromptDoubleOptions(message)
            {
                DefaultValue = defaultIn,
                UseDefaultValue = true,
                AllowZero = false,
                AllowNegative = false,
            };
            PromptDoubleResult res = ed.GetDouble(opts);
            return res.Status == PromptStatus.OK ? res.Value : defaultIn;
        }

        private static double PromptDouble(Editor ed, string message, double defaultValue)
        {
            var opts = new PromptDoubleOptions(message)
            {
                DefaultValue = defaultValue,
                UseDefaultValue = true,
                AllowZero = true,
                AllowNegative = false,
            };
            PromptDoubleResult res = ed.GetDouble(opts);
            return res.Status == PromptStatus.OK ? res.Value : defaultValue;
        }

        private static void WriteCalcSteps(Editor ed, System.Collections.Generic.IEnumerable<CalcStep> steps)
        {
            ed.WriteMessage("\n  Calculation steps:");
            foreach (CalcStep step in steps)
                ed.WriteMessage("\n    " + step);
        }

        private static Document Active()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) throw new InvalidOperationException("No active drawing.");
            return doc;
        }
    }
}