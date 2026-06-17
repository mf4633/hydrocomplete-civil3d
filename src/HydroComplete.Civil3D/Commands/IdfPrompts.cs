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

        public static Atlas14Resolution? PromptPreset(Editor ed, Database? db = null)
        {
            DrawingGeolocation.Result? geo = db != null ? DrawingGeolocation.TryRead(db) : null;
            Atlas14Resolution? autoResolution = null;
            if (geo != null)
            {
                try
                {
                    autoResolution = Atlas14Service.Resolve(geo.Lat, geo.Lon);
                }
                catch
                {
                    autoResolution = Atlas14Resolution.EmbeddedNearest(geo.Lat, geo.Lon);
                }
            }

            ed.WriteMessage("\n--- NOAA Atlas 14 IDF (10-yr, i=a/(t+b)^c) ---");
            if (autoResolution != null && geo != null)
            {
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  Drawing geolocation ({0}): lat {1:0.####}, lon {2:0.####}",
                    geo.Source, geo.Lat, geo.Lon));
                var autoSample = autoResolution.ToCurve().Intensity(10.0);
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  auto             {0} [{1}] i@10min={2:0.00} in/hr (Enter to accept)",
                    autoResolution.DisplayLabel,
                    autoResolution.SourceLabel,
                    autoSample.IntensityInHr));
                if (!string.IsNullOrWhiteSpace(autoResolution.ProjectArea))
                {
                    ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                        "\n                   NOAA project area: {0}", autoResolution.ProjectArea));
                }
            }

            ed.WriteMessage("\n  --- embedded city presets ---");
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
                DefaultValue = autoResolution != null ? "auto" : fallbackKey,
                UseDefaultValue = true,
                AllowSpaces = false,
            };
            PromptResult res = ed.GetString(opts);
            string key = res.Status == PromptStatus.OK ? res.StringResult.Trim() : opts.DefaultValue;

            if (string.Equals(key, "custom", StringComparison.OrdinalIgnoreCase))
                return null;

            if (autoResolution != null &&
                (string.IsNullOrEmpty(key) ||
                 string.Equals(key, "auto", StringComparison.OrdinalIgnoreCase)))
            {
                return autoResolution;
            }

            Atlas14Presets.Preset? preset = Atlas14Presets.Find(key);
            if (preset == null)
            {
                ed.WriteMessage($"\nUnknown preset '{key}' — using Charlotte, NC (embedded).\n");
                preset = Atlas14Presets.Find(fallbackKey);
            }
            return Atlas14Resolution.FromPreset(preset!);
        }

        public static void WriteAtlas14List(Editor ed)
        {
            var sb = new StringBuilder();
            sb.AppendLine("\n--- HydroComplete: NOAA Atlas 14 IDF ---");
            sb.AppendLine("  i = a/(t+b)^c   (10-yr return period, t in minutes)");
            sb.AppendLine("  Live PFDS fetch: when the drawing has geolocation, auto uses NOAA HDSC");
            sb.AppendLine("  for the drawing coordinates (cached 30 days under %APPDATA%\\HydroComplete\\idf-cache).");
            sb.AppendLine("  Offline or out-of-coverage locations fall back to the nearest embedded city.");
            sb.AppendLine();
            sb.AppendLine("  embedded presets:");
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
            sb.AppendLine("When the drawing has geolocation, press Enter at the preset prompt for auto (live when online).\n");
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