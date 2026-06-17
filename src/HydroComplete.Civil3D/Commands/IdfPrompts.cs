using System;
using System.Globalization;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using HydroComplete.Civil3D.Reading;
using HydroComplete.Engine;

namespace HydroComplete.Civil3D.Commands
{
    /// <summary>Shared IDF / Atlas 14 preset prompts for HC_RATIONAL and HC_HGL.</summary>
    internal static class IdfPrompts
    {
        public static IdfCurve PromptCustomIdfCurve(Editor ed)
        {
            double a = PromptDouble(ed, "\nCustom IDF coefficient a", 120.0);
            double b = PromptDouble(ed, "Custom IDF coefficient b", 12.0);
            double c = PromptDouble(ed, "Custom IDF coefficient c", 0.85);
            return new IdfCurve(a, b, c);
        }

        public static Atlas14Presets.Preset? PromptPreset(Editor ed, Database? db = null)
        {
            DrawingGeolocation.Result? geo = db != null ? DrawingGeolocation.TryRead(db) : null;
            Atlas14Presets.Preset? autoPreset = geo != null
                ? Atlas14Presets.ResolveForDrawing(geo.Lat, geo.Lon)
                : null;

            ed.WriteMessage("\n--- NOAA Atlas 14 IDF presets (10-yr, i=a/(t+b)^c) ---");
            if (autoPreset != null && geo != null)
            {
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  Drawing geolocation ({0}): lat {1:0.####}, lon {2:0.####}",
                    geo.Source, geo.Lat, geo.Lon));
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  auto             {0} ({1}) — nearest preset (Enter to accept)",
                    autoPreset.DisplayName, autoPreset.Key));
            }

            foreach (Atlas14Presets.Preset p in Atlas14Presets.List())
            {
                var sample = p.ToCurve().Intensity(10.0);
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  {0,-16} {1,-22} i@10min={2:0.00} in/hr",
                    p.Key, p.DisplayName, sample.IntensityInHr));
            }
            ed.WriteMessage("\n  custom           Enter a/b/c manually");
            ed.WriteMessage("\n");

            string fallbackKey = "charlotte-nc";
            var opts = new PromptStringOptions("\nPreset key (or custom)")
            {
                DefaultValue = autoPreset != null ? "auto" : fallbackKey,
                UseDefaultValue = true,
                AllowSpaces = false,
            };
            PromptResult res = ed.GetString(opts);
            string key = res.Status == PromptStatus.OK ? res.StringResult.Trim() : opts.DefaultValue;

            if (string.Equals(key, "custom", StringComparison.OrdinalIgnoreCase))
                return null;

            if (autoPreset != null &&
                (string.IsNullOrEmpty(key) ||
                 string.Equals(key, "auto", StringComparison.OrdinalIgnoreCase)))
            {
                return autoPreset;
            }

            Atlas14Presets.Preset? preset = Atlas14Presets.Find(key);
            if (preset == null)
            {
                ed.WriteMessage($"\nUnknown preset '{key}' — using Charlotte, NC.\n");
                preset = Atlas14Presets.Find(fallbackKey);
            }
            return preset;
        }

        public static void WriteAtlas14List(Editor ed)
        {
            var sb = new StringBuilder();
            sb.AppendLine("\n--- HydroComplete: NOAA Atlas 14 IDF presets ---");
            sb.AppendLine("  i = a/(t+b)^c   (10-yr return period, t in minutes)");
            foreach (Atlas14Presets.Preset p in Atlas14Presets.List())
            {
                var at10 = p.ToCurve().Intensity(10.0);
                var at15 = p.ToCurve().Intensity(15.0);
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-16} {1,-20} a={2,5:0.#} b={3,4:0.#} c={4:0.##}  i@10m={5:0.00}  i@15m={6:0.00}",
                    p.Key, p.DisplayName, p.A, p.B, p.C,
                    at10.IntensityInHr, at15.IntensityInHr));
            }
            sb.AppendLine("\nUse preset key with HC_RATIONAL or HC_HGL (Rational Q option).");
            sb.AppendLine("When the drawing has geolocation, press Enter at the preset prompt for auto.\n");
            ed.WriteMessage(sb.ToString());
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
    }
}