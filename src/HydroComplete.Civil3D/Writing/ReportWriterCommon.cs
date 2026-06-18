using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using HydroComplete.Engine;

namespace HydroComplete.Civil3D.Writing
{
    /// <summary>Shared paths, HTML/KaTeX formula rendering, and formatting for reports.</summary>
    internal static class ReportWriterCommon
    {
        private static readonly Dictionary<string, string> FormulaLatex = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["(1.486/n)*A*R^(2/3)*S^(1/2)"] = @"Q = \frac{1.486}{n} A R^{2/3} S^{1/2}",
            ["pi*D^2/4"] = @"A = \frac{\pi D^2}{4}",
            ["D/4"] = @"R = \frac{D}{4}",
            ["Q_full/A_full"] = @"V = \frac{Q_{\text{full}}}{A_{\text{full}}}",
            ["[n*Q/(1.486*A*R^(2/3))]^2"] = @"S_f = \left[\frac{n Q}{1.486\, A\, R^{2/3}}\right]^2",
            ["S_f*L"] = @"h_f = S_f L",
            ["K*Vh"] = @"h_m = K \cdot V_h",
            ["Q = C*i*A"] = @"Q = C \cdot i \cdot A",
            ["i = a/(t+b)^c"] = @"i = \frac{a}{(t+b)^c}",
            ["A = R*K*LS*C*P"] = @"A = R \cdot K \cdot LS \cdot C \cdot P",
            ["Q = (P-Ia)^2/(P-Ia+S)"] = @"Q = \frac{(P-I_a)^2}{P - I_a + S}",
            ["S = 1000/CN - 10"] = @"S = \frac{1000}{CN} - 10",
            ["Ia = 0.2*S"] = @"I_a = 0.2 S",
            ["t_c = sum(segment Tc)"] = @"t_c = \sum t_{c,\text{segment}}",
            ["Modified Puls"] = @"\text{Modified Puls routing}",
            ["TR-20 lag"] = @"t_{\text{lag}} = 0.6\, t_c",
        };

        public static string OutputFolder =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "HydroComplete");

        public static string BuildReportPath(string drawingName, string extension)
        {
            Directory.CreateDirectory(OutputFolder);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string safeDrawing = SanitizeFileName(drawingName);
            return Path.Combine(OutputFolder, $"report-{safeDrawing}-{stamp}.{extension}");
        }

        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "drawing";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim();
        }

        public static string EscapeHtml(string s)
        {
            return s
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }

        public static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max - 1) + "~";
        }

        public static void AppendHtmlHead(StringBuilder sb, string pageTitle)
        {
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\"/>");
            sb.AppendLine("<title>" + EscapeHtml(pageTitle) + "</title>");
            sb.AppendLine("<link rel=\"stylesheet\" href=\"https://cdn.jsdelivr.net/npm/katex@0.16.8/dist/katex.min.css\">");
            sb.AppendLine("<script src=\"https://cdn.jsdelivr.net/npm/katex@0.16.8/dist/katex.min.js\"></script>");
            sb.AppendLine("<script src=\"https://cdn.jsdelivr.net/npm/katex@0.16.8/dist/contrib/auto-render.min.js\"></script>");
            AppendReportCss(sb);
            sb.AppendLine("</head><body>");
        }

        public static void AppendHtmlFoot(StringBuilder sb)
        {
            sb.AppendLine(KatexRehydrationScript);
            sb.AppendLine("</body></html>");
        }

        public static void AppendCalcSteps(StringBuilder sb, IEnumerable<CalcStep> steps, string? heading = null)
        {
            var list = new List<CalcStep>(steps);
            if (list.Count == 0) return;

            if (!string.IsNullOrWhiteSpace(heading))
                sb.AppendLine("<h3>" + EscapeHtml(heading!) + "</h3>");

            sb.AppendLine("<div class=\"hc-formula-panel\">");
            foreach (CalcStep step in list)
                sb.AppendLine(RenderCalcStepHtml(step));
            sb.AppendLine("</div>");
        }

        public static string RenderCalcStepHtml(CalcStep step)
        {
            string? latex = TryMapFormulaToLatex(step.Formula);
            string resultLatex = FormatResultLatex(step);
            var block = new StringBuilder();
            block.AppendLine("<div class=\"hc-formula-step\" data-label=\"" + EscapeHtml(step.Label) + "\">");
            block.AppendLine("<div class=\"hc-formula-title\">" + EscapeHtml(step.Label) + "</div>");

            if (latex != null)
            {
                block.AppendLine("<div class=\"hc-formula-equation\"><code class=\"hc-tex-fallback\">" +
                                 EscapeHtml(latex) + "</code></div>");
                block.AppendLine("<div class=\"hc-formula-result\"><code class=\"hc-tex-fallback\">" +
                                 EscapeHtml(resultLatex) + "</code></div>");
            }
            else
            {
                block.AppendLine("<div class=\"hc-formula-desc\">" + EscapeHtml(step.ToString()) + "</div>");
            }

            if (!string.IsNullOrWhiteSpace(step.Formula) && latex == null)
                block.AppendLine("<div class=\"hc-formula-desc\">" + EscapeHtml(step.Formula) + "</div>");

            block.AppendLine("</div>");
            return block.ToString();
        }

        public static string? TryMapFormulaToLatex(string formula)
        {
            if (string.IsNullOrWhiteSpace(formula)) return null;
            if (FormulaLatex.TryGetValue(formula.Trim(), out string? latex))
                return latex;
            return null;
        }

        private static string FormatResultLatex(CalcStep step)
        {
            string units = string.IsNullOrWhiteSpace(step.Units) ? "" : $"\\ \\text{{{step.Units}}}";
            return $"{EscapeLatexIdentifier(step.Label)} = {step.Value.ToString("0.####", CultureInfo.InvariantCulture)}{units}";
        }

        private static string EscapeLatexIdentifier(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return "x";
            return label.Replace("_", @"\_");
        }

        private static void AppendReportCss(StringBuilder sb)
        {
            sb.AppendLine("<style>");
            sb.AppendLine("body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#1a1a1a;}");
            sb.AppendLine("h1{font-size:1.4rem;} h2{font-size:1.15rem;margin-top:28px;} h3{font-size:1rem;margin-top:16px;}");
            sb.AppendLine("table{border-collapse:collapse;width:100%;margin:16px 0;}");
            sb.AppendLine("th,td{border:1px solid #ccc;padding:6px 8px;text-align:left;font-size:0.9rem;}");
            sb.AppendLine("th{background:#f0f4f8;} tr.surcharged{background:#ffe6e6;}");
            sb.AppendLine(".disclaimer{margin-top:24px;padding:12px;background:#fff8e6;border:1px solid #e6c200;}");
            sb.AppendLine(".hc-formula-panel{margin:12px 0;}");
            sb.AppendLine(".hc-formula-step{border:1px solid #e0e6ed;border-radius:6px;padding:10px 12px;margin:8px 0;background:#fafbfc;}");
            sb.AppendLine(".hc-formula-title{font-weight:600;font-size:0.95rem;margin-bottom:6px;}");
            sb.AppendLine(".hc-formula-equation,.hc-formula-result{margin:4px 0;}");
            sb.AppendLine(".hc-formula-desc{font-family:Consolas,monospace;font-size:0.85rem;color:#444;}");
            sb.AppendLine(".hc-tex-fallback{font-family:Consolas,monospace;font-size:0.9rem;}");
            sb.AppendLine(".pass{color:#0a7a2f;font-weight:600;} .failtxt{color:#b00020;font-weight:600;}");
            sb.AppendLine("</style>");
        }

        private const string KatexRehydrationScript = @"<script>
(function rehydrateKaTeX() {
  if (typeof katex === 'undefined') return setTimeout(rehydrateKaTeX, 50);
  document.querySelectorAll('code.hc-tex-fallback').forEach(function(el) {
    var latex = el.textContent;
    try {
      var span = document.createElement('span');
      katex.render(latex, span, {
        displayMode: el.closest('.hc-formula-equation, .hc-formula-result') !== null,
        throwOnError: false,
        strict: false
      });
      el.replaceWith(span);
    } catch (e) {}
  });
  if (typeof renderMathInElement !== 'undefined') {
    renderMathInElement(document.body, {
      delimiters: [
        { left: '$$', right: '$$', display: true },
        { left: '\\(', right: '\\)', display: false }
      ],
      throwOnError: false
    });
  }
})();
</script>";
    }
}