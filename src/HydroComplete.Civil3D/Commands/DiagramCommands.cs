using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using HydroComplete.Civil3D.Reading;
using HydroComplete.Civil3D.Writing;
using HydroComplete.Engine;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace HydroComplete.Civil3D.Commands
{
    /// <summary>HC_WQ_DIAGRAM — treatment-train node diagram (HTML/SVG) from catchment land use + BMP chain.</summary>
    public sealed class DiagramCommands
    {
        private static readonly string[] BmpTypes =
        {
            BmpType.Bioretention, BmpType.WetPond, "constructed-wetland",
            BmpType.VegetatedSwale, BmpType.SandFilter,
            "infiltration-trench", "permeable-pavement", "green-roof",
            "cistern", "level-spreader-filter",
        };

        private static readonly string[] LandUseKeywords =
        {
            "ResLow", "ResMed", "ResHigh", "Commercial", "Industrial",
            "Roadway", "Parking", "Institutional", "OpenSpace", "Construction", "Agricultural",
        };

        [CommandMethod("HC_WQ_DIAGRAM")]
        public void TreatmentTrainDiagramCommand()
        {
            Document doc = Active();
            Editor ed = doc.Editor;
            CivilDocument civilDoc = CivilApplication.ActiveDocument;

            var catchments = CatchmentReader.ReadAll(doc.Database, civilDoc);
            double totalArea;
            double runoffDepthIn;
            string landUse = PromptLandUse(ed);

            if (catchments.Count > 0)
            {
                totalArea = catchments.Sum(c => c.AreaAcres);
                double rainfall = PromptPositiveDouble(ed, "\nDesign storm rainfall, in", 1.0);
                ScsRunoff.CompositeRunoffResult runoff = ScsRunoff.ComputeComposite(catchments, rainfall);
                runoffDepthIn = runoff.CompositeRunoffDepthInches;
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  {0} catchments, {1:0.000} ac, runoff depth = {2:0.###} in",
                    catchments.Count, totalArea, runoffDepthIn));
            }
            else
            {
                ed.WriteMessage("\n  No catchments found — using manual site parameters.");
                totalArea = PromptPositiveDouble(ed, "\nDrainage area, acres", 5.0);
                runoffDepthIn = PromptPositiveDouble(ed, "\nRunoff depth, in", 0.5);
            }

            // Build pollutant loads from EMC
            Dictionary<string, double> initialLoads = AggregateEmcLoads(totalArea, landUse, runoffDepthIn);

            // BMP chain
            List<string> bmpChain = PromptBmpChain(ed);
            if (bmpChain.Count == 0)
            {
                ed.WriteMessage("\nNo BMPs selected — diagram cancelled.\n");
                return;
            }

            WaterQualityEngine.TreatmentTrainResult train =
                WaterQualityEngine.ApplyTreatmentTrain(initialLoads, bmpChain);

            string drawingName = System.IO.Path.GetFileNameWithoutExtension(doc.Name) ?? "drawing";
            string path = TreatmentTrainDiagramWriter.Write(drawingName, train, landUse, totalArea, runoffDepthIn);

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: treatment train diagram ---"));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Chain: {0}",
                string.Join(" → ", bmpChain.Select(k => BmpLibrary.GetBmp(k).Name))));
            foreach (string p in new[] { Pollutant.Tss, Pollutant.Tn, Pollutant.Tp })
            {
                train.OverallRemovalEfficiency.TryGetValue(p, out double eta);
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  {0} overall removal: {1:0.#}%", p.ToUpperInvariant(), eta * 100));
            }
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture, "\n  Diagram: {0}\n", path));
        }

        private static List<string> PromptBmpChain(Editor ed)
        {
            var chain = new List<string>();

            // Build keyword list: truncate long BMP names to max 24 chars for the prompt
            var bmpKeywords = BmpTypes.Select(t =>
            {
                BmpDefinition def = BmpLibrary.GetBmp(t);
                return (Type: t, Keyword: def.Name.Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", ""));
            }).ToList();

            string stop = "Done";
            while (chain.Count < 6)
            {
                string prompt = chain.Count == 0
                    ? "\nSelect BMP for chain [Done to cancel]"
                    : $"\nAdd next BMP ({chain.Count} selected) [Done to finish]";

                var opts = new PromptKeywordOptions(prompt) { AllowNone = true };
                foreach (var (_, kw) in bmpKeywords) opts.Keywords.Add(kw);
                opts.Keywords.Add(stop);
                opts.Keywords.Default = stop;

                PromptResult res = ed.GetKeywords(opts);
                if (res.Status != PromptStatus.OK ||
                    string.Equals(res.StringResult, stop, StringComparison.OrdinalIgnoreCase))
                    break;

                string? bmpType = bmpKeywords
                    .FirstOrDefault(b => string.Equals(b.Keyword, res.StringResult, StringComparison.OrdinalIgnoreCase))
                    .Type;
                if (bmpType != null)
                    chain.Add(bmpType);
            }

            return chain;
        }

        private static Dictionary<string, double> AggregateEmcLoads(
            double areaAcres, string landUse, double runoffDepthIn)
        {
            WaterQualityEngine.EventPollutantLoadResult loads =
                WaterQualityEngine.CalculateEventPollutantLoads(
                    runoffDepthIn, areaAcres, landUse, antecedentDryDays: 3);

            var dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in loads.LoadsLbs)
                dict[kv.Key] = kv.Value;
            return dict;
        }

        private static string PromptLandUse(Editor ed)
        {
            var opts = new PromptKeywordOptions(
                "\nLand use [ResLow/ResMed/ResHigh/Commercial/Industrial/" +
                "Roadway/Parking/Institutional/OpenSpace/Construction/Agricultural]")
            { AllowNone = true };
            foreach (string lu in LandUseKeywords) opts.Keywords.Add(lu);
            opts.Keywords.Default = "ResMed";
            PromptResult res = ed.GetKeywords(opts);
            string sel = res.Status == PromptStatus.OK ? res.StringResult : "ResMed";
            return sel switch
            {
                "ResLow"        => "residential-low",
                "ResHigh"       => "residential-high",
                "Commercial"    => LandUse.Commercial,
                "Industrial"    => LandUse.Industrial,
                "Roadway"       => "roadway",
                "Parking"       => "parking",
                "Institutional" => "institutional",
                "OpenSpace"     => "open-space",
                "Construction"  => "construction",
                "Agricultural"  => "agricultural",
                _               => "residential-medium",
            };
        }

        private static double PromptPositiveDouble(Editor ed, string message, double defaultValue)
        {
            var opts = new PromptDoubleOptions(message)
            {
                DefaultValue = defaultValue,
                UseDefaultValue = true,
                AllowZero = false,
                AllowNegative = false,
            };
            PromptDoubleResult res = ed.GetDouble(opts);
            return res.Status == PromptStatus.OK ? res.Value : defaultValue;
        }

        private static Document Active()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) throw new InvalidOperationException("No active drawing.");
            return doc;
        }
    }
}
