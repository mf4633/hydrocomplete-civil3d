using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HydroComplete.Engine;

namespace HydroComplete.Civil3D.Writing
{
    /// <summary>
    /// Generates an HTML/SVG treatment-train node diagram from a WaterQualityEngine result.
    /// Mirrors the GoJS treatment-train diagram on hydrocomplete.com using static SVG.
    /// </summary>
    internal static class TreatmentTrainDiagramWriter
    {
        private static readonly string[] DisplayPollutants = { Pollutant.Tss, Pollutant.Tn, Pollutant.Tp };

        private static readonly Dictionary<string, string> BmpColors =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [BmpType.Bioretention]       = "#4caf50",
                [BmpType.WetPond]            = "#2196f3",
                ["constructed-wetland"]       = "#009688",
                [BmpType.VegetatedSwale]      = "#8bc34a",
                [BmpType.SandFilter]          = "#ff9800",
                ["infiltration-trench"]        = "#795548",
                ["permeable-pavement"]         = "#607d8b",
                ["green-roof"]                = "#66bb6a",
                ["cistern"]                   = "#0288d1",
                ["level-spreader-filter"]      = "#7cb342",
            };

        public static string Write(
            string drawingName,
            WaterQualityEngine.TreatmentTrainResult train,
            string landUse,
            double drainageAreaAcres,
            double runoffDepthIn)
        {
            string path = ReportWriterCommon.BuildReportPath(drawingName, "train-diagram.html");

            var sb = new StringBuilder();
            ReportWriterCommon.AppendHtmlHead(sb, "HydroComplete — Treatment Train Diagram");
            sb.AppendLine("<h1>HydroComplete — BMP Treatment Train</h1>");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "<p>Drawing: <strong>{0}</strong><br/>Land use: <strong>{1}</strong> — " +
                "{2:0.000} ac, runoff depth {3:0.###} in<br/>Generated: {4}</p>",
                ReportWriterCommon.EscapeHtml(drawingName),
                ReportWriterCommon.EscapeHtml(landUse),
                drainageAreaAcres, runoffDepthIn,
                ReportWriterCommon.EscapeHtml(DateTime.Now.ToString("f", CultureInfo.CurrentCulture))));

            sb.AppendLine(BuildSvg(train));
            sb.AppendLine(BuildTable(train));

            sb.AppendLine("<div class=\"disclaimer\">");
            sb.AppendLine("<strong>Note:</strong> Removal efficiencies from BmpLibrary median values. ");
            sb.AppendLine("Series efficiency = 1 &minus; &prod;(1 &minus; &eta;<sub>i</sub>). ");
            sb.AppendLine("Verify with site-specific data and jurisdiction BMP manual.");
            sb.AppendLine("</div>");

            ReportWriterCommon.AppendHtmlFoot(sb);
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            return path;
        }

        private static string BuildSvg(WaterQualityEngine.TreatmentTrainResult train)
        {
            int n = train.BmpSteps.Count;
            const double nodeW = 140;
            const double nodeH = 80;
            const double gapX = 90;
            const double padY = 40;
            const double padX = 30;
            const double arrowHeadLen = 10;
            const double labelOffset = 14;

            // Source node + n BMP nodes + outfall node
            int totalNodes = n + 2;
            double svgW = padX * 2 + totalNodes * nodeW + (totalNodes - 1) * gapX;
            double svgH = padY * 2 + nodeH + 70; // extra for load labels below

            double cy = padY + nodeH / 2;

            double NodeX(int idx) => padX + idx * (nodeW + gapX);

            var svg = new StringBuilder();
            svg.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {0:0.#} {1:0.#}\" " +
                "style=\"max-width:100%;border:1px solid #ddd;background:#fff;font-family:Segoe UI,Arial,sans-serif\">",
                svgW, svgH));

            // ── arrows and load labels ──────────────────────────────────────
            for (int i = 0; i < totalNodes - 1; i++)
            {
                double x1 = NodeX(i) + nodeW;
                double x2 = NodeX(i + 1);
                double mx = (x1 + x2) / 2;
                double ay = cy;

                // Arrow shaft
                svg.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "<line x1=\"{0:0.#}\" y1=\"{1:0.#}\" x2=\"{2:0.#}\" y2=\"{3:0.#}\" " +
                    "stroke=\"#555\" stroke-width=\"2\"/>",
                    x1, ay, x2 - arrowHeadLen, ay));

                // Arrowhead
                svg.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "<polygon points=\"{0:0.#},{1:0.#} {2:0.#},{3:0.#} {2:0.#},{4:0.#}\" fill=\"#555\"/>",
                    x2, ay, x2 - arrowHeadLen, ay - 5, ay + 5));

                // Load labels on the arrow: show remaining lbs at that point
                Dictionary<string, double> loadsAtArrow = i == 0
                    ? train.InitialLoadsLbs
                    : train.BmpSteps[i - 1].EffluentLbs;

                int labelLine = 0;
                foreach (string p in DisplayPollutants)
                {
                    if (!loadsAtArrow.TryGetValue(p, out double lbs)) continue;
                    double textY = ay - 10 - labelLine * labelOffset;
                    svg.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "<text x=\"{0:0.#}\" y=\"{1:0.#}\" font-size=\"9\" fill=\"#444\" text-anchor=\"middle\">" +
                        "{2}: {3:0.##} lbs</text>",
                        mx, textY, p.ToUpperInvariant(), lbs));
                    labelLine++;
                }
            }

            // ── source node ─────────────────────────────────────────────────
            DrawRoundRect(svg, NodeX(0), cy - nodeH / 2, nodeW, nodeH, "#1565c0", "#e3f2fd",
                "Site Runoff", null);

            // ── BMP nodes ───────────────────────────────────────────────────
            for (int i = 0; i < n; i++)
            {
                WaterQualityEngine.TreatmentTrainBmpStep step = train.BmpSteps[i];
                BmpDefinition bmpDef = BmpLibrary.GetBmp(step.BmpType);
                string color = BmpColors.TryGetValue(step.BmpType, out string? c) ? c : "#78909c";

                // Per-pollutant removal for this BMP
                string etaLabel = string.Join("  ",
                    DisplayPollutants.Select(p =>
                    {
                        double inf = step.InfluentLbs.TryGetValue(p, out double iv) ? iv : 0;
                        double rem = step.RemovedLbs.TryGetValue(p, out double rv) ? rv : 0;
                        double eta = inf > 0 ? rem / inf * 100 : 0;
                        return string.Format(CultureInfo.InvariantCulture, "{0}:{1:0.#}%", p.ToUpperInvariant(), eta);
                    }));

                DrawRoundRect(svg, NodeX(i + 1), cy - nodeH / 2, nodeW, nodeH, color, LightenColor(color),
                    ReportWriterCommon.Trim(bmpDef.Name, 18), etaLabel);
            }

            // ── outfall node ─────────────────────────────────────────────────
            double ox = NodeX(totalNodes - 1);
            double oy = cy;
            svg.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "<polygon points=\"{0:0.#},{1:0.#} {2:0.#},{3:0.#} {4:0.#},{5:0.#}\" " +
                "fill=\"#ef9a9a\" stroke=\"#b71c1c\"/>",
                ox + nodeW / 2, oy + nodeH / 2,
                ox, oy - nodeH / 2,
                ox + nodeW, oy - nodeH / 2));
            svg.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "<text x=\"{0:0.#}\" y=\"{1:0.#}\" font-size=\"11\" fill=\"#111\" " +
                "text-anchor=\"middle\" font-weight=\"600\">Outfall</text>",
                ox + nodeW / 2, oy + 5));

            // ── overall removal legend ───────────────────────────────────────
            double legY = cy + nodeH / 2 + 20;
            svg.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "<text x=\"{0:0.#}\" y=\"{1:0.#}\" font-size=\"11\" fill=\"#333\" text-anchor=\"middle\">",
                svgW / 2, legY));
            svg.Append("Overall removal: ");
            svg.Append(string.Join("   ", DisplayPollutants.Select(p =>
            {
                train.OverallRemovalEfficiency.TryGetValue(p, out double eta);
                return string.Format(CultureInfo.InvariantCulture,
                    "{0} {1:0.#}%", p.ToUpperInvariant(), eta * 100);
            })));
            svg.AppendLine("</text>");

            svg.AppendLine("</svg>");
            return svg.ToString();
        }

        private static void DrawRoundRect(
            StringBuilder svg,
            double x, double y, double w, double h,
            string stroke, string fill,
            string title, string? subtitle)
        {
            svg.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "<rect x=\"{0:0.#}\" y=\"{1:0.#}\" width=\"{2:0.#}\" height=\"{3:0.#}\" " +
                "rx=\"8\" fill=\"{4}\" stroke=\"{5}\" stroke-width=\"2\"/>",
                x, y, w, h, fill, stroke));

            double textY = subtitle == null
                ? y + h / 2 + 4
                : y + h / 2 - 4;

            svg.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "<text x=\"{0:0.#}\" y=\"{1:0.#}\" font-size=\"11\" fill=\"#111\" " +
                "text-anchor=\"middle\" font-weight=\"600\">{2}</text>",
                x + w / 2, textY, ReportWriterCommon.EscapeHtml(title)));

            if (subtitle != null)
            {
                svg.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "<text x=\"{0:0.#}\" y=\"{1:0.#}\" font-size=\"9\" fill=\"#444\" " +
                    "text-anchor=\"middle\">{2}</text>",
                    x + w / 2, textY + 14, ReportWriterCommon.EscapeHtml(subtitle)));
            }
        }

        private static string BuildTable(WaterQualityEngine.TreatmentTrainResult train)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<h2>Per-BMP Pollutant Balance</h2>");
            sb.AppendLine("<table><thead><tr>");
            sb.AppendLine("<th>BMP</th>");
            foreach (string p in DisplayPollutants)
                sb.AppendLine($"<th>{p.ToUpperInvariant()} In (lbs)</th><th>Removed</th><th>η (%)</th>");
            sb.AppendLine("</tr></thead><tbody>");

            foreach (WaterQualityEngine.TreatmentTrainBmpStep step in train.BmpSteps)
            {
                BmpDefinition def = BmpLibrary.GetBmp(step.BmpType);
                sb.Append("<tr>");
                sb.Append($"<td>{ReportWriterCommon.EscapeHtml(def.Name)}</td>");
                foreach (string p in DisplayPollutants)
                {
                    step.InfluentLbs.TryGetValue(p, out double inf);
                    step.RemovedLbs.TryGetValue(p, out double rem);
                    double eta = inf > 0 ? rem / inf * 100 : 0;
                    sb.Append(string.Format(CultureInfo.InvariantCulture,
                        "<td>{0:0.###}</td><td>{1:0.###}</td><td>{2:0.1}</td>", inf, rem, eta));
                }
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</tbody></table>");
            return sb.ToString();
        }

        private static string LightenColor(string hex)
        {
            if (hex.Length != 7 || hex[0] != '#') return "#f5f5f5";
            try
            {
                int r = Convert.ToInt32(hex.Substring(1, 2), 16);
                int g = Convert.ToInt32(hex.Substring(3, 2), 16);
                int b = Convert.ToInt32(hex.Substring(5, 2), 16);
                r = Math.Min(255, r + (255 - r) * 3 / 4);
                g = Math.Min(255, g + (255 - g) * 3 / 4);
                b = Math.Min(255, b + (255 - b) * 3 / 4);
                return $"#{r:X2}{g:X2}{b:X2}";
            }
            catch { return "#f5f5f5"; }
        }
    }
}
