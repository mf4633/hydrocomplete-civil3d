using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using HydroComplete.Civil3D.Reading;
using HydroComplete.Engine;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace HydroComplete.Civil3D.Commands
{
    /// <summary>HEC-22 grate-on-grade inlet interception checks (HC_INLETS).</summary>
    public sealed class InletCommands
    {
        private sealed class InletRow
        {
            public string Label { get; set; } = "";
            public string Structure { get; set; } = "";
            public double DesignQCfs { get; set; }
        }

        [CommandMethod("HC_INLETS")]
        public void InletInterception()
        {
            Document doc = Active();
            Editor ed = doc.Editor;
            CivilDocument civilDoc = CivilApplication.ActiveDocument;

            double grateLengthFt = PromptDouble(ed, "\nGrate length L (ft)", 5.0);
            double flowDepthFt = PromptDouble(ed, "Gutter flow depth d (ft)", 0.15);
            double gutterSlope = PromptDouble(ed, "Gutter slope S (ft/ft)", 0.005);

            var catchments = CatchmentReader.ReadAll(doc.Database, civilDoc);
            var pipes = PipeNetworkReader.ReadAll(doc.Database, civilDoc);
            ApplyDefaultTcFallback(catchments, pipes);

            List<InletRow> rows = BuildInletRows(ed, doc.Database, catchments, pipes);
            if (rows.Count == 0)
            {
                ed.WriteMessage("\nNo catchments or pipe-network structures found in this drawing.\n");
                return;
            }

            double capacity = InletCapacity.GrateCapacityCfs(grateLengthFt, flowDepthFt, gutterSlope);
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: HEC-22 grate-on-grade inlet check ({0} location(s)) ---",
                rows.Count));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Grate L={0:0.##} ft  d={1:0.###} ft  S={2:0.####}  ->  Q_cap={3:0.00} cfs",
                grateLengthFt, flowDepthFt, gutterSlope, capacity));
            ed.WriteMessage("\n  Location              Structure           Q_des(cfs)  Q_cap(cfs)  PASS");

            int passCount = 0;
            foreach (InletRow row in rows)
            {
                InletCapacity.InletCheck check = InletCapacity.CheckInlet(
                    row.DesignQCfs, grateLengthFt, flowDepthFt, gutterSlope);
                if (check.Ok) passCount++;

                string structure = string.IsNullOrWhiteSpace(row.Structure) ? "—" : row.Structure;
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  {0,-20} {1,-18} {2,10:0.00}  {3,10:0.00}  {4}",
                    Trim(row.Label, 20),
                    Trim(structure, 18),
                    check.DesignQCfs,
                    check.CapacityCfs,
                    check.Ok ? "OK" : "FAIL"));
            }

            int failCount = rows.Count - passCount;
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  {0} pass, {1} fail.  Formula: Q_cap = Cw*L*d^1.5*sqrt(S), Cw={2:0.#} (HEC-22).\n",
                passCount, failCount, InletCapacity.CompositeGutterCw));
        }

        private static List<InletRow> BuildInletRows(
            Editor ed,
            Database db,
            IReadOnlyList<Catchment> catchments,
            IReadOnlyList<ReadPipe> pipes)
        {
            if (catchments.Count > 0)
            {
                Atlas14Resolution? resolution = IdfPrompts.PromptPreset(ed, db);
                IdfCurve idf = resolution != null ? resolution.ToCurve() : IdfPrompts.PromptCustomIdfCurve(ed);

                if (resolution != null)
                {
                    ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                        "\n  IDF: {0} [{1}, {2}-yr]\n",
                        resolution.DisplayLabel, resolution.SourceLabel, resolution.ReturnPeriodYears));
                }

                var rows = new List<InletRow>();
                foreach (Catchment cm in catchments)
                {
                    Rational.PeakFlowResult peak = Rational.Peak(cm, idf);
                    rows.Add(new InletRow
                    {
                        Label = string.IsNullOrWhiteSpace(cm.Name) ? "(catchment)" : cm.Name,
                        Structure = cm.OutfallStructureName ?? "",
                        DesignQCfs = peak.PeakFlowCfs,
                    });
                }

                return rows;
            }

            if (pipes.Count == 0)
                return new List<InletRow>();

            double uniformQ = PromptDouble(ed, "\nUniform design flow Q per inlet (cfs)", 1.0);
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  No catchments — using uniform Q={0:0.00} cfs at each structure.\n", uniformQ));

            return CollectStructureInlets(pipes)
                .Select(s => new InletRow
                {
                    Label = s.Name,
                    Structure = s.Name,
                    DesignQCfs = uniformQ,
                })
                .OrderBy(r => r.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<(string Id, string Name)> CollectStructureInlets(IReadOnlyList<ReadPipe> pipes)
        {
            var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (ReadPipe rp in pipes)
            {
                AddStructure(seen, rp.UpstreamStructureId, StructureName(rp, rp.UpstreamStructureId));
                AddStructure(seen, rp.DownstreamStructureId, StructureName(rp, rp.DownstreamStructureId));
            }

            return seen
                .Select(pair => (Id: pair.Key, Name: pair.Value))
                .OrderBy(pair => pair.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void AddStructure(Dictionary<string, string> seen, ObjectId structureId, string name)
        {
            if (structureId.IsNull) return;
            string id = structureId.Handle.ToString();
            if (!seen.ContainsKey(id))
                seen[id] = string.IsNullOrWhiteSpace(name) ? id : name;
        }

        private static string StructureName(ReadPipe rp, ObjectId structureId)
        {
            if (structureId == rp.StartStructureId) return rp.StartStructureName;
            if (structureId == rp.EndStructureId) return rp.EndStructureName;
            if (structureId == rp.UpstreamStructureId)
                return rp.UpstreamStructureId == rp.StartStructureId
                    ? rp.StartStructureName
                    : rp.EndStructureName;
            return rp.DownstreamStructureId == rp.StartStructureId
                ? rp.StartStructureName
                : rp.EndStructureName;
        }

        private static void ApplyDefaultTcFallback(IList<Catchment> catchments, IReadOnlyList<ReadPipe> pipes)
        {
            if (catchments.Count == 0 || pipes.Count == 0) return;

            bool anyDefault = catchments.Any(cm =>
                Math.Abs(cm.TcMinutes - CatchmentReader.DefaultTcMinutes) < 0.01);
            if (!anyDefault) return;

            double networkTc = Reading.NetworkTcEstimator.EstimateSystemTc(pipes);
            if (networkTc <= 0) return;

            foreach (Catchment cm in catchments)
            {
                if (Math.Abs(cm.TcMinutes - CatchmentReader.DefaultTcMinutes) < 0.01)
                    cm.TcMinutes = networkTc;
            }
        }

        private static Document Active()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) throw new InvalidOperationException("No active drawing.");
            return doc;
        }

        private static double PromptDouble(Editor ed, string message, double dflt)
        {
            var opts = new PromptDoubleOptions(message)
            {
                DefaultValue = dflt,
                UseDefaultValue = true,
                AllowNegative = false,
                AllowZero = false,
            };
            PromptDoubleResult res = ed.GetDouble(opts);
            return res.Status == PromptStatus.OK ? res.Value : dflt;
        }

        private static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max - 1) + "~";
        }
    }
}