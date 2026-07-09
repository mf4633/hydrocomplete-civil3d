using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Writes a chainage-elevation profile (invert, crown, optional HGL) to ASCII DXF.
    /// Hydraflow-style export for external CAD review — pure engine, no Autodesk refs.
    /// </summary>
    public static class ProfileDxfWriter
    {
        public const string InvertLayer = "HC-PROFILE-INVERT";
        public const string CrownLayer = "HC-PROFILE-CROWN";
        public const string HglLayer = "HC-PROFILE-HGL";
        public const string LabelLayer = "HC-PROFILE-LABEL";

        public sealed class ProfilePoint
        {
            public double ChainageFt { get; set; }
            public double ElevationFt { get; set; }
        }

        public sealed class ProfileStation
        {
            public double ChainageFt { get; set; }
            public string StructureName { get; set; } = "";
            public double InvertFt { get; set; }
            public double CrownFt { get; set; }
            public double? HglFt { get; set; }
        }

        public sealed class ProfileDxfData
        {
            public string NetworkName { get; set; } = "";
            public List<ProfilePoint> InvertPoints { get; } = new List<ProfilePoint>();
            public List<ProfilePoint> CrownPoints { get; } = new List<ProfilePoint>();
            public List<ProfilePoint> HglPoints { get; } = new List<ProfilePoint>();
            public List<ProfileStation> Stations { get; } = new List<ProfileStation>();
        }

        public sealed class ProfileDxfOptions
        {
            public double OriginX { get; set; }
            public double OriginY { get; set; }
            public double DatumElevationFt { get; set; }
            public double HorizontalScale { get; set; } = 20.0;
            public double VerticalScale { get; set; } = 20.0;
            public bool IncludeHgl { get; set; }
            public double TextHeight { get; set; } = 0.1;
        }

        public static void Write(string filePath, ProfileDxfData data, ProfileDxfOptions options)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path is required.", nameof(filePath));
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (options == null) throw new ArgumentNullException(nameof(options));

            string dxf = WriteToString(data, options);
            File.WriteAllText(filePath, dxf, Encoding.ASCII);
        }

        public static string WriteToString(ProfileDxfData data, ProfileDxfOptions options)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (options == null) throw new ArgumentNullException(nameof(options));

            double hScale = Math.Max(options.HorizontalScale, 1e-6);
            double vScale = Math.Max(options.VerticalScale, 1e-6);
            double textH = Math.Max(options.TextHeight, 1e-6);

            var sb = new StringBuilder(4096);
            WriteLine(sb, 0, "SECTION");
            WriteLine(sb, 2, "HEADER");
            WriteLine(sb, 9, "$ACADVER");
            WriteLine(sb, 1, "AC1014");
            WriteLine(sb, 9, "$INSUNITS");
            WriteLine(sb, 70, "1");
            WriteLine(sb, 0, "ENDSEC");

            WriteLine(sb, 0, "SECTION");
            WriteLine(sb, 2, "TABLES");
            WriteTable(sb, "LAYER", BuildLayers(options));
            WriteLine(sb, 0, "ENDSEC");

            WriteLine(sb, 0, "SECTION");
            WriteLine(sb, 2, "ENTITIES");

            if (data.InvertPoints.Count >= 2)
            {
                WriteLwPolyline(sb, InvertLayer, data.InvertPoints, options, hScale, vScale);
            }

            if (data.CrownPoints.Count >= 2)
            {
                WriteLwPolyline(sb, CrownLayer, data.CrownPoints, options, hScale, vScale);
            }

            if (options.IncludeHgl && data.HglPoints.Count >= 2)
            {
                WriteLwPolyline(sb, HglLayer, data.HglPoints, options, hScale, vScale);
            }

            foreach (ProfileStation station in data.Stations)
            {
                (double x, double y) = ToDxf(station.ChainageFt, station.InvertFt, options, hScale, vScale);
                var lines = new List<string>
                {
                    station.StructureName,
                    $"STA {station.ChainageFt.ToString("0.0", CultureInfo.InvariantCulture)}",
                };
                if (station.HglFt.HasValue)
                    lines.Add($"HGL {station.HglFt.Value.ToString("0.00", CultureInfo.InvariantCulture)}");

                // DXF TEXT is a single-line entity — it does not interpret \n (an MTEXT-only
                // escape). Emit one stacked TEXT entity per line instead of a literal "\n".
                for (int li = 0; li < lines.Count; li++)
                    WriteText(sb, LabelLayer, x, y - li * textH * 1.4, textH, lines[li]);
            }

            WriteLine(sb, 0, "ENDSEC");
            WriteLine(sb, 0, "EOF");
            return sb.ToString();
        }

        private static List<string> BuildLayers(ProfileDxfOptions options)
        {
            var layers = new List<string> { InvertLayer, CrownLayer, LabelLayer };
            if (options.IncludeHgl)
                layers.Add(HglLayer);
            return layers;
        }

        private static void WriteTable(StringBuilder sb, string tableName, IReadOnlyList<string> layerNames)
        {
            WriteLine(sb, 0, "TABLE");
            WriteLine(sb, 2, tableName);
            WriteLine(sb, 70, layerNames.Count.ToString(CultureInfo.InvariantCulture));

            foreach (string layer in layerNames)
            {
                WriteLine(sb, 0, "LAYER");
                WriteLine(sb, 2, layer);
                WriteLine(sb, 70, "0");
                WriteLine(sb, 62, "7");
                WriteLine(sb, 6, "CONTINUOUS");
            }

            WriteLine(sb, 0, "ENDTAB");
        }

        private static void WriteLwPolyline(
            StringBuilder sb,
            string layer,
            IReadOnlyList<ProfilePoint> points,
            ProfileDxfOptions options,
            double hScale,
            double vScale)
        {
            WriteLine(sb, 0, "LWPOLYLINE");
            WriteLine(sb, 8, layer);
            WriteLine(sb, 90, points.Count.ToString(CultureInfo.InvariantCulture));
            WriteLine(sb, 70, "0");

            foreach (ProfilePoint pt in points)
            {
                (double x, double y) = ToDxf(pt.ChainageFt, pt.ElevationFt, options, hScale, vScale);
                WriteLine(sb, 10, x.ToString("0.######", CultureInfo.InvariantCulture));
                WriteLine(sb, 20, y.ToString("0.######", CultureInfo.InvariantCulture));
            }
        }

        private static void WriteText(
            StringBuilder sb,
            string layer,
            double x,
            double y,
            double height,
            string text)
        {
            WriteLine(sb, 0, "TEXT");
            WriteLine(sb, 8, layer);
            WriteLine(sb, 10, x.ToString("0.######", CultureInfo.InvariantCulture));
            WriteLine(sb, 20, y.ToString("0.######", CultureInfo.InvariantCulture));
            WriteLine(sb, 30, "0");
            WriteLine(sb, 40, height.ToString("0.######", CultureInfo.InvariantCulture));
            WriteLine(sb, 1, text);
        }

        private static (double X, double Y) ToDxf(
            double chainageFt,
            double elevationFt,
            ProfileDxfOptions options,
            double hScale,
            double vScale)
        {
            double x = options.OriginX + chainageFt / hScale;
            double y = options.OriginY + (elevationFt - options.DatumElevationFt) / vScale;
            return (x, y);
        }

        private static void WriteLine(StringBuilder sb, int code, string value)
        {
            sb.Append(code.ToString(CultureInfo.InvariantCulture));
            sb.Append('\n');
            sb.Append(value);
            sb.Append('\n');
        }
    }
}