using System;
using System.Collections.Generic;
using System.Linq;

namespace HydroComplete.Engine
{
    /// <summary>
    /// NOAA Atlas 14 IDF curve presets (i = a/(t+b)^c) for common US cities.
    /// Coefficients mirror HydroComplete web HydraflowEngine estimates from Atlas 14
    /// 24-hr depth ratios. Use for Rational design intensity without manual entry.
    /// </summary>
    public static class Atlas14Presets
    {
        public sealed class Preset
        {
            public Preset(string key, string displayName, string state, double lat, double lon,
                double a, double b, double c, int returnPeriodYears)
            {
                Key = key;
                DisplayName = displayName;
                State = state;
                Lat = lat;
                Lon = lon;
                A = a;
                B = b;
                C = c;
                ReturnPeriodYears = returnPeriodYears;
            }

            public string Key { get; }
            public string DisplayName { get; }
            public string State { get; }
            public double Lat { get; }
            public double Lon { get; }
            public double A { get; }
            public double B { get; }
            public double C { get; }
            public int ReturnPeriodYears { get; }

            public IdfCurve ToCurve() => new IdfCurve(A, B, C);
        }

        private static readonly Preset[] All =
        {
            new Preset("charlotte-nc",    "Charlotte, NC",    "NC", 35.23,  -80.84,  96.6,  8.5, 0.78, 10),
            new Preset("raleigh-nc",      "Raleigh, NC",      "NC", 35.78,  -78.64,  98.2,  8.5, 0.78, 10),
            new Preset("asheville-nc",    "Asheville, NC",    "NC", 35.60,  -82.55,  88.4,  8.5, 0.78, 10),
            new Preset("atlanta-ga",      "Atlanta, GA",      "GA", 33.75,  -84.39,  94.0,  8.5, 0.78, 10),
            new Preset("washington-dc",   "Washington, DC",   "DC", 38.91,  -77.04,  90.0,  8.5, 0.78, 10),
            new Preset("philadelphia-pa", "Philadelphia, PA", "PA", 39.95,  -75.17,  89.0,  8.5, 0.78, 10),
            new Preset("new-york-ny",     "New York, NY",     "NY", 40.71,  -74.01,  92.0,  9.0, 0.80, 10),
            new Preset("boston-ma",       "Boston, MA",       "MA", 42.36,  -71.06,  86.0,  9.0, 0.80, 10),
            new Preset("chicago-il",      "Chicago, IL",      "IL", 41.88,  -87.63,  78.0,  8.5, 0.78, 10),
            new Preset("detroit-mi",      "Detroit, MI",      "MI", 42.33,  -83.05,  70.0,  8.5, 0.78, 10),
            new Preset("minneapolis-mn",  "Minneapolis, MN",  "MN", 44.98,  -93.27,  74.0,  8.5, 0.78, 10),
            new Preset("denver-co",       "Denver, CO",       "CO", 39.74, -104.99,  52.0,  8.0, 0.75, 10),
            new Preset("dallas-tx",       "Dallas, TX",       "TX", 32.78,  -96.80, 105.0,  9.0, 0.80, 10),
            new Preset("houston-tx",      "Houston, TX",      "TX", 29.76,  -95.37, 130.0,  9.0, 0.80, 10),
            new Preset("miami-fl",        "Miami, FL",        "FL", 25.76,  -80.19, 140.0,  9.0, 0.78, 10),
            new Preset("phoenix-az",      "Phoenix, AZ",      "AZ", 33.45, -112.07,  48.0,  7.0, 0.72, 10),
            new Preset("los-angeles-ca",  "Los Angeles, CA",  "CA", 34.05, -118.24,  55.0,  7.0, 0.72, 10),
            new Preset("seattle-wa",      "Seattle, WA",      "WA", 47.61, -122.33,  42.0,  8.0, 0.75, 10),
        };

        public static IReadOnlyList<Preset> List() => All;

        public static Preset? Find(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            return All.FirstOrDefault(p =>
                string.Equals(p.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Nearest Atlas 14 preset for optional drawing coordinates; null when geo is unavailable.
        /// </summary>
        public static Preset? ResolveForDrawing(double? lat, double? lon)
        {
            if (!lat.HasValue || !lon.HasValue) return null;
            double la = lat.Value;
            double lo = lon.Value;
            if (double.IsNaN(la) || double.IsNaN(lo) || double.IsInfinity(la) || double.IsInfinity(lo))
                return null;
            if (la < -90.0 || la > 90.0 || lo < -180.0 || lo > 180.0) return null;
            return Nearest(la, lo);
        }

        /// <summary>Nearest preset by great-circle distance (degrees, adequate for city pick).</summary>
        public static Preset Nearest(double lat, double lon)
        {
            Preset best = All[0];
            double bestDist = double.MaxValue;
            foreach (Preset p in All)
            {
                double dLat = p.Lat - lat;
                double dLon = p.Lon - lon;
                double dist = dLat * dLat + dLon * dLon;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = p;
                }
            }
            return best;
        }

        /// <summary>
        /// Composite Rational peak using system Tc and a preset IDF curve.
        /// </summary>
        public static Rational.PeakFlowResult PeakFromCatchments(
            IReadOnlyList<Catchment> catchments, Preset preset)
        {
            if (catchments == null) throw new ArgumentNullException(nameof(catchments));
            if (preset == null) throw new ArgumentNullException(nameof(preset));
            if (catchments.Count == 0)
                throw new ArgumentException("At least one catchment is required.", nameof(catchments));

            double systemTc = 0.0;
            foreach (Catchment cm in catchments)
                systemTc = Math.Max(systemTc, cm.TcMinutes);

            var idf = preset.ToCurve();
            var intensity = idf.Intensity(systemTc);
            var peak = Rational.Peak(catchments, intensity.IntensityInHr);
            peak.Steps.Insert(0, new CalcStep("IDF_preset", preset.ReturnPeriodYears, "yr",
                $"{preset.Key} — {preset.DisplayName}"));
            foreach (CalcStep step in intensity.Steps)
                peak.Steps.Insert(1, step);
            return peak;
        }
    }
}