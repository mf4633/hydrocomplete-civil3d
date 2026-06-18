using System;
using System.Collections.Generic;
using System.Linq;

namespace HydroComplete.Engine
{
    /// <summary>
    /// NOAA Atlas 14 IDF curve presets (i = a/(t+b)^c) for common US cities.
    /// Coefficients are fit directly from NOAA Atlas 14 PFDS 10-yr partial-duration
    /// intensities (see the provenance note on <c>All</c>). Use for Rational design
    /// intensity without manual entry; the live Atlas14Fetcher supersedes these when
    /// the drawing is geolocated and online.
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

        // Coefficients refit 2026-06-17 from live NOAA Atlas 14 PFDS partial-duration
        // intensities (10-yr; 5/10/15/30/60/120-min) via the same log-linear fit as
        // Atlas14Fetcher/IdfCurveFitter. Fit RMSE < 2.5% for all but LA (5.2%) and
        // Miami (3.5%). These are the OFFLINE fallback; the live fetcher supersedes
        // them when the drawing is geolocated and online.
        // NOTE: Seattle / Pacific NW is intentionally absent — it is OUTSIDE NOAA
        // Atlas 14 coverage (PFDS returns "not within a project area"; that region
        // is NOAA Atlas 2). Add a separate data source before serving PNW sites.
        private static readonly Preset[] All =
        {
            new Preset("charlotte-nc",    "Charlotte, NC",    "NC", 35.23,  -80.84,  81.21, 13.50, 0.832, 10),
            new Preset("raleigh-nc",      "Raleigh, NC",      "NC", 35.78,  -78.64,  72.49, 12.50, 0.807, 10),
            new Preset("asheville-nc",    "Asheville, NC",    "NC", 35.60,  -82.55,  99.76, 16.25, 0.904, 10),
            new Preset("atlanta-ga",      "Atlanta, GA",      "GA", 33.75,  -84.39,  40.11,  5.50, 0.706, 10),
            new Preset("washington-dc",   "Washington, DC",   "DC", 38.91,  -77.04,  82.66, 14.25, 0.852, 10),
            new Preset("philadelphia-pa", "Philadelphia, PA", "PA", 39.95,  -75.17,  60.49, 12.00, 0.793, 10),
            new Preset("new-york-ny",     "New York, NY",     "NY", 40.71,  -74.01,  36.45,  4.00, 0.708, 10),
            new Preset("boston-ma",       "Boston, MA",       "MA", 42.36,  -71.06,  31.35,  4.00, 0.701, 10),
            new Preset("chicago-il",      "Chicago, IL",      "IL", 41.88,  -87.63,  55.29,  8.25, 0.779, 10),
            new Preset("detroit-mi",      "Detroit, MI",      "MI", 42.33,  -83.05,  32.83,  5.25, 0.714, 10),
            new Preset("minneapolis-mn",  "Minneapolis, MN",  "MN", 44.98,  -93.27,  31.88,  4.25, 0.656, 10),
            new Preset("denver-co",       "Denver, CO",       "CO", 39.74, -104.99,  35.05,  7.00, 0.786, 10),
            new Preset("dallas-tx",       "Dallas, TX",       "TX", 32.78,  -96.80,  57.31,  8.50, 0.737, 10),
            new Preset("houston-tx",      "Houston, TX",      "TX", 29.76,  -95.37,  51.13,  6.75, 0.657, 10),
            new Preset("miami-fl",        "Miami, FL",        "FL", 25.76,  -80.19,  39.39,  4.00, 0.599, 10),
            new Preset("phoenix-az",      "Phoenix, AZ",      "AZ", 33.45, -112.07,  62.26, 11.50, 0.918, 10),
            new Preset("los-angeles-ca",  "Los Angeles, CA",  "CA", 34.05, -118.24,  12.99,  4.00, 0.609, 10),
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

        /// <summary>Nearest preset by Euclidean degree distance (unweighted lat/lon; adequate for a sparse city pick).</summary>
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