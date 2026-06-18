using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using HydroComplete.Civil3D.Reading;
using HydroComplete.Engine;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace HydroComplete.Civil3D.Writing
{
    /// <summary>
    /// Formula-transparent PDF report for Manning capacity and steady HGL (PDFsharp, MIT).
    /// </summary>
    internal static class PdfReportWriter
    {
        private const double MarginLeft = 48;
        private const double MarginTop = 48;
        private const double MarginBottom = 48;
        private const double ContentWidth = 516;

        public static string Write(
            string drawingName,
            IReadOnlyList<ReadPipe> pipes,
            IReadOnlyDictionary<ObjectId, Manning.CapacityResult> capacities,
            HglReportData? hglData = null,
            CapacityReportData? capacityData = null)
        {
            string path = ReportWriterCommon.BuildReportPath(drawingName, "pdf");
            string generated = DateTime.Now.ToString("f", CultureInfo.CurrentCulture);

            var document = new PdfDocument();
            document.Info.Title = "HydroComplete Hydraulic Report";
            document.Info.Author = "HydroComplete";
            document.Info.Subject = drawingName;

            var page = AddPage(document);
            var gfx = XGraphics.FromPdfPage(page);
            double y = MarginTop;

            y = DrawTitle(gfx, ref page, ref gfx, document, y, "HydroComplete — Hydraulic Report");
            y = DrawBodyText(gfx, ref page, ref gfx, document, y, $"Drawing: {drawingName}");
            y = DrawBodyText(gfx, ref page, ref gfx, document, y, $"Generated: {generated}");
            y += 8;

            y = DrawSectionHeading(gfx, ref page, ref gfx, document, y, "Manning Pipe Capacity");
            y = DrawBodyText(gfx, ref page, ref gfx, document, y,
                "Method: Manning full-barrel capacity for circular pipes (US customary, n=0.013 default).");
            y += 4;
            y = DrawPipeTable(gfx, ref page, ref gfx, document, y, pipes, capacities);

            y = DrawSectionHeading(gfx, ref page, ref gfx, document, y, "Manning calculation steps");
            foreach (ReadPipe rp in pipes)
            {
                if (!capacities.TryGetValue(rp.PipeId, out Manning.CapacityResult? cap))
                    continue;

                string pipeLabel = ReportWriterCommon.Trim(rp.NetworkName + "/" + rp.PipeName, 64);
                y = EnsureSpace(document, ref page, ref gfx, y, 20);
                y = DrawSubheading(gfx, ref page, ref gfx, document, y, pipeLabel);

                foreach (CalcStep step in cap.Steps)
                {
                    y = EnsureSpace(document, ref page, ref gfx, y, 14);
                    y = DrawMonoText(gfx, ref page, ref gfx, document, y, step.ToString());
                }
                y += 4;
            }

            if (capacityData != null && capacityData.Rows.Count > 0)
                y = AppendCapacitySection(gfx, ref page, ref gfx, document, y, capacityData);

            if (hglData != null && hglData.Networks.Count > 0)
                y = AppendHglSection(gfx, ref page, ref gfx, document, y, hglData);

            y = EnsureSpace(document, ref page, ref gfx, y, 72);
            DrawDisclaimer(gfx, ref page, ref gfx, document, y);

            document.Save(path);
            return path;
        }

        private static double AppendCapacitySection(
            XGraphics gfx,
            ref PdfPage page,
            ref XGraphics activeGfx,
            PdfDocument document,
            double y,
            CapacityReportData capacityData)
        {
            y += 8;
            y = DrawSectionHeading(activeGfx, ref page, ref activeGfx, document, y, "Design Capacity Check");
            y = DrawBodyText(activeGfx, ref page, ref activeGfx, document, y,
                string.Format(CultureInfo.InvariantCulture,
                    "Method: Manning normal depth at uniform design Q = {0:0.00} cfs. " +
                    "Surcharge when Q exceeds peak open-channel capacity.",
                    capacityData.DesignFlowCfs));

            string[] headers = { "Network / Pipe", "Q_full", "Q_des", "Q_des/Q", "d/D", "SURCH" };
            double[] colWidths = { 180, 58, 58, 58, 50, 42 };
            double rowHeight = 18;
            y = DrawTableHeader(activeGfx, ref page, ref activeGfx, document, y, headers, colWidths, rowHeight);

            foreach (CapacityPipeRow row in capacityData.Rows)
            {
                ReadPipe rp = row.Pipe;
                string[] cells =
                {
                    ReportWriterCommon.Trim(rp.NetworkName + "/" + rp.PipeName, 36),
                    row.QFullCfs.ToString("0.0", CultureInfo.InvariantCulture),
                    row.DesignFlowCfs.ToString("0.0", CultureInfo.InvariantCulture),
                    row.FlowRatio.ToString("0.00", CultureInfo.InvariantCulture),
                    row.RelativeDepth.ToString("0.00", CultureInfo.InvariantCulture),
                    row.Surcharged ? "*" : "",
                };
                y = EnsureSpace(document, ref page, ref activeGfx, y, rowHeight + 4);
                y = DrawTableRow(activeGfx, y, cells, colWidths, rowHeight, row.Surcharged);
            }

            return y;
        }

        private static double AppendHglSection(
            XGraphics gfx,
            ref PdfPage page,
            ref XGraphics activeGfx,
            PdfDocument document,
            double y,
            HglReportData hglData)
        {
            y += 8;
            y = DrawSectionHeading(activeGfx, ref page, ref activeGfx, document, y, "Steady HGL Profile");

            string lossNote = hglData.IncludeMinorLosses ? " with HEC-22 junction/exit losses" : "";
            y = DrawBodyText(activeGfx, ref page, ref activeGfx, document, y,
                string.Format(CultureInfo.InvariantCulture,
                    "Method: steady uniform-flow stepping downstream from headwater HGL using Manning normal depth per reach{0}. Design Q = {1:0.00} cfs.",
                    lossNote, hglData.DesignFlowCfs));

            foreach (HglNetworkReport net in hglData.Networks)
            {
                if (net.Rows.Count == 0) continue;

                y = EnsureSpace(document, ref page, ref activeGfx, y, 24);
                y = DrawSubheading(activeGfx, ref page, ref activeGfx, document, y, net.NetworkName);
                y = DrawBodyText(activeGfx, ref page, ref activeGfx, document, y,
                    string.Format(CultureInfo.InvariantCulture,
                        "Outfall tailwater HGL = {0:0.00} ft (profile stepped upstream, friction + HEC-22 minor losses).",
                        net.StartHglFt));

                string[] headers = { "Pipe", "d/D", "hf (ft)", "hm (ft)", "HGL_US (ft)", "HGL_DS (ft)", "SURCH" };
                double[] colWidths = { 130, 42, 52, 52, 76, 76, 38 };
                double rowHeight = 18;
                y = DrawTableHeader(activeGfx, ref page, ref activeGfx, document, y, headers, colWidths, rowHeight);

                foreach (HglPipeReportRow row in net.Rows)
                {
                    string dOverD = row.FlowSurcharged
                        ? "SURCH"
                        : row.RelativeDepth.ToString("0.00", CultureInfo.InvariantCulture);

                    string[] cells =
                    {
                        ReportWriterCommon.Trim(row.PipeName, 28),
                        dOverD,
                        row.Point.HfFt.ToString("0.00", CultureInfo.InvariantCulture),
                        row.Point.HmFt.ToString("0.00", CultureInfo.InvariantCulture),
                        row.HglUsFt.ToString("0.00", CultureInfo.InvariantCulture),
                        row.HglDsFt.ToString("0.00", CultureInfo.InvariantCulture),
                        row.IsSurcharged ? "*" : "",
                    };
                    y = EnsureSpace(document, ref page, ref activeGfx, y, rowHeight + 4);
                    y = DrawTableRow(activeGfx, y, cells, colWidths, rowHeight, row.IsSurcharged);
                }

                y += 8;
                y = DrawSubheading(activeGfx, ref page, ref activeGfx, document, y, "HGL calculation steps");
                foreach (HglPipeReportRow row in net.Rows)
                {
                    y = EnsureSpace(document, ref page, ref activeGfx, y, 18);
                    y = DrawMonoText(activeGfx, ref page, ref activeGfx, document, y,
                        ReportWriterCommon.Trim(row.PipeName, 64) + ":");
                    foreach (CalcStep step in row.Point.Steps)
                    {
                        y = EnsureSpace(document, ref page, ref activeGfx, y, 14);
                        y = DrawMonoText(activeGfx, ref page, ref activeGfx, document, y, step.ToString());
                    }
                    y += 4;
                }
            }

            return y;
        }

        private static double DrawPipeTable(
            XGraphics gfx,
            ref PdfPage page,
            ref XGraphics activeGfx,
            PdfDocument document,
            double y,
            IReadOnlyList<ReadPipe> pipes,
            IReadOnlyDictionary<ObjectId, Manning.CapacityResult> capacities)
        {
            string[] headers = { "Network / Pipe", "Dia (ft)", "Slope", "Q_full (cfs)", "V_full (fps)" };
            double[] colWidths = { 210, 62, 62, 82, 82 };
            double rowHeight = 18;

            y = DrawTableHeader(activeGfx, ref page, ref activeGfx, document, y, headers, colWidths, rowHeight);

            foreach (ReadPipe rp in pipes)
            {
                if (!capacities.TryGetValue(rp.PipeId, out Manning.CapacityResult? cap))
                    continue;

                string[] cells =
                {
                    ReportWriterCommon.Trim(rp.NetworkName + "/" + rp.PipeName, 40),
                    rp.Segment.DiameterFt.ToString("0.00", CultureInfo.InvariantCulture),
                    rp.Segment.Slope.ToString("0.0000", CultureInfo.InvariantCulture),
                    cap.FullFlowCfs.ToString("0.00", CultureInfo.InvariantCulture),
                    cap.FullVelocityFps.ToString("0.00", CultureInfo.InvariantCulture),
                };

                y = EnsureSpace(document, ref page, ref activeGfx, y, rowHeight + 4);
                y = DrawTableRow(activeGfx, y, cells, colWidths, rowHeight);
            }

            return y;
        }

        private static double DrawTableHeader(
            XGraphics gfx,
            ref PdfPage page,
            ref XGraphics activeGfx,
            PdfDocument document,
            double y,
            string[] headers,
            double[] colWidths,
            double rowHeight)
        {
            y = EnsureSpace(document, ref page, ref activeGfx, y, rowHeight + 4);
            var font = new XFont("Segoe UI", 9, XFontStyleEx.Bold);
            double x = MarginLeft;
            double tableWidth = Sum(colWidths);

            activeGfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(240, 244, 248)),
                MarginLeft, y, tableWidth, rowHeight);
            activeGfx.DrawRectangle(XPens.LightGray, MarginLeft, y, tableWidth, rowHeight);

            for (int i = 0; i < headers.Length; i++)
            {
                activeGfx.DrawString(headers[i], font, XBrushes.Black,
                    new XRect(x + 4, y + 3, colWidths[i] - 8, rowHeight), XStringFormats.TopLeft);
                x += colWidths[i];
            }

            return y + rowHeight;
        }

        private static double DrawTableRow(
            XGraphics gfx,
            double y,
            string[] cells,
            double[] colWidths,
            double rowHeight,
            bool highlight = false)
        {
            var font = new XFont("Segoe UI", 9);
            double x = MarginLeft;
            double tableWidth = Sum(colWidths);

            if (highlight)
                gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(255, 230, 230)),
                    MarginLeft, y, tableWidth, rowHeight);
            gfx.DrawRectangle(XPens.LightGray, MarginLeft, y, tableWidth, rowHeight);

            for (int i = 0; i < cells.Length; i++)
            {
                gfx.DrawString(cells[i], font, XBrushes.Black,
                    new XRect(x + 4, y + 3, colWidths[i] - 8, rowHeight), XStringFormats.TopLeft);
                x += colWidths[i];
            }

            return y + rowHeight;
        }

        private static double DrawTitle(
            XGraphics gfx,
            ref PdfPage page,
            ref XGraphics activeGfx,
            PdfDocument document,
            double y,
            string text)
        {
            y = EnsureSpace(document, ref page, ref activeGfx, y, 28);
            var font = new XFont("Segoe UI", 16, XFontStyleEx.Bold);
            activeGfx.DrawString(text, font, XBrushes.Black,
                new XRect(MarginLeft, y, ContentWidth, 24), XStringFormats.TopLeft);
            return y + 28;
        }

        private static double DrawSectionHeading(
            XGraphics gfx,
            ref PdfPage page,
            ref XGraphics activeGfx,
            PdfDocument document,
            double y,
            string text)
        {
            y = EnsureSpace(document, ref page, ref activeGfx, y, 22);
            var font = new XFont("Segoe UI", 12, XFontStyleEx.Bold);
            activeGfx.DrawString(text, font, XBrushes.Black,
                new XRect(MarginLeft, y, ContentWidth, 18), XStringFormats.TopLeft);
            return y + 22;
        }

        private static double DrawSubheading(
            XGraphics gfx,
            ref PdfPage page,
            ref XGraphics activeGfx,
            PdfDocument document,
            double y,
            string text)
        {
            var font = new XFont("Segoe UI", 10, XFontStyleEx.Bold);
            activeGfx.DrawString(text, font, XBrushes.Black,
                new XRect(MarginLeft, y, ContentWidth, 16), XStringFormats.TopLeft);
            return y + 16;
        }

        private static double DrawBodyText(
            XGraphics gfx,
            ref PdfPage page,
            ref XGraphics activeGfx,
            PdfDocument document,
            double y,
            string text)
        {
            y = EnsureSpace(document, ref page, ref activeGfx, y, 16);
            var font = new XFont("Segoe UI", 10);
            activeGfx.DrawString(text, font, XBrushes.Black,
                new XRect(MarginLeft, y, ContentWidth, 14), XStringFormats.TopLeft);
            return y + 14;
        }

        private static double DrawMonoText(
            XGraphics gfx,
            ref PdfPage page,
            ref XGraphics activeGfx,
            PdfDocument document,
            double y,
            string text)
        {
            var font = new XFont("Consolas", 8.5);
            activeGfx.DrawString(text, font, XBrushes.Black,
                new XRect(MarginLeft + 8, y, ContentWidth - 8, 12), XStringFormats.TopLeft);
            return y + 12;
        }

        private static void DrawDisclaimer(
            XGraphics gfx,
            ref PdfPage page,
            ref XGraphics activeGfx,
            PdfDocument document,
            double y)
        {
            var font = new XFont("Segoe UI", 9);
            var bold = new XFont("Segoe UI", 9, XFontStyleEx.Bold);
            double boxHeight = 72;
            y = EnsureSpace(document, ref page, ref activeGfx, y, boxHeight);

            activeGfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(255, 248, 230)),
                MarginLeft, y, ContentWidth, boxHeight);
            activeGfx.DrawRectangle(XPens.Goldenrod, MarginLeft, y, ContentWidth, boxHeight);

            activeGfx.DrawString("Disclaimer:", bold, XBrushes.Black,
                new XRect(MarginLeft + 8, y + 8, ContentWidth - 16, 14), XStringFormats.TopLeft);
            const string disclaimer =
                "This report is generated by HydroComplete for preliminary storm-sewer review. " +
                "Verify all inputs (diameter, slope, roughness, design flow) against the engineer's design basis. " +
                "Not a substitute for licensed professional judgment or jurisdiction-specific design standards.";
            activeGfx.DrawString(disclaimer, font, XBrushes.Black,
                new XRect(MarginLeft + 8, y + 24, ContentWidth - 16, boxHeight - 28),
                XStringFormats.TopLeft);
        }

        private static PdfPage AddPage(PdfDocument document)
        {
            var page = document.AddPage();
            page.Size = PdfSharp.PageSize.Letter;
            return page;
        }

        private static double PageBottom(PdfPage page) =>
            page.Height.Point - MarginBottom;

        private static double EnsureSpace(
            PdfDocument document,
            ref PdfPage page,
            ref XGraphics gfx,
            double y,
            double needed)
        {
            if (y + needed <= PageBottom(page))
                return y;

            gfx.Dispose();
            page = AddPage(document);
            gfx = XGraphics.FromPdfPage(page);
            return MarginTop;
        }

        private static double Sum(double[] values)
        {
            double total = 0;
            foreach (double v in values) total += v;
            return total;
        }
    }
}