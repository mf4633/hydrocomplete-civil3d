using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using HydroComplete.Civil3D.Reading;
using HydroComplete.Civil3D.Writing;
using HydroComplete.Engine;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace HydroComplete.Civil3D.Commands
{
    /// <summary>
    /// HC_DETENTION, HC_BMP_SIZE, HC_WQ_TRAIN, HC_SEDIMENT_BASIN — detention routing,
    /// BMP sizing, treatment trains, and sediment basin design wired to the engine.
    /// </summary>
    public sealed class AdvancedStormwaterCommands
    {
        [CommandMethod("HC_DETENTION")]
        public void DetentionRoutingCommand()
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
                $"\nDesign rainfall depth for inflow hydrograph [{state.Code}]");

            List<DetentionRouting.StorageIndicationPoint> storageCurve;
            string storageDesc;
            List<OutletStructures.OutletDefinition> pondOutlets;

            if (PromptStorageMode(ed) == StorageMode.Prismatic)
            {
                double maxStorage = PromptDouble(ed, "\nPrismatic pond max storage, ft³", 50_000.0);
                double avgDepth = PromptDouble(ed, "\nAverage pond depth, ft", 8.0);
                pondOutlets = PromptOutlets(ed);
                storageCurve = DetentionRouting.BuildPrismaticStorageIndicationCurve(maxStorage, pondOutlets, avgDepth);
                storageDesc = string.Format(CultureInfo.InvariantCulture,
                    "Prismatic pond (V_max={0:0} ft³, avg depth={1:0.##} ft)", maxStorage, avgDepth);
            }
            else
            {
                var elevArea = PromptElevationAreaTable(ed);
                var ssResult = StageStorage.BuildFromElevationArea(elevArea);
                pondOutlets = PromptOutlets(ed);
                storageCurve = DetentionRouting.BuildStorageIndicationCurve(ssResult.Points, pondOutlets);
                storageDesc = string.Format(CultureInfo.InvariantCulture,
                    "Stage-storage from {0} elevation-area point(s), V_total={1:0} ft³",
                    elevArea.Count, ssResult.TotalStorageFt3);
            }

            double totalArea = catchments.Sum(c => c.AreaAcres);
            double systemTc = catchments.Max(c => c.TcMinutes);
            if (systemTc <= 0) systemTc = CatchmentReader.DefaultTcMinutes;

            ScsRunoff.CompositeRunoffResult runoff =
                ScsRunoff.ComputeComposite(catchments, rainfall);
            ScsUnitHydrograph.UnitHydrographResult uh =
                ScsUnitHydrograph.Generate(totalArea, systemTc);
            List<DetentionRouting.HydrographPoint> inflow =
                DetentionRouting.InflowFromUnitHydrograph(uh, runoff.CompositeRunoffDepthInches);

            double timestep = PromptDouble(ed, "\nRouting time step, hours", DetentionRouting.DefaultTimestepHours);
            DetentionRouting.RoutingResult routing =
                DetentionRouting.Route(inflow, storageCurve, timestep);
            double continuityErr = DetentionRouting.ContinuityErrorPercent(routing);

            var outletDescs = DescribeOutlets(pondOutlets);
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: detention routing ({0}, {1} catchments) ---",
                state.Name, catchments.Count));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Storage: {0}", storageDesc));
            foreach (string outletLine in outletDescs)
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture, "\n  Outlet: {0}", outletLine));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Inflow: SCS UH (A={0:0.000} ac, Tc={1:0.0} min, Q_depth={2:0.###} in)",
                totalArea, systemTc, runoff.CompositeRunoffDepthInches));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Q_in,peak = {0:0.00} cfs   Q_out,peak = {1:0.00} cfs   attenuation = {2:0.1}%",
                routing.PeakInflowCfs, routing.PeakOutflowCfs, routing.ReductionPercent));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  S_max = {0:0} ft³   peak elev = {1:0.##} ft   continuity error = {2:0.2}%",
                routing.PeakStorageFt3, routing.PeakElevationFt, continuityErr));
            WriteCalcSteps(ed, routing.Steps);
            var detentionReport = new DetentionReportSection
            {
                StorageDescription = storageDesc,
                RunoffDepthIn = runoff.CompositeRunoffDepthInches,
                DrainageAreaAcres = totalArea,
                SystemTcMinutes = systemTc,
                Result = routing,
            };
            detentionReport.OutletDescriptions.AddRange(outletDescs);
            OfferStormwaterHtmlExport(ed, doc, new StormwaterReportData { Detention = detentionReport });
            ed.WriteMessage("\n");
        }

        [CommandMethod("HC_BMP_SIZE")]
        public void BmpSizeCommand()
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
            string bmpType = PromptBmpType(ed);
            BmpDefinition bmp = BmpLibrary.GetBmp(bmpType);

            double totalArea = catchments.Sum(c => c.AreaAcres);
            WaterQualityEngine.WqvResult wqv =
                WaterQualityEngine.ComputeWqvFromCatchments(catchments, state.WqVolumeFactorInches);

            WaterQualityEngine.BmpSizingResult sizing = WaterQualityEngine.SizeBmp(
                bmpType,
                state.WqVolumeFactorInches,
                totalArea,
                wqv.ImperviousPercent);

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: BMP sizing ({0}, {1}) ---",
                bmp.Name, state.Name));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Area = {0:0.000} ac   I = {1:0.#}%   WQ storm = {2:0.##} in",
                totalArea, wqv.ImperviousPercent, state.WqVolumeFactorInches));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  WQV = {0:0} cf   treated volume = {1:0} cf",
                sizing.TotalWqvCf, sizing.TreatedVolumeCf));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Surface area = {0:0} sf ({1:0.##}% site footprint)",
                sizing.SurfaceAreaSf, sizing.FootprintPercent));
            if (sizing.LengthFt.HasValue)
            {
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  Length = {0:0.0} ft   width = {1:0.0} ft",
                    sizing.LengthFt.Value, sizing.WidthFt ?? 0.0));
            }

            WriteCalcSteps(ed, sizing.Steps);
            OfferStormwaterHtmlExport(ed, doc, new StormwaterReportData
            {
                BmpSizing = new BmpSizingReportSection
                {
                    Result = sizing,
                    DesignStormInches = state.WqVolumeFactorInches,
                    DrainageAreaAcres = totalArea,
                    ImperviousPercent = wqv.ImperviousPercent,
                },
            });
            ed.WriteMessage("\n");
        }

        [CommandMethod("HC_WQ_TRAIN")]
        public void TreatmentTrainCommand()
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
            string landUse = PromptLandUse(ed);
            IReadOnlyList<string> bmpChain = PromptBmpChain(ed);
            if (bmpChain.Count == 0)
            {
                ed.WriteMessage("\n  Treatment train cancelled (no BMPs selected).\n");
                return;
            }

            double rainfall = state.WqVolumeFactorInches;
            double totalArea = catchments.Sum(c => c.AreaAcres);
            ScsRunoff.CompositeRunoffResult runoff =
                ScsRunoff.ComputeComposite(catchments, rainfall);
            double runoffDepth = runoff.CompositeRunoffDepthInches;

            var initialLoads = AggregateEmcLoads(catchments, landUse, runoffDepth);
            WaterQualityEngine.TreatmentTrainResult train =
                WaterQualityEngine.ApplyTreatmentTrain(initialLoads, bmpChain);

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: BMP treatment train ({0}, {1} BMPs, {2} catchments) ---",
                state.Name, bmpChain.Count, catchments.Count));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Land use = {0}   runoff depth = {1:0.###} in   area = {2:0.000} ac",
                landUse, runoffDepth, totalArea));
            ed.WriteMessage("\n  Pollutant        Influent(lbs)  Effluent(lbs)  Removed(lbs)  eta");
            foreach (string pollutant in Pollutant.Core)
            {
                train.InitialLoadsLbs.TryGetValue(pollutant, out double influent);
                train.FinalEffluentLbs.TryGetValue(pollutant, out double effluent);
                train.TotalRemovedLbs.TryGetValue(pollutant, out double removed);
                train.OverallRemovalEfficiency.TryGetValue(pollutant, out double eta);
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  {0,-14} {1,13:0.####} {2,13:0.####} {3,13:0.####} {4,6:0.1}%",
                    pollutant, influent, effluent, removed, eta * 100.0));
            }

            ed.WriteMessage("\n  BMP chain:");
            for (int i = 0; i < train.BmpSteps.Count; i++)
            {
                WaterQualityEngine.TreatmentTrainBmpStep step = train.BmpSteps[i];
                BmpDefinition bmp = BmpLibrary.GetBmp(step.BmpType);
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n    {0}. {1} ({2})", i + 1, bmp.Name, step.BmpType));
            }

            WriteCalcSteps(ed, train.Steps);
            OfferStormwaterHtmlExport(ed, doc, new StormwaterReportData
            {
                TreatmentTrain = new TreatmentTrainReportSection
                {
                    LandUse = landUse,
                    RunoffDepthIn = runoffDepth,
                    DrainageAreaAcres = totalArea,
                    BmpChain = bmpChain,
                    Result = train,
                },
            });
            ed.WriteMessage("\n");
        }

        [CommandMethod("HC_SEDIMENT_BASIN")]
        public void SedimentBasinCommand()
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
            double peakQ = PromptPeakFlowCfs(ed, doc.Database, catchments);
            double totalArea = catchments.Sum(c => c.AreaAcres);

            double slopePct = PromptDouble(ed, "\nAverage catchment slope, percent", 5.0);
            double lengthFt = PromptDouble(ed, "\nRepresentative slope length, ft", 300.0);
            double rFactor = PromptDouble(ed, "\nRUSLE R-factor", state.DefaultRFactor);

            var rusleRows = catchments
                .Select(cm => SedimentEngine.Rusle(cm.AreaAcres, slopePct, lengthFt, cm.RunoffC, rFactor, name: cm.Name))
                .ToList();
            double sedimentYield = SedimentEngine.WeightedAverageSoilLoss(rusleRows);

            SedimentBasin.DesignResult design = SedimentBasin.Design(
                peakQ, totalArea, sedimentYield);

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: sediment basin ({0}, Q={1:0.00} cfs) ---",
                state.Name, peakQ));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Drainage area = {0:0.000} ac   sediment yield = {1:0.##} tons/ac/yr",
                totalArea, sedimentYield));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Surface area = {0:0} sf   L = {1:0.0} ft   W = {2:0.0} ft   depth = {3:0.0} ft",
                design.SurfaceAreaSf, design.LengthFt, design.WidthFt, design.DepthFt));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Pool volume = {0:0} cf   sediment storage = {1:0} cf   total = {2:0} cf",
                design.PoolVolumeCf, design.SedimentStorageCf, design.TotalVolumeCf));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Forebay = {0:0} cf ({1:0.0} x {2:0.0} ft)   trapping = {3:0.#}% ({4})",
                design.ForebayVolumeCf, design.ForebayLengthFt, design.ForebayWidthFt,
                design.TrappingEfficiencyPct, design.TrapEfficiencyMethod));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Dewatering time = {0:0} hr", design.DewateringTimeHr));

            WriteCalcSteps(ed, design.Steps);
            OfferStormwaterHtmlExport(ed, doc, new StormwaterReportData
            {
                SedimentBasin = new SedimentBasinReportSection
                {
                    DesignFlowCfs = peakQ,
                    DrainageAreaAcres = totalArea,
                    SedimentYieldTonsPerAcreYr = sedimentYield,
                    Result = design,
                },
            });
            ed.WriteMessage("\n");
        }

        private enum StorageMode
        {
            ElevationArea,
            Prismatic,
        }

        private static StorageMode PromptStorageMode(Editor ed)
        {
            var opts = new PromptKeywordOptions("\nPond storage curve [ElevArea/Prismatic]")
            {
                AllowNone = true,
            };
            opts.Keywords.Add("ElevArea");
            opts.Keywords.Add("Prismatic");
            opts.Keywords.Default = "ElevArea";
            PromptResult res = ed.GetKeywords(opts);
            if (res.Status != PromptStatus.OK) return StorageMode.ElevationArea;
            return string.Equals(res.StringResult, "Prismatic", StringComparison.OrdinalIgnoreCase)
                ? StorageMode.Prismatic
                : StorageMode.ElevationArea;
        }

        private static List<StageStorage.ElevationAreaPoint> PromptElevationAreaTable(Editor ed)
        {
            int count = (int)Math.Round(PromptDouble(ed, "\nNumber of elevation-area points (min 2)", 3.0));
            count = Math.Max(2, count);

            var table = new List<StageStorage.ElevationAreaPoint>();
            for (int i = 0; i < count; i++)
            {
                double elev = PromptDouble(ed, $"\n  Point {i + 1} elevation, ft", i * 2.0);
                double area = PromptDouble(ed, $"\n  Point {i + 1} surface area, ft²", 1000.0 + i * 500.0);
                table.Add(new StageStorage.ElevationAreaPoint { ElevationFt = elev, AreaFt2 = area });
            }

            return table.OrderBy(p => p.ElevationFt).ToList();
        }

        private static List<OutletStructures.OutletDefinition> PromptOutlets(Editor ed)
        {
            int count = (int)Math.Round(PromptDouble(ed, "\nNumber of pond outlets", 1.0));
            count = Math.Max(1, count);

            var outlets = new List<OutletStructures.OutletDefinition>();
            for (int i = 0; i < count; i++)
            {
                string label = $"Outlet {i + 1}";
                var kindOpts = new PromptKeywordOptions($"\n{label} type [Orifice/Weir]")
                {
                    AllowNone = true,
                };
                kindOpts.Keywords.Add("Orifice");
                kindOpts.Keywords.Add("Weir");
                kindOpts.Keywords.Default = i == 0 ? "Orifice" : "Weir";
                PromptResult kindRes = ed.GetKeywords(kindOpts);
                bool isWeir = kindRes.Status == PromptStatus.OK
                    && string.Equals(kindRes.StringResult, "Weir", StringComparison.OrdinalIgnoreCase);

                if (isWeir)
                {
                    outlets.Add(new OutletStructures.WeirOutlet
                    {
                        Name = label,
                        LengthFt = PromptDouble(ed, $"\n{label} weir length, ft", 8.0),
                        CrestElevFt = PromptDouble(ed, $"\n{label} crest elevation, ft", 2.0 + i),
                        Cw = PromptDouble(ed, $"\n{label} weir coefficient Cw", 3.0),
                    });
                }
                else
                {
                    outlets.Add(new OutletStructures.OrificeOutlet
                    {
                        Name = label,
                        DiameterInches = PromptDouble(ed, $"\n{label} orifice diameter, in", 6.0),
                        InvertElevFt = PromptDouble(ed, $"\n{label} invert elevation, ft", 0.5),
                        Cd = PromptDouble(ed, $"\n{label} discharge coefficient Cd", 0.6),
                    });
                }
            }

            return outlets;
        }

        private static List<string> DescribeOutlets(IReadOnlyList<OutletStructures.OutletDefinition> outlets)
        {
            if (outlets.Count == 0)
                return new List<string> { "(none)" };

            var descriptions = new List<string>();
            foreach (OutletStructures.OutletDefinition outlet in outlets)
            {
                switch (outlet)
                {
                    case OutletStructures.OrificeOutlet o:
                        descriptions.Add(string.Format(CultureInfo.InvariantCulture,
                            "{0}: orifice D={1:0.#} in, invert={2:0.##} ft, Cd={3:0.##}",
                            o.Name, o.DiameterInches, o.InvertElevFt, o.Cd));
                        break;
                    case OutletStructures.WeirOutlet w:
                        descriptions.Add(string.Format(CultureInfo.InvariantCulture,
                            "{0}: weir L={1:0.##} ft, crest={2:0.##} ft, Cw={3:0.##}",
                            w.Name, w.LengthFt, w.CrestElevFt, w.Cw));
                        break;
                    case OutletStructures.RiserOutlet r:
                        descriptions.Add(string.Format(CultureInfo.InvariantCulture,
                            "{0}: riser D={1:0.#} in, crest={2:0.##} ft",
                            r.Name, r.DiameterInches, r.CrestElevFt));
                        break;
                    default:
                        descriptions.Add(outlet.Name);
                        break;
                }
            }

            return descriptions;
        }

        private static string PromptBmpType(Editor ed)
        {
            var opts = new PromptKeywordOptions(
                "\nBMP type [Bioretention/WetPond/SandFilter/VegetatedSwale]")
            {
                AllowNone = true,
            };
            opts.Keywords.Add("Bioretention");
            opts.Keywords.Add("WetPond");
            opts.Keywords.Add("SandFilter");
            opts.Keywords.Add("VegetatedSwale");
            opts.Keywords.Default = "Bioretention";
            PromptResult res = ed.GetKeywords(opts);
            string selected = res.Status == PromptStatus.OK ? res.StringResult : "Bioretention";

            switch (selected)
            {
                case "WetPond": return BmpType.WetPond;
                case "SandFilter": return BmpType.SandFilter;
                case "VegetatedSwale": return BmpType.VegetatedSwale;
                default: return BmpType.Bioretention;
            }
        }

        private static string PromptLandUse(Editor ed)
        {
            var opts = new PromptKeywordOptions("\nLand use [Residential/Commercial/Industrial]")
            {
                AllowNone = true,
            };
            opts.Keywords.Add("Residential");
            opts.Keywords.Add("Commercial");
            opts.Keywords.Add("Industrial");
            opts.Keywords.Default = "Residential";
            PromptResult res = ed.GetKeywords(opts);
            string selected = res.Status == PromptStatus.OK ? res.StringResult : "Residential";

            switch (selected)
            {
                case "Commercial": return LandUse.Commercial;
                case "Industrial": return LandUse.Industrial;
                default: return LandUse.Residential;
            }
        }

        private static IReadOnlyList<string> PromptBmpChain(Editor ed)
        {
            var opts = new PromptStringOptions(
                "\nBMP chain (comma-separated: bioretention, wet-pond, sand-filter, vegetated-swale)")
            {
                AllowSpaces = true,
            };
            PromptResult res = ed.GetString(opts);
            if (res.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(res.StringResult))
                return Array.Empty<string>();

            var chain = new List<string>();
            foreach (string token in res.StringResult.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                string key = NormalizeBmpKey(token.Trim());
                if (!string.IsNullOrEmpty(key))
                    chain.Add(key);
            }

            return chain;
        }

        private static string NormalizeBmpKey(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return "";

            string lower = token.ToLowerInvariant().Replace(" ", "-", StringComparison.Ordinal);
            switch (lower)
            {
                case "bioretention":
                case "wet-pond":
                case "wetpond":
                    return lower == "wetpond" ? BmpType.WetPond : lower;
                case "sand-filter":
                case "sandfilter":
                    return BmpType.SandFilter;
                case "vegetated-swale":
                case "swale":
                    return BmpType.VegetatedSwale;
                default:
                    try
                    {
                        BmpLibrary.GetBmp(lower);
                        return lower;
                    }
                    catch (ArgumentException)
                    {
                        return "";
                    }
            }
        }

        private static Dictionary<string, double> AggregateEmcLoads(
            IReadOnlyList<Catchment> catchments,
            string landUse,
            double runoffDepthIn)
        {
            var totals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (string pollutant in Pollutant.Core)
                totals[pollutant] = 0.0;

            foreach (Catchment cm in catchments)
            {
                foreach (string pollutant in Pollutant.Core)
                {
                    WaterQualityEngine.EmcLoadResult load =
                        WaterQualityEngine.CalculateEmcLoad(pollutant, landUse, runoffDepthIn, cm.AreaAcres);
                    totals[pollutant] += load.EmcLoadLbs;
                }
            }

            return totals;
        }

        private static double PromptPeakFlowCfs(Editor ed, Database db, IReadOnlyList<Catchment> catchments)
        {
            bool useRational = PromptYesNo(ed, "\nCompute peak Q from Rational method + IDF", defaultYes: true);
            if (!useRational)
                return PromptDouble(ed, "\nDesign peak flow Q, cfs", 10.0);

            Atlas14Resolution? resolution = IdfPrompts.PromptPreset(ed, db);
            Rational.PeakFlowResult q;
            double systemTc = catchments.Max(c => c.TcMinutes);

            if (resolution != null)
            {
                q = resolution.PeakFromCatchments(catchments);
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  IDF: {0} [{1}, {2}-yr]  Tc={3:0.0} min  i={4:0.000} in/hr  Q={5:0.00} cfs",
                    resolution.DisplayLabel, resolution.SourceLabel, resolution.ReturnPeriodYears,
                    systemTc, q.IntensityInHr, q.PeakFlowCfs));
            }
            else
            {
                IdfCurve idf = IdfPrompts.PromptCustomIdfCurve(ed);
                var intensity = idf.Intensity(systemTc);
                q = Rational.Peak(catchments, intensity.IntensityInHr);
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  Custom IDF  Tc={0:0.0} min  i={1:0.000} in/hr  Q={2:0.00} cfs",
                    systemTc, q.IntensityInHr, q.PeakFlowCfs));
            }

            return q.PeakFlowCfs;
        }

        private static void OfferStormwaterHtmlExport(Editor ed, Document doc, StormwaterReportData data)
        {
            if (!data.HasContent) return;
            if (!PromptYesNo(ed, "\nExport stormwater HTML report", defaultYes: false))
                return;

            string drawingName = Path.GetFileNameWithoutExtension(doc.Name);
            if (string.IsNullOrWhiteSpace(drawingName))
                drawingName = "untitled";

            string path = HtmlReportWriter.WriteStormwater(drawingName, data);
            ed.WriteMessage($"\n  Stormwater HTML report -> {path}");
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

        private static bool PromptYesNo(Editor ed, string message, bool defaultYes)
        {
            var opts = new PromptKeywordOptions(message)
            {
                AllowNone = true,
            };
            opts.Keywords.Add("Yes");
            opts.Keywords.Add("No");
            opts.Keywords.Default = defaultYes ? "Yes" : "No";
            PromptResult res = ed.GetKeywords(opts);
            if (res.Status != PromptStatus.OK) return defaultYes;
            return string.Equals(res.StringResult, "Yes", StringComparison.OrdinalIgnoreCase);
        }

        private static void WriteCalcSteps(Editor ed, IEnumerable<CalcStep> steps)
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