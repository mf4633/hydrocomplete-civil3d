using System;
using System.Collections.Generic;
using System.Linq;

namespace HydroComplete.Engine
{
    /// <summary>
    /// NOAA Atlas 14 IDF curve presets (i = a/(t+b)^c) for common US cities.
    /// Coefficients are fit directly from NOAA Atlas 14 PFDS partial-duration
    /// intensities (see the provenance note on <c>All</c>). Use for Rational design
    /// intensity without manual entry; the live Atlas14Fetcher supersedes these when
    /// the drawing is geolocated and online.
    /// </summary>
    public static class Atlas14Presets
    {
        public sealed class Preset
        {
            private readonly Dictionary<int, (double A, double B, double C)> _curves;

            public Preset(
                string key,
                string displayName,
                string state,
                double lat,
                double lon,
                double a2, double b2, double c2,
                double a10, double b10, double c10,
                double a25, double b25, double c25,
                double a100, double b100, double c100,
                double i10_2yr, double i10_10yr, double i10_25yr, double i10_100yr)
            {
                Key = key;
                DisplayName = displayName;
                State = state;
                Lat = lat;
                Lon = lon;
                ReturnPeriodYears = 10;
                _curves = new Dictionary<int, (double A, double B, double C)>
                {
                    [2] = (a2, b2, c2),
                    [10] = (a10, b10, c10),
                    [25] = (a25, b25, c25),
                    [100] = (a100, b100, c100),
                };
                Intensity10MinInHr = new Dictionary<int, double>
                {
                    [2] = i10_2yr,
                    [10] = i10_10yr,
                    [25] = i10_25yr,
                    [100] = i10_100yr,
                };
            }

            public string Key { get; }
            public string DisplayName { get; }
            public string State { get; }
            public double Lat { get; }
            public double Lon { get; }
            public int ReturnPeriodYears { get; }

            /// <summary>10-yr curve coefficients (default design return period).</summary>
            public double A => _curves[10].A;
            public double B => _curves[10].B;
            public double C => _curves[10].C;

            /// <summary>Tabular PFDS 10-min intensities (in/hr) for standard return periods.</summary>
            public IReadOnlyDictionary<int, double> Intensity10MinInHr { get; }

            public IdfCurve ToCurve(int returnPeriodYears = 10)
            {
                if (!_curves.TryGetValue(returnPeriodYears, out (double A, double B, double C) curve))
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(returnPeriodYears),
                        "Return period must be one of the embedded standard periods (2, 10, 25, 100).");
                }

                return new IdfCurve(curve.A, curve.B, curve.C);
            }

            public string MultiReturnPeriod10MinLabel =>
                Atlas14Fetcher.FormatMultiReturnPeriodIntensities(Intensity10MinInHr);
        }

        // Coefficients refit 2026-06-17 from live NOAA Atlas 14 PFDS partial-duration
        // intensities (2/10/25/100-yr; 5/10/15/30/60/120-min) via the same log-linear
        // fit as Atlas14Fetcher/IdfCurveFitter. Tabular i@10m values are from the PFDS
        // 10-min row. These are the OFFLINE fallback; the live fetcher supersedes them
        // when the drawing is geolocated and online.
        // NOTE: Seattle / Pacific NW is intentionally absent — it is OUTSIDE NOAA
        // Atlas 14 coverage (PFDS returns "not within a project area"; that region
        // is NOAA Atlas 2). Add a separate data source before serving PNW sites.
        private static readonly Preset[] All =
        {
            new Preset("charlotte-nc",    "Charlotte, NC",    "NC", 35.23,  -80.84,  75.09, 13.25, 0.891,  81.21, 13.50, 0.832,  81.57, 13.50, 0.802,  69.60, 12.00, 0.729,  4.54, 5.81, 6.40, 7.19),
            new Preset("raleigh-nc",      "Raleigh, NC",      "NC", 35.78,  -78.64,  70.57, 12.75, 0.877,  72.49, 12.50, 0.807,  71.11, 12.25, 0.772,  59.68, 10.50, 0.693,  4.55, 5.82, 6.41, 7.25),
            new Preset("asheville-nc",    "Asheville, NC",    "NC", 35.60,  -82.55,  73.41, 14.50, 0.925,  99.76, 16.25, 0.904, 105.49, 16.50, 0.875, 104.65, 16.00, 0.823,  3.79, 5.14, 5.89, 7.03),
            new Preset("atlanta-ga",      "Atlanta, GA",      "GA", 33.75,  -84.39,  28.19,  5.50, 0.703,  40.11,  5.50, 0.706,  45.92,  5.25, 0.695,  54.21,  4.75, 0.680,  4.02, 5.69, 6.78, 8.55),
            new Preset("washington-dc",   "Washington, DC",   "DC", 38.91,  -77.04,  69.47, 13.50, 0.897,  82.66, 14.25, 0.852,  78.40, 13.50, 0.804,  67.53, 11.75, 0.724,  4.07, 5.41, 6.11, 7.15),
            new Preset("philadelphia-pa", "Philadelphia, PA", "PA", 39.95,  -75.17,  52.50, 11.50, 0.841,  60.49, 12.00, 0.793,  58.81, 11.50, 0.754,  52.52, 10.25, 0.687,  3.97, 5.17, 5.76, 6.56),
            new Preset("new-york-ny",     "New York, NY",     "NY", 40.71,  -74.01,  24.05,  4.00, 0.700,  36.45,  4.00, 0.708,  44.03,  4.00, 0.710,  56.08,  4.00, 0.714,  3.77, 5.59, 6.73, 8.47),
            new Preset("boston-ma",       "Boston, MA",       "MA", 42.36,  -71.06,  20.36,  4.00, 0.708,  31.35,  4.00, 0.701,  38.36,  4.00, 0.700,  48.72,  4.00, 0.697,  3.14, 4.92, 6.03, 7.74),
            new Preset("chicago-il",      "Chicago, IL",      "IL", 41.88,  -87.63,  52.44,  9.25, 0.848,  55.29,  8.25, 0.779,  56.21,  7.75, 0.746,  55.70,  6.75, 0.696,  4.29, 5.73, 6.51, 7.70),
            new Preset("detroit-mi",      "Detroit, MI",      "MI", 42.33,  -83.05,  22.24,  5.25, 0.715,  32.83,  5.25, 0.714,  39.59,  5.25, 0.713,  48.87,  5.00, 0.704,  3.13, 4.63, 5.60, 7.16),
            new Preset("minneapolis-mn",  "Minneapolis, MN",  "MN", 44.98,  -93.27,  24.31,  5.00, 0.691,  31.88,  4.25, 0.656,  36.43,  4.00, 0.636,  44.37,  4.00, 0.613,  3.68, 5.49, 6.70, 8.69),
            new Preset("denver-co",       "Denver, CO",       "CO", 39.74, -104.99,  19.87,  6.75, 0.764,  35.05,  7.00, 0.786,  43.66,  6.75, 0.782,  59.99,  6.75, 0.783,  2.26, 3.71, 4.73, 6.48),
            new Preset("dallas-tx",       "Dallas, TX",       "TX", 32.78,  -96.80,  45.96,  9.50, 0.765,  57.31,  8.50, 0.737,  63.29,  8.00, 0.722,  69.90,  7.25, 0.697,  4.75, 6.72, 7.93, 9.73),
            new Preset("houston-tx",      "Houston, TX",      "TX", 29.76,  -95.37,  48.02,  9.25, 0.726,  51.13,  6.75, 0.657,  51.17,  5.25, 0.616,  54.41,  4.00, 0.574,  5.57, 8.07, 9.67, 12.20),
            new Preset("miami-fl",        "Miami, FL",        "FL", 25.76,  -80.19,  31.41,  4.50, 0.628,  39.39,  4.00, 0.599,  44.51,  4.00, 0.584,  51.63,  4.00, 0.561,  5.73, 7.93, 9.33, 11.50),
            new Preset("phoenix-az",      "Phoenix, AZ",      "AZ", 33.45, -112.07,  30.81, 10.00, 0.875,  62.26, 11.50, 0.918,  76.75, 11.50, 0.920,  95.72, 11.25, 0.913,  2.21, 3.67, 4.49, 5.79),
            new Preset("los-angeles-ca",  "Los Angeles, CA",  "CA", 34.05, -118.24,   8.37,  4.00, 0.611,  12.99,  4.00, 0.609,  16.13,  4.00, 0.611,  21.24,  4.00, 0.612,  1.67, 2.60, 3.22, 4.22),
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
            IReadOnlyList<Catchment> catchments,
            Preset preset,
            int returnPeriodYears = 10)
        {
            if (catchments == null) throw new ArgumentNullException(nameof(catchments));
            if (preset == null) throw new ArgumentNullException(nameof(preset));
            if (catchments.Count == 0)
                throw new ArgumentException("At least one catchment is required.", nameof(catchments));

            double systemTc = 0.0;
            foreach (Catchment cm in catchments)
                systemTc = Math.Max(systemTc, cm.TcMinutes);

            var idf = preset.ToCurve(returnPeriodYears);
            var intensity = idf.Intensity(systemTc);
            var peak = Rational.Peak(catchments, intensity.IntensityInHr);
            peak.Steps.Insert(0, new CalcStep("IDF_preset", returnPeriodYears, "yr",
                $"{preset.Key} — {preset.DisplayName}"));
            foreach (CalcStep step in intensity.Steps)
                peak.Steps.Insert(1, step);
            return peak;
        }
    }
}