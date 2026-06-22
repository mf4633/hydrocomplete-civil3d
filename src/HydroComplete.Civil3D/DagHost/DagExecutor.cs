using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using HydroComplete.Engine;

namespace HydroComplete.Civil3D.DagHost
{
    /// <summary>
    /// Walks the DAG in topological order, runs each node's engine calculation,
    /// and writes computed outputs back onto each node for the WebView2 renderer.
    /// </summary>
    public sealed class DagExecutor
    {
        public string Execute(string dagJson, string orderJson)
        {
            JsonNode dag = JsonNode.Parse(dagJson)
                ?? throw new ArgumentException("Invalid DAG JSON");
            int[] order = JsonSerializer.Deserialize<int[]>(orderJson) ?? Array.Empty<int>();

            JsonArray nodes = dag["nodes"]?.AsArray() ?? new JsonArray();
            JsonArray edges = dag["edges"]?.AsArray() ?? new JsonArray();

            var nodeById = new Dictionary<int, JsonObject>();
            foreach (JsonNode? n in nodes)
            {
                if (n is JsonObject obj)
                {
                    int id = obj["id"]?["0"]?.GetValue<int>() ?? -1;
                    if (id >= 0) nodeById[id] = obj;
                }
            }

            // (toNode, toPort) → (fromNode, fromPort)
            var edgeMap = new Dictionary<(int, int), (int, int)>();
            foreach (JsonNode? e in edges)
            {
                if (e is not JsonObject eo) continue;
                int from = eo["from_node"]?["0"]?.GetValue<int>() ?? -1;
                int fp   = eo["from_port"]?.GetValue<int>() ?? 0;
                int to   = eo["to_node"]?["0"]?.GetValue<int>() ?? -1;
                int tp   = eo["to_port"]?.GetValue<int>() ?? 0;
                if (from >= 0 && to >= 0) edgeMap[(to, tp)] = (from, fp);
            }

            JsonNode GetOutput(int nodeId, int port)
            {
                if (!nodeById.TryGetValue(nodeId, out var n)) return JsonValue.Create(0.0);
                return n["outputs"]?[port.ToString()] ?? JsonValue.Create(0.0);
            }

            foreach (int nodeId in order)
            {
                if (!nodeById.TryGetValue(nodeId, out JsonObject? node)) continue;
                string kind   = node["kind"]?.GetValue<string>() ?? "";
                JsonObject cfg = node["config"]?.AsObject() ?? new JsonObject();
                var outs       = new JsonObject();
                try { RunNode(kind, cfg, edgeMap, nodeId, GetOutput, outs); }
                catch (Exception ex) { outs["error"] = JsonValue.Create(ex.Message); }
                node["outputs"] = outs;
            }

            return dag.ToJsonString();
        }

        private static void RunNode(
            string kind,
            JsonObject cfg,
            Dictionary<(int, int), (int, int)> edgeMap,
            int nodeId,
            Func<int, int, JsonNode> getOutput,
            JsonObject outs)
        {
            // Local helpers in method scope — available inside switch cases below.
            JsonNode Input(int p)
            {
                if (!edgeMap.TryGetValue((nodeId, p), out var src)) return JsonValue.Create(0.0);
                return getOutput(src.Item1, src.Item2);
            }
            double D(int p, double fb = 0.0)
            {
                try { return Input(p).GetValue<double>(); } catch { return fb; }
            }
            string Steps(IEnumerable<CalcStep> s)
            {
                var sb = new System.Text.StringBuilder();
                foreach (CalcStep c in s)
                    sb.Append($"{c.Label}={c.Value:0.####}{(string.IsNullOrEmpty(c.Units) ? "" : " " + c.Units)}; ");
                return sb.ToString();
            }

            switch (kind)
            {
                case "catchment":
                    outs["0"] = JsonValue.Create(Dbl(cfg, "area_acres",    5.0));
                    outs["1"] = JsonValue.Create(Dbl(cfg, "curve_number", 75.0));
                    break;

                case "rainfall_event":
                    outs["0"] = JsonValue.Create(Dbl(cfg, "depth_in", 3.5));
                    break;

                case "rational_method":
                {
                    double area = D(0, Dbl(cfg, "area_acres",     5.0));
                    double c    = Dbl(cfg, "runoff_c",            0.70);
                    double i    = Dbl(cfg, "intensity_in_hr",     2.5);
                    Rational.PeakFlowResult r = Rational.Peak(c, i, area);
                    outs["0"] = JsonValue.Create(r.PeakFlowCfs);
                    outs["steps"] = JsonValue.Create(Steps(r.Steps));
                    break;
                }

                case "scs_runoff":
                {
                    double cn   = D(0, Dbl(cfg, "curve_number", 75.0));
                    double rain = D(1, Dbl(cfg, "rainfall_in",   3.5));
                    double area = Dbl(cfg, "area_acres", 5.0);
                    double q    = ScsRunoff.RunoffDepthInches(rain, cn);
                    double vol  = q * area * 43560.0 / 12.0;
                    outs["0"] = JsonValue.Create(q);
                    outs["volume_cf"] = JsonValue.Create(vol);
                    break;
                }

                case "loss_method":
                {
                    double rain = D(0, Dbl(cfg, "rainfall_in",    3.5));
                    double cn   = Dbl(cfg, "curve_number",       75.0);
                    double dur  = Dbl(cfg, "duration_hr",        24.0);
                    int    ns   = Math.Max(1, (int)Dbl(cfg, "steps", 24));
                    double dt   = dur / ns;
                    string method = Str(cfg, "method", "curve_number");
                    var lp = method switch
                    {
                        "green_ampt" => new LossMethods.LossParameters
                        {
                            Method    = LossMethods.LossMethodType.GreenAmpt,
                            GreenAmpt = LossMethods.GreenAmptForSoilGroup(Str(cfg, "hsg", "B")),
                        },
                        "horton" => new LossMethods.LossParameters
                        {
                            Method = LossMethods.LossMethodType.Horton,
                            Horton = LossMethods.HortonForSoilGroup(Str(cfg, "hsg", "B")),
                        },
                        _ => new LossMethods.LossParameters
                        {
                            Method      = LossMethods.LossMethodType.CurveNumber,
                            CurveNumber = cn,
                        },
                    };
                    LossMethods.IncrementalLossResult r = LossMethods.ComputeIncremental(
                        TypeIiHyet(rain, ns), dt, lp);
                    outs["0"] = JsonValue.Create(r.TotalExcessIn);
                    outs["loss_in"] = JsonValue.Create(r.TotalLossIn);
                    outs["steps"] = JsonValue.Create(Steps(r.Steps));
                    break;
                }

                case "continuous_sim":
                {
                    double area = D(0, Dbl(cfg, "area_acres", 5.0));
                    ContinuousSimulation.ContinuousSimulationResult r = ContinuousSimulation.Run(
                        new ContinuousSimulation.SiteData
                        {
                            Location    = Str(cfg, "location",  "charlotte-nc"),
                            AreaAcres   = area,
                            CurveNumber = Dbl(cfg, "curve_number", 75.0),
                            LandUse     = Str(cfg, "land_use", "residential-medium"),
                            Years       = Math.Max(1, (int)Dbl(cfg, "years", 3.0)),
                        });
                    int yrs = r.Years;
                    outs["0"] = JsonValue.Create(r.AnnualAvgRunoffAcreIn);
                    outs["tss_lbs_yr"] = JsonValue.Create(
                        r.TotalLoadsLbs.TryGetValue(Pollutant.Tss, out double tss) ? tss / yrs : 0.0);
                    outs["tn_lbs_yr"] = JsonValue.Create(
                        r.TotalLoadsLbs.TryGetValue(Pollutant.Tn, out double tn) ? tn / yrs : 0.0);
                    break;
                }

                case "manning_pipe":
                {
                    double dia = Dbl(cfg, "diameter_ft", 1.5);
                    double s   = Dbl(cfg, "slope",       0.005);
                    double n   = Dbl(cfg, "manning_n",   0.013);
                    double q   = D(0, Dbl(cfg, "design_q_cfs", 5.0));
                    var seg    = new PipeSegment { DiameterFt = dia, Slope = s, ManningN = n };
                    Manning.CapacityResult   cap = Manning.Capacity(seg);
                    Manning.NormalDepthResult nd  = Manning.NormalDepth(seg, q);
                    outs["0"] = JsonValue.Create(cap.FullFlowCfs);
                    outs["1"] = JsonValue.Create(nd.RelativeDepth * dia);
                    outs["q_ratio"]    = JsonValue.Create(cap.FullFlowCfs > 0 ? q / cap.FullFlowCfs : 0.0);
                    outs["surcharged"] = JsonValue.Create(nd.Surcharged ? 1.0 : 0.0);
                    outs["steps"] = JsonValue.Create(Steps(cap.Steps));
                    break;
                }

                case "manning_channel":
                {
                    double b   = Dbl(cfg, "bottom_width_ft", 4.0);
                    double z   = Dbl(cfg, "side_slope",      3.0);
                    double s   = Dbl(cfg, "slope",           0.01);
                    double nc  = Dbl(cfg, "manning_n",       0.035);
                    double qin = D(0, Dbl(cfg, "design_q_cfs", 5.0));
                    ChannelHydraulics.NormalDepthResult r =
                        ChannelHydraulics.NormalDepth(b, z, nc, s, qin);
                    outs["0"] = JsonValue.Create(r.DepthFt);
                    outs["1"] = JsonValue.Create(r.VelocityFps);
                    outs["froude"] = JsonValue.Create(r.FroudeNumber);
                    outs["regime"] = JsonValue.Create(r.FlowRegime);
                    outs["steps"]  = JsonValue.Create(Steps(r.Steps));
                    break;
                }

                case "detention_pond":
                {
                    double area  = Dbl(cfg, "area_acres",       5.0);
                    double cn    = Dbl(cfg, "curve_number",    80.0);
                    double rain  = Dbl(cfg, "rainfall_in",      4.2);
                    double tc    = Dbl(cfg, "tc_min",           20.0);
                    double bArea = Dbl(cfg, "bottom_area_sf",  5000.0);
                    double slope = Dbl(cfg, "side_slope",        3.0);
                    double maxD  = Dbl(cfg, "max_depth_ft",      6.0);
                    double oDia  = Dbl(cfg, "orifice_dia_in",    6.0);
                    double oInv  = Dbl(cfg, "orifice_invert_ft", 0.0);

                    // Build a trapezoidal stage-storage table
                    var elevAreas = new List<StageStorage.ElevationAreaPoint>();
                    for (int i = 0; i <= 10; i++)
                    {
                        double d = maxD * i / 10.0;
                        double w = Math.Sqrt(bArea) + 2.0 * slope * d;
                        elevAreas.Add(new StageStorage.ElevationAreaPoint
                        {
                            ElevationFt = oInv + d,
                            AreaFt2     = w * w,
                        });
                    }
                    StageStorage.StageStorageResult ss = StageStorage.BuildFromElevationArea(elevAreas);

                    var outlets = new List<OutletStructures.OutletDefinition>
                    {
                        new OutletStructures.OrificeOutlet
                        {
                            DiameterInches = oDia,
                            Cd             = 0.60,
                            InvertElevFt   = oInv,
                        },
                    };

                    List<DetentionRouting.StorageIndicationPoint> storageCurve =
                        DetentionRouting.BuildStorageIndicationCurve(ss.Points, outlets, maxD + oInv);

                    double runoffIn = ScsRunoff.RunoffDepthInches(rain, cn);
                    ScsUnitHydrograph.UnitHydrographResult uh = ScsUnitHydrograph.Generate(area, tc);
                    List<DetentionRouting.HydrographPoint> inflow =
                        DetentionRouting.InflowFromUnitHydrograph(uh, runoffIn);
                    DetentionRouting.RoutingResult r = DetentionRouting.Route(inflow, storageCurve);
                    outs["0"] = JsonValue.Create(r.PeakOutflowCfs);
                    outs["1"] = JsonValue.Create(r.PeakStorageFt3);
                    outs["attenuation_pct"]  = JsonValue.Create(r.ReductionPercent);
                    outs["peak_storage_cf"]  = JsonValue.Create(r.PeakStorageFt3);
                    outs["steps"] = JsonValue.Create(Steps(r.Steps));
                    break;
                }

                case "water_quality_volume":
                {
                    double area   = D(0, Dbl(cfg, "area_acres",      5.0));
                    double rain   = D(1, Dbl(cfg, "design_storm_in", 1.0));
                    double imperv = Dbl(cfg, "impervious_pct", 50.0);
                    WaterQualityEngine.WqvResult r =
                        WaterQualityEngine.CalculateWqv(rain, area, imperv);
                    outs["0"] = JsonValue.Create(r.WqvCf);
                    outs["rv"] = JsonValue.Create(r.RunoffCoefficientRv);
                    outs["steps"] = JsonValue.Create(Steps(r.Steps));
                    break;
                }

                case "bmp_sizing":
                {
                    double area   = D(0, Dbl(cfg, "area_acres", 5.0));
                    double rain   = Dbl(cfg, "design_storm_in", 1.0);
                    double imperv = Dbl(cfg, "impervious_pct", 50.0);
                    string bt     = Str(cfg, "bmp_type", BmpType.Bioretention);
                    WaterQualityEngine.BmpSizingResult r =
                        WaterQualityEngine.SizeBmp(bt, rain, area, imperv);
                    outs["0"] = JsonValue.Create(r.SurfaceAreaSf);
                    outs["steps"] = JsonValue.Create(Steps(r.Steps));
                    break;
                }

                case "treatment_bmp":
                case "treatment_train":
                {
                    double area   = Dbl(cfg, "area_acres", 5.0);
                    double runoff = D(0, Dbl(cfg, "runoff_in", 0.5));
                    string lu     = Str(cfg, "land_use", LandUse.Commercial);
                    string[] chain = kind == "treatment_train"
                        ? Str(cfg, "bmp_chain", BmpType.Bioretention).Split(',')
                        : new[] { Str(cfg, "bmp_type", BmpType.Bioretention) };

                    WaterQualityEngine.EventPollutantLoadResult loads =
                        WaterQualityEngine.CalculateEventPollutantLoads(runoff, area, lu, 3);
                    WaterQualityEngine.TreatmentTrainResult r =
                        WaterQualityEngine.ApplyTreatmentTrain(loads.LoadsLbs, chain);
                    r.OverallRemovalEfficiency.TryGetValue(Pollutant.Tss, out double tssEta);
                    outs["0"] = JsonNode.Parse(JsonSerializer.Serialize(r.FinalEffluentLbs))!;
                    outs["1"] = JsonValue.Create(tssEta * 100.0);
                    outs["steps"] = JsonValue.Create(Steps(r.Steps));
                    break;
                }

                case "sediment_basin":
                {
                    double q     = D(0, Dbl(cfg, "design_q_cfs",          5.0));
                    double area  = Dbl(cfg, "area_acres",                  5.0);
                    double yield = D(1, Dbl(cfg, "sed_yield_tons_ac_yr",  10.0));
                    SedimentBasin.DesignResult r = SedimentBasin.Design(q, area, yield);
                    outs["0"] = JsonValue.Create(r.TrappingEfficiencyPct);
                    outs["1"] = JsonValue.Create(r.TotalVolumeCf);
                    outs["surface_sf"] = JsonValue.Create(r.SurfaceAreaSf);
                    outs["steps"] = JsonValue.Create(Steps(r.Steps));
                    break;
                }

                case "rusle_erosion":
                {
                    RusleAnalysis.SoilLossResult r = RusleAnalysis.SoilLoss(new RusleAnalysis.SiteInput
                    {
                        Region       = Str(cfg, "region",    "charlotte-nc"),
                        SoilType     = Str(cfg, "soil_type", "loam"),
                        Cover        = Str(cfg, "cover",     "construction-site"),
                        Practice     = Str(cfg, "practice",  "none"),
                        SlopeLengthFt = Dbl(cfg, "slope_length_ft", 100.0),
                        SlopePercent  = Dbl(cfg, "slope_pct",         5.0),
                        AreaAcres     = Dbl(cfg, "area_acres",         1.0),
                    });
                    outs["0"] = JsonValue.Create(r.SoilLossPerAcreTonsYr);
                    outs["total_tons_yr"] = JsonValue.Create(r.TotalSoilLossTonsYr);
                    outs["steps"] = JsonValue.Create(Steps(r.Steps));
                    break;
                }

                case "outfall":
                    outs["received"] = Input(0).DeepClone();
                    break;

                default:
                    outs["note"] = JsonValue.Create($"Node '{kind}' not yet wired.");
                    break;
            }
        }

        // ── Static helpers ───────────────────────────────────────────────────

        private static double Dbl(JsonObject cfg, string key, double fallback)
        {
            if (cfg.TryGetPropertyValue(key, out JsonNode? v) && v != null)
                try { return v.GetValue<double>(); } catch { }
            return fallback;
        }

        private static string Str(JsonObject cfg, string key, string fallback)
        {
            if (cfg.TryGetPropertyValue(key, out JsonNode? v) && v != null)
                try { return v.GetValue<string>(); } catch { }
            return fallback;
        }

        private static double[] TypeIiHyet(double total, int n)
        {
            double[] cum = {
                0,0.005,0.010,0.015,0.020,0.026,0.033,0.041,0.049,0.057,
                0.067,0.076,0.100,0.220,0.430,0.570,0.663,0.727,0.767,0.800,
                0.820,0.840,0.860,0.880,1.000,
            };
            var inc = new double[n];
            for (int i = 0; i < n; i++)
                inc[i] = (cum[i + 1] - cum[i]) * total;
            return inc;
        }
    }
}
