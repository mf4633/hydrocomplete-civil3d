using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using HydroComplete.Civil3D.DagHost;
using HydroComplete.Civil3D.Reading;
using HydroComplete.Engine;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace HydroComplete.Civil3D.Commands
{
    /// <summary>HC_DAG — opens the HydroComplete Model Builder, optionally pre-populated
    /// from catchments and pipe networks found in the active drawing.</summary>
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
                ed.WriteMessage("\nHydroComplete Model Builder opened.\n");

                // Auto-populate from drawing if it has usable data
                string? seedJson = TrySeedFromDrawing(doc, ed);
                if (seedJson != null)
                {
                    _ = _palette.SeedDagAsync(seedJson);
                    ed.WriteMessage("  Drawing data detected — DAG pre-populated. Press ▶ Run to compute.\n");
                }
                else
                {
                    ed.WriteMessage("  Use the Templates menu or drag nodes from the palette to start.\n");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nError opening DAG panel: {ex.Message}\n");
            }
        }

        // ── Drawing → DAG seed ────────────────────────────────────────────────

        /// Reads catchments and pipe networks from the drawing and builds a starter DAG JSON.
        /// Returns null if the drawing has nothing useful.
        private static string? TrySeedFromDrawing(Document doc, Editor ed)
        {
            try
            {
                CivilDocument civilDoc = CivilApplication.ActiveDocument;
                var pipes    = PipeNetworkReader.ReadAll(doc.Database, civilDoc);
                var catches  = CatchmentReader.ReadAll(doc.Database, civilDoc);

                if (pipes.Count == 0 && catches.Count == 0) return null;

                // Build a minimal DAG JSON in the same format the WASM editor uses.
                var nodes   = new JsonArray();
                var edges   = new JsonArray();
                uint nextId = 0;

                double x = 40, y = 60;
                const double DX = 220, DY = 110;

                // ── Catchment nodes ──────────────────────────────────────────
                var catchNodeIds = new Dictionary<string, uint>();
                foreach (Catchment c in catches.Take(8)) // cap at 8 to keep it readable
                {
                    uint id = nextId++;
                    catchNodeIds[c.Name] = id;
                    nodes.Add(MakeNode(id, "catchment", x, y + catchNodeIds.Count * DY, new Dictionary<string, object>
                    {
                        ["area_acres"]   = Math.Round(c.AreaAcres, 3),
                        ["curve_number"] = c.CurveNumber > 0 ? c.CurveNumber : 75.0,
                        ["land_use"]     = LandUseFromC(c.RunoffC),
                    }, c.Name));
                }

                // ── Rainfall event (shared) ──────────────────────────────────
                uint rainId = nextId++;
                nodes.Add(MakeNode(rainId, "rainfall_event", x, y - DY, new Dictionary<string, object>
                {
                    ["depth_in"] = 3.5,
                    ["duration_hr"] = 24.0,
                }, "Design Storm"));

                // ── SCS runoff node for each catchment ───────────────────────
                double scsX = x + DX;
                var scsNodeIds = new Dictionary<string, uint>();
                foreach (var (name, cid) in catchNodeIds)
                {
                    uint scsId = nextId++;
                    scsNodeIds[name] = scsId;
                    var c = catches.First(cc => cc.Name == name);
                    nodes.Add(MakeNode(scsId, "scs_runoff", scsX, y + scsNodeIds.Count * DY, new Dictionary<string, object>
                    {
                        ["curve_number"] = c.CurveNumber > 0 ? c.CurveNumber : 75.0,
                        ["area_acres"]   = Math.Round(c.AreaAcres, 3),
                    }));
                    edges.Add(MakeEdge(nextId++, cid, 0, scsId, 0));
                    edges.Add(MakeEdge(nextId++, rainId, 0, scsId, 1));
                }

                // ── Pipe network nodes ───────────────────────────────────────
                double pipeX = scsX + DX;
                var networkNames = pipes.Select(p => p.NetworkName)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToList();

                foreach (string netName in networkNames)
                {
                    var netPipes = pipes
                        .Where(p => string.Equals(p.NetworkName, netName, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    if (netPipes.Count == 0) continue;

                    // Use the largest pipe as representative
                    var rep = netPipes.OrderByDescending(p => p.Segment.DiameterFt).First();
                    uint pipeId = nextId++;
                    nodes.Add(MakeNode(pipeId, "manning_pipe", pipeX,
                        y + networkNames.IndexOf(netName) * DY * 2,
                        new Dictionary<string, object>
                        {
                            ["diameter_ft"]  = Math.Round(rep.Segment.DiameterFt, 3),
                            ["slope"]        = Math.Round(Math.Abs(rep.Segment.Slope), 5),
                            ["manning_n"]    = rep.Segment.ManningN > 0 ? rep.Segment.ManningN : 0.013,
                        }, $"{netName} ({netPipes.Count} pipes)"));
                }

                // ── Outfall ──────────────────────────────────────────────────
                uint outfallId = nextId++;
                double outX = pipeX + (pipes.Count > 0 ? DX : 0);
                nodes.Add(MakeNode(outfallId, "outfall", outX, y + DY, null, "Outfall"));

                var dag = new JsonObject
                {
                    ["nodes"] = nodes,
                    ["edges"] = edges,
                };
                return dag.ToJsonString();
            }
            catch
            {
                return null;
            }
        }

        private static string LandUseFromC(double c) => c switch
        {
            > 0.85 => "roadway",
            > 0.70 => "commercial",
            > 0.55 => "residential-high",
            > 0.40 => "residential-medium",
            _ => "residential-low",
        };

        private static JsonObject MakeNode(uint id, string kind, double x, double y,
            Dictionary<string, object>? config, string? label = null)
        {
            var cfg = new JsonObject();
            if (config != null)
            {
                foreach (var (k, v) in config)
                {
                    cfg[k] = v switch
                    {
                        double d => JsonValue.Create(d),
                        string s => JsonValue.Create(s),
                        _ => JsonValue.Create(v.ToString()),
                    };
                }
            }
            var node = new JsonObject
            {
                ["id"]      = new JsonObject { ["0"] = JsonValue.Create(id) },
                ["kind"]    = JsonValue.Create(kind),
                ["x"]       = JsonValue.Create(x),
                ["y"]       = JsonValue.Create(y),
                ["config"]  = cfg,
                ["outputs"] = new JsonObject(),
            };
            if (!string.IsNullOrEmpty(label))
                node["label"] = JsonValue.Create(label);
            return node;
        }

        private static JsonObject MakeEdge(uint id, uint from, int fromPort, uint to, int toPort) =>
            new JsonObject
            {
                ["id"]        = new JsonObject { ["0"] = JsonValue.Create(id) },
                ["from_node"] = new JsonObject { ["0"] = JsonValue.Create(from) },
                ["from_port"] = JsonValue.Create(fromPort),
                ["to_node"]   = new JsonObject { ["0"] = JsonValue.Create(to) },
                ["to_port"]   = JsonValue.Create(toPort),
            };

        // ── Run handler ───────────────────────────────────────────────────────

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
