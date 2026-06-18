using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using HydroComplete.Civil3D.Reading;
using HydroComplete.Civil3D.Writing;
using HydroComplete.Engine;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace HydroComplete.Civil3D.Commands
{
    /// <summary>
    /// HC_VALIDATE — design-criteria review of analyzed pipe networks
    /// (slope, capacity, velocity, cover, size progression, HGL flooding).
    /// </summary>
    public sealed class ValidateCommands
    {
        [CommandMethod("HC_VALIDATE")]
        public void Validate()
        {
            Document doc = Active();
            Editor ed = doc.Editor;
            CivilDocument civilDoc = CivilApplication.ActiveDocument;

            var pipes = PipeNetworkReader.ReadAll(doc.Database, civilDoc);
            if (pipes.Count == 0)
            {
                ed.WriteMessage("\nNo pipe networks found in this drawing.\n");
                return;
            }

            DesignFlowContext flow = DesignFlowResolver.Prompt(ed, doc.Database, civilDoc, pipes, doc.Name);
            var capacity = CapacityReportBuilder.Build(
                pipes, flow.DesignFlowCfs, flow.PipeFlowCfs);

            if (capacity.Rows.Count == 0)
            {
                ed.WriteMessage("\nNo pipes with design flow > 0 to validate.\n");
                return;
            }

            var nodes = ReadStructureNodes(doc.Database, civilDoc, pipes);
            ApplyNodeHglFromProfile(pipes, flow, nodes);

            var reviewPipes = BuildReviewPipes(capacity.Rows);
            var findings = DesignReview.ReviewNetwork(reviewPipes, nodes);

            string qHeader = capacity.IsRouted
                ? string.Format(CultureInfo.InvariantCulture,
                    "routed Q, system total={0:0.0} cfs", flow.DesignFlowCfs)
                : string.Format(CultureInfo.InvariantCulture,
                    "Q={0:0.0} cfs", flow.DesignFlowCfs);

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: design validation ({0}, {1} pipes) ---",
                qHeader, reviewPipes.Count));

            int errors = 0;
            int warnings = 0;
            foreach (DesignFinding finding in findings.OrderBy(f => f.Severity).ThenBy(f => f.Id))
            {
                string tag = finding.Severity == DesignFindingSeverity.Error ? "ERROR" : "WARN ";
                ed.WriteMessage($"\n  [{tag}] {finding.Message}");
                if (finding.Severity == DesignFindingSeverity.Error) errors++;
                else warnings++;
            }

            if (findings.Count == 0)
                ed.WriteMessage("\n  All checked design criteria passed.");
            else
            {
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  Summary: {0} error(s), {1} warning(s).", errors, warnings));
            }

            ed.WriteMessage("\n");
        }

        internal static List<ReviewPipeInput> BuildReviewPipes(
            IReadOnlyList<CapacityPipeRow> capacityRows)
        {
            var review = new List<ReviewPipeInput>();
            foreach (CapacityPipeRow row in capacityRows)
            {
                ReadPipe rp = row.Pipe;
                double slope = rp.LengthFt > 0
                    ? (rp.UpstreamInvertFt - rp.DownstreamInvertFt) / rp.LengthFt
                    : 0.0;

                review.Add(new ReviewPipeInput
                {
                    Id = rp.NetworkName + "/" + rp.PipeName,
                    UpstreamNodeId = rp.UpstreamStructureId.Handle.ToString(),
                    DownstreamNodeId = rp.DownstreamStructureId.Handle.ToString(),
                    DiameterFt = rp.Segment.DiameterFt,
                    Slope = slope,
                    DesignFlowCfs = row.DesignFlowCfs,
                    FullCapacityCfs = row.QFullCfs,
                    VelocityFps = row.NormalDepth.VelocityFps,
                    Surcharged = row.Surcharged,
                    UpstreamInvertFt = rp.UpstreamInvertFt,
                    DownstreamInvertFt = rp.DownstreamInvertFt,
                });
            }

            return review;
        }

        internal static Dictionary<string, ReviewNodeInput> ReadStructureNodes(
            Database db,
            CivilDocument civilDoc,
            IReadOnlyList<ReadPipe> pipes)
        {
            var nodes = new Dictionary<string, ReviewNodeInput>(StringComparer.OrdinalIgnoreCase);
            ObjectIdCollection networkIds = civilDoc.GetPipeNetworkIds();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId nid in networkIds)
                {
                    if (!(tr.GetObject(nid, OpenMode.ForRead) is Network net)) continue;

                    foreach (ObjectId sid in net.GetStructureIds())
                    {
                        if (!(tr.GetObject(sid, OpenMode.ForRead) is Structure structure)) continue;

                        string id = sid.Handle.ToString();
                        nodes[id] = new ReviewNodeInput
                        {
                            Id = structure.Name ?? id,
                            RimFt = TryReadStructureRim(structure),
                            InvertFt = TryReadStructureInvert(structure),
                        };
                    }
                }

                tr.Commit();
            }

            foreach (ReadPipe rp in pipes)
            {
                EnsureNode(nodes, rp.UpstreamStructureId);
                EnsureNode(nodes, rp.DownstreamStructureId);
            }

            return nodes;
        }

        private static void EnsureNode(Dictionary<string, ReviewNodeInput> nodes, ObjectId structureId)
        {
            if (structureId.IsNull) return;
            string id = structureId.Handle.ToString();
            if (!nodes.ContainsKey(id))
                nodes[id] = new ReviewNodeInput { Id = id };
        }

        internal static void ApplyNodeHglFromProfile(
            IReadOnlyList<ReadPipe> pipes,
            DesignFlowContext flow,
            Dictionary<string, ReviewNodeInput> nodes)
        {
            var hglOptions = new HglProfileOptions
            {
                IncludeJunctionLosses = true,
                IncludeExitLoss = true,
            };

            foreach (NetworkTopology.OrderedNetwork net in NetworkTopology.BuildOrderedNetworks(pipes))
            {
                if (net.OrderedPipes.Count == 0) continue;

                List<NetworkReach> reaches = flow.IsRouted && flow.PipeFlowCfs != null
                    ? NetworkTopology.BuildReaches(net.OrderedPipes, flow.PipeFlowCfs, includeJunctionLosses: true)
                    : NetworkTopology.BuildReaches(net.OrderedPipes, flow.DesignFlowCfs, includeJunctionLosses: true);

                double tailwater = net.OrderedPipes[net.OrderedPipes.Count - 1].DownstreamInvertFt;
                List<HglProfilePoint> profile = Hgl.SteadyBackwaterFromOutfall(reaches, tailwater, hglOptions);

                for (int i = 0; i < net.OrderedPipes.Count && i < profile.Count; i++)
                {
                    ReadPipe rp = net.OrderedPipes[i];
                    HglProfilePoint point = profile[i];
                    double hglUs = point.HglUpstreamFt;
                    double hglDs = point.HglFt;

                    BumpNodeHgl(nodes, rp.UpstreamStructureId, hglUs);
                    BumpNodeHgl(nodes, rp.DownstreamStructureId, hglDs);
                }
            }
        }

        private static void BumpNodeHgl(
            Dictionary<string, ReviewNodeInput> nodes,
            ObjectId structureId,
            double hglFt)
        {
            if (structureId.IsNull) return;
            string id = structureId.Handle.ToString();
            if (!nodes.TryGetValue(id, out ReviewNodeInput? node))
            {
                node = new ReviewNodeInput { Id = id };
                nodes[id] = node;
            }

            if (!node.HglFt.HasValue || hglFt > node.HglFt.Value)
                node.HglFt = hglFt;
        }

        private static double? TryReadStructureRim(Structure structure)
        {
            return TryReadDouble(structure, "RimElevation", "RimElevationAtInsertion", "SurfaceElevation");
        }

        private static double? TryReadStructureInvert(Structure structure)
        {
            double? sump = TryReadDouble(structure, "SumpElevation", "FloorElevation", "InvertElevation");
            if (sump.HasValue) return sump.Value;

            double? rim = TryReadStructureRim(structure);
            double? depth = TryReadDouble(structure, "SumpDepth", "FloorDepth");
            if (rim.HasValue && depth.HasValue)
                return rim.Value - depth.Value;

            return null;
        }

        private static double? TryReadDouble(object target, params string[] memberNames)
        {
            Type type = target.GetType();
            foreach (string name in memberNames)
            {
                PropertyInfo? prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && prop.PropertyType == typeof(double))
                {
                    try
                    {
                        return (double)prop.GetValue(target)!;
                    }
                    catch
                    {
                    }
                }
            }

            return null;
        }

        private static Document Active()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) throw new InvalidOperationException("No active drawing.");
            return doc;
        }
    }
}