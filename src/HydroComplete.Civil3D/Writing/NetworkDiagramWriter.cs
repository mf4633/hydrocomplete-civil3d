using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.Geometry;
using HydroComplete.Civil3D.Reading;
using HydroComplete.Engine;

namespace HydroComplete.Civil3D.Writing
{
    /// <summary>
    /// Exports an HTML/SVG pipe-network schematic from Civil 3D plan topology
    /// (Civil 3D equivalent of the hc-refactored GoJS conveyance diagram).
    /// </summary>
    internal static class NetworkDiagramWriter
    {
        public sealed class PipeDiagramStats
        {
            public double DesignFlowCfs { get; set; }
            public double FlowRatio { get; set; }
            public bool Surcharged { get; set; }
        }

        public static string Write(
            string drawingName,
            string networkName,
            IReadOnlyList<ReadPipe> pipes,
            IReadOnlyDictionary<string, string> structureNames,
            IReadOnlyDictionary<string, PipeDiagramStats>? pipeStats = null)
        {
            string path = ReportWriterCommon.BuildReportPath(drawingName, "network-diagram.html");
            var networkPipes = pipes
                .Where(p => string.Equals(p.NetworkName, networkName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var sb = new StringBuilder();
            ReportWriterCommon.AppendHtmlHead(sb, "HydroComplete Network Diagram");
            sb.AppendLine("<h1>HydroComplete — Pipe Network Diagram</h1>");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "<p>Drawing: <strong>{0}</strong><br/>Network: <strong>{1}</strong><br/>Generated: {2}</p>",
                ReportWriterCommon.EscapeHtml(drawingName),
                ReportWriterCommon.EscapeHtml(networkName),
                ReportWriterCommon.EscapeHtml(DateTime.Now.ToString("f", CultureInfo.CurrentCulture))));
            sb.AppendLine("<p>Plan-view schematic from Civil 3D pipe topology. ");
            sb.AppendLine("Red pipes = surcharged; amber = Q/Q<sub>full</sub> &gt; 0.85.</p>");

            sb.AppendLine(BuildLegend());
            sb.AppendLine(BuildSvg(networkPipes, structureNames, pipeStats));

            sb.AppendLine("<div class=\"disclaimer\">");
            sb.AppendLine("<strong>Note:</strong> Schematic uses drawing plan coordinates when available. ");
            sb.AppendLine("This is not a GoJS treatment-train diagram — it reflects actual pipe network geometry.");
            sb.AppendLine("</div>");
            ReportWriterCommon.AppendHtmlFoot(sb);

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            return path;
        }

        private static string BuildLegend()
        {
            return @"<div style=""margin:12px 0;font-size:0.9rem"">
<span style=""display:inline-block;width:14px;height:14px;background:#4caf50;border-radius:50%;margin-right:4px""></span> Headwater
<span style=""display:inline-block;width:14px;height:14px;background:#f48fb1;transform:rotate(45deg);margin:0 4px 0 16px""></span> Junction
<span style=""display:inline-block;width:0;height:0;border-left:8px solid transparent;border-right:8px solid transparent;border-top:14px solid #ef9a9a;margin:0 4px 0 16px""></span> Outfall
<span style=""display:inline-block;width:24px;height:4px;background:#555;margin:0 4px 0 16px""></span> Pipe
<span style=""display:inline-block;width:24px;height:4px;background:#c62828;margin:0 4px 0 16px""></span> Surcharged
</div>";
        }

        private static string BuildSvg(
            IReadOnlyList<ReadPipe> pipes,
            IReadOnlyDictionary<string, string> structureNames,
            IReadOnlyDictionary<string, PipeDiagramStats>? pipeStats)
        {
            if (pipes.Count == 0)
                return "<p><em>No pipes in this network.</em></p>";

            var nodes = new Dictionary<string, NodeLayout>(StringComparer.OrdinalIgnoreCase);
            var inflowCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var outflowCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (ReadPipe pipe in pipes)
            {
                string us = pipe.UpstreamStructureId.Handle.ToString();
                string ds = pipe.DownstreamStructureId.Handle.ToString();
                Point3d usPt = GetUpstreamPoint(pipe);
                Point3d dsPt = GetDownstreamPoint(pipe);

                EnsureNode(nodes, us, usPt, structureNames);
                EnsureNode(nodes, ds, dsPt, structureNames);
                TouchBounds(usPt, ref minX, ref minY, ref maxX, ref maxY);
                TouchBounds(dsPt, ref minX, ref minY, ref maxX, ref maxY);

                outflowCount[us] = outflowCount.TryGetValue(us, out int ou) ? ou + 1 : 1;
                inflowCount[ds] = inflowCount.TryGetValue(ds, out int id) ? id + 1 : 1;
            }

            const double pad = 40;
            const double width = 900;
            const double height = 600;
            double spanX = Math.Max(maxX - minX, 1);
            double spanY = Math.Max(maxY - minY, 1);
            double scale = Math.Min((width - 2 * pad) / spanX, (height - 2 * pad) / spanY);

            double MapX(double x) => pad + (x - minX) * scale;
            double MapY(double y) => height - pad - (y - minY) * scale;

            var svg = new StringBuilder();
            svg.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {0} {1}\" style=\"max-width:100%;border:1px solid #ddd;background:#fff\">",
                width, height));

            foreach (ReadPipe pipe in pipes)
            {
                string key = pipe.PipeId.Handle.ToString();
                Point3d p1 = GetUpstreamPoint(pipe);
                Point3d p2 = GetDownstreamPoint(pipe);
                double x1 = MapX(p1.X);
                double y1 = MapY(p1.Y);
                double x2 = MapX(p2.X);
                double y2 = MapY(p2.Y);

                string stroke = "#555";
                double strokeWidth = 3;
                if (pipeStats != null && pipeStats.TryGetValue(key, out PipeDiagramStats? stats))
                {
                    if (stats.Surcharged) stroke = "#c62828";
                    else if (stats.FlowRatio > 0.85) stroke = "#f9a825";
                }

                svg.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "<line x1=\"{0:0.##}\" y1=\"{1:0.##}\" x2=\"{2:0.##}\" y2=\"{3:0.##}\" stroke=\"{4}\" stroke-width=\"{5}\"/>",
                    x1, y1, x2, y2, stroke, strokeWidth));

                double mx = (x1 + x2) / 2;
                double my = (y1 + y2) / 2;
                string label = pipe.PipeName;
                if (pipe.Segment.DiameterFt > 0)
                    label += string.Format(CultureInfo.InvariantCulture, " Ø{0:0.#}\"", pipe.Segment.DiameterFt * 12.0);
                if (pipeStats != null && pipeStats.TryGetValue(key, out PipeDiagramStats? ps))
                    label += string.Format(CultureInfo.InvariantCulture, " Q={0:0.#}", ps.DesignFlowCfs);

                svg.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "<text x=\"{0:0.##}\" y=\"{1:0.##}\" font-size=\"10\" fill=\"#333\" text-anchor=\"middle\">{2}</text>",
                    mx, my - 4, ReportWriterCommon.EscapeHtml(label)));
            }

            foreach (KeyValuePair<string, NodeLayout> pair in nodes)
            {
                string id = pair.Key;
                NodeLayout node = pair.Value;
                double cx = MapX(node.X);
                double cy = MapY(node.Y);
                int inflows = inflowCount.TryGetValue(id, out int ic) ? ic : 0;
                int outflows = outflowCount.TryGetValue(id, out int oc) ? oc : 0;
                bool isOutfall = outflows == 0 && inflows > 0;
                bool isHeadwater = inflows == 0 && outflows > 0;
                bool isJunction = inflows > 1;

                if (isOutfall)
                {
                    svg.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "<polygon points=\"{0:0.##},{1:0.##} {2:0.##},{3:0.##} {4:0.##},{5:0.##}\" fill=\"#ef9a9a\" stroke=\"#b71c1c\"/>",
                        cx, cy + 10, cx - 8, cy - 6, cx + 8, cy - 6));
                }
                else if (isJunction)
                {
                    svg.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "<rect x=\"{0:0.##}\" y=\"{1:0.##}\" width=\"14\" height=\"14\" fill=\"#f48fb1\" stroke=\"#ad1457\" transform=\"rotate(45 {2:0.##} {3:0.##})\"/>",
                        cx - 7, cy - 7, cx, cy));
                }
                else
                {
                    string fill = isHeadwater ? "#4caf50" : "#90caf9";
                    string stroke = isHeadwater ? "#2e7d32" : "#1565c0";
                    svg.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "<circle cx=\"{0:0.##}\" cy=\"{1:0.##}\" r=\"8\" fill=\"{2}\" stroke=\"{3}\"/>",
                        cx, cy, fill, stroke));
                }

                svg.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "<text x=\"{0:0.##}\" y=\"{1:0.##}\" font-size=\"9\" fill=\"#111\" text-anchor=\"middle\">{2}</text>",
                    cx, cy + 18, ReportWriterCommon.EscapeHtml(ReportWriterCommon.Trim(node.Label, 18))));
            }

            svg.AppendLine("</svg>");
            return svg.ToString();
        }

        private static Point3d GetUpstreamPoint(ReadPipe pipe)
        {
            return pipe.UpstreamStructureId == pipe.StartStructureId
                ? pipe.StartPoint
                : pipe.EndPoint;
        }

        private static Point3d GetDownstreamPoint(ReadPipe pipe)
        {
            return pipe.DownstreamStructureId == pipe.EndStructureId
                ? pipe.EndPoint
                : pipe.StartPoint;
        }

        private static void EnsureNode(
            Dictionary<string, NodeLayout> nodes,
            string id,
            Point3d pt,
            IReadOnlyDictionary<string, string> structureNames)
        {
            if (!nodes.ContainsKey(id))
            {
                structureNames.TryGetValue(id, out string? name);
                nodes[id] = new NodeLayout
                {
                    X = pt.X,
                    Y = pt.Y,
                    Label = string.IsNullOrWhiteSpace(name) ? id.Substring(Math.Max(0, id.Length - 6)) : name,
                };
            }
        }

        private static void TouchBounds(Point3d pt, ref double minX, ref double minY, ref double maxX, ref double maxY)
        {
            minX = Math.Min(minX, pt.X);
            minY = Math.Min(minY, pt.Y);
            maxX = Math.Max(maxX, pt.X);
            maxY = Math.Max(maxY, pt.Y);
        }

        private sealed class NodeLayout
        {
            public double X { get; set; }
            public double Y { get; set; }
            public string Label { get; set; } = "";
        }
    }
}