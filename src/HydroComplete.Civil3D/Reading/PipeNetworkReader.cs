using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using HydroComplete.Engine;

namespace HydroComplete.Civil3D.Reading
{
    /// <summary>An engine pipe paired with the Civil 3D names it came from.</summary>
    public sealed class ReadPipe
    {
        public ObjectId PipeId { get; set; }
        public string NetworkName { get; set; } = "";
        public string PipeName { get; set; } = "";
        public PipeSegment Segment { get; set; } = new PipeSegment();
        public double LengthFt { get; set; }
        public double StartInvertFt { get; set; }
        public double EndInvertFt { get; set; }
        public ObjectId StartStructureId { get; set; }
        public ObjectId EndStructureId { get; set; }
        public string StartStructureName { get; set; } = "";
        public string EndStructureName { get; set; } = "";

        /// <summary>Upstream end invert (higher elevation for gravity flow).</summary>
        public double UpstreamInvertFt { get; set; }

        /// <summary>Downstream end invert (lower elevation for gravity flow).</summary>
        public double DownstreamInvertFt { get; set; }

        /// <summary>Structure at the upstream end of flow.</summary>
        public ObjectId UpstreamStructureId { get; set; }

        /// <summary>Structure at the downstream end of flow.</summary>
        public ObjectId DownstreamStructureId { get; set; }

        /// <summary>Civil 3D pipe start point (plan XY + rim/center elevation Z).</summary>
        public Point3d StartPoint { get; set; }

        /// <summary>Civil 3D pipe end point (plan XY + rim/center elevation Z).</summary>
        public Point3d EndPoint { get; set; }
    }

    /// <summary>Per-network roll-up of pipe and structure counts from the drawing.</summary>
    public sealed class NetworkSummary
    {
        public string NetworkName { get; set; } = "";
        public int PipeCount { get; set; }
        public int StructureCount { get; set; }
        public double TotalLengthFt { get; set; }
        public double MinInvertFt { get; set; } = double.PositiveInfinity;
        public double MaxInvertFt { get; set; } = double.NegativeInfinity;
        public double MinDiameterFt { get; set; } = double.PositiveInfinity;
        public double MaxDiameterFt { get; set; } = double.NegativeInfinity;

        public bool HasPipes =>
            PipeCount > 0
            && !double.IsPositiveInfinity(MinInvertFt)
            && !double.IsNegativeInfinity(MaxInvertFt);
    }

    /// <summary>
    /// Reads circular gravity pipes out of the active drawing's Civil 3D pipe
    /// networks and maps them onto the engine's <see cref="PipeSegment"/> model.
    /// Diameters are assumed to be in feet (imperial drawing).
    /// </summary>
    public static class PipeNetworkReader
    {
        /// <summary>Default Manning's n; Civil 3D pipes don't carry roughness.</summary>
        public const double DefaultManningN = 0.013;

        public static List<ReadPipe> ReadAll(Database db, CivilDocument civilDoc)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (civilDoc == null) throw new ArgumentNullException(nameof(civilDoc));

            var pipes = new List<ReadPipe>();
            ObjectIdCollection networkIds = civilDoc.GetPipeNetworkIds();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId nid in networkIds)
                {
                    if (!(tr.GetObject(nid, OpenMode.ForRead) is Network net)) continue;
                    string netName = net.Name ?? "";
                    var structureNames = BuildStructureNameLookup(net, tr);

                    foreach (ObjectId pid in net.GetPipeIds())
                    {
                        if (!(tr.GetObject(pid, OpenMode.ForRead) is Pipe pipe)) continue;

                        double startInvert = InvertAt(pipe.StartPoint, pipe.InnerDiameterOrWidth);
                        double endInvert = InvertAt(pipe.EndPoint, pipe.InnerDiameterOrWidth);
                        bool startIsUpstream = pipe.FlowDirection == FlowDirectionType.StartToEnd
                            || (pipe.FlowDirection != FlowDirectionType.EndToStart
                                && startInvert >= endInvert);

                        double lengthFt = pipe.Length2D;
                        if (lengthFt <= 0)
                            lengthFt = pipe.Length3D;

                        pipes.Add(new ReadPipe
                        {
                            PipeId = pid,
                            NetworkName = netName,
                            PipeName = pipe.Name ?? "",
                            LengthFt = lengthFt,
                            StartInvertFt = startInvert,
                            EndInvertFt = endInvert,
                            StartStructureId = pipe.StartStructureId,
                            EndStructureId = pipe.EndStructureId,
                            StartStructureName = ResolveStructureName(pipe.StartStructureId, structureNames),
                            EndStructureName = ResolveStructureName(pipe.EndStructureId, structureNames),
                            UpstreamInvertFt = startIsUpstream ? startInvert : endInvert,
                            DownstreamInvertFt = startIsUpstream ? endInvert : startInvert,
                            UpstreamStructureId = startIsUpstream ? pipe.StartStructureId : pipe.EndStructureId,
                            DownstreamStructureId = startIsUpstream ? pipe.EndStructureId : pipe.StartStructureId,
                            StartPoint = pipe.StartPoint,
                            EndPoint = pipe.EndPoint,
                            Segment = new PipeSegment
                            {
                                Name = pipe.Name ?? "",
                                DiameterFt = pipe.InnerDiameterOrWidth,
                                Slope = Math.Abs(pipe.Slope),
                                ManningN = DefaultManningN,
                                LengthFt = lengthFt,
                                StartInvertFt = startInvert,
                                EndInvertFt = endInvert,
                            },
                        });
                    }
                }
                tr.Commit();
            }

            return pipes;
        }

        /// <summary>
        /// Summarizes every pipe network: pipe count, total length, invert and
        /// diameter ranges, and structure count.
        /// </summary>
        public static List<NetworkSummary> ReadNetworkSummaries(Database db, CivilDocument civilDoc)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (civilDoc == null) throw new ArgumentNullException(nameof(civilDoc));

            var summaries = new List<NetworkSummary>();
            ObjectIdCollection networkIds = civilDoc.GetPipeNetworkIds();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId nid in networkIds)
                {
                    if (!(tr.GetObject(nid, OpenMode.ForRead) is Network net)) continue;

                    var summary = new NetworkSummary
                    {
                        NetworkName = net.Name ?? "",
                        StructureCount = net.GetStructureIds().Count,
                    };

                    foreach (ObjectId pid in net.GetPipeIds())
                    {
                        if (!(tr.GetObject(pid, OpenMode.ForRead) is Pipe pipe)) continue;

                        double startInvert = InvertAt(pipe.StartPoint, pipe.InnerDiameterOrWidth);
                        double endInvert = InvertAt(pipe.EndPoint, pipe.InnerDiameterOrWidth);
                        double diameterFt = pipe.InnerDiameterOrWidth;

                        double lengthFt = pipe.Length2D;
                        if (lengthFt <= 0)
                            lengthFt = pipe.Length3D;

                        summary.PipeCount++;
                        summary.TotalLengthFt += lengthFt;
                        summary.MinInvertFt = Math.Min(summary.MinInvertFt, Math.Min(startInvert, endInvert));
                        summary.MaxInvertFt = Math.Max(summary.MaxInvertFt, Math.Max(startInvert, endInvert));
                        summary.MinDiameterFt = Math.Min(summary.MinDiameterFt, diameterFt);
                        summary.MaxDiameterFt = Math.Max(summary.MaxDiameterFt, diameterFt);
                    }

                    summaries.Add(summary);
                }

                tr.Commit();
            }

            return summaries;
        }

        /// <summary>
        /// Civil 3D 2026 exposes pipe-end elevations via <see cref="Pipe.StartPoint"/> /
        /// <see cref="Pipe.EndPoint"/> (Z); there is no StartInvert/EndInvert property.
        /// For circular pipes we subtract half the inner diameter to obtain invert.
        /// </summary>
        private static double InvertAt(Point3d endPoint, double innerDiameterFt)
        {
            double radius = innerDiameterFt > 0 ? innerDiameterFt / 2.0 : 0.0;
            return endPoint.Z - radius;
        }

        private static Dictionary<ObjectId, string> BuildStructureNameLookup(Network net, Transaction tr)
        {
            var names = new Dictionary<ObjectId, string>();
            foreach (ObjectId sid in net.GetStructureIds())
            {
                if (!(tr.GetObject(sid, OpenMode.ForRead) is Structure structure)) continue;
                names[sid] = structure.Name ?? "";
            }
            return names;
        }

        private static string ResolveStructureName(ObjectId structureId, Dictionary<ObjectId, string> names)
        {
            if (structureId.IsNull) return "";
            return names.TryGetValue(structureId, out string? name) ? name : "";
        }
    }
}