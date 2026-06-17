using System;
using System.Globalization;
using System.IO;

namespace HydroComplete.Civil3D.Writing
{
    /// <summary>Shared paths and formatting for HTML/PDF Manning reports.</summary>
    internal static class ReportWriterCommon
    {
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
                .Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal)
                .Replace("\"", "&quot;", StringComparison.Ordinal);
        }

        public static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max - 1) + "~";
        }
    }
}