using System;
using System.Collections.Generic;
using System.IO;
using HydroComplete.Engine;

namespace HydroComplete.Civil3D
{
    /// <summary>
    /// Civil 3D host wrapper for live NOAA Atlas 14 PFDS fetch with local cache.
    /// </summary>
    public static class Atlas14Service
    {
        public static readonly string CacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HydroComplete",
            "idf-cache");

        private static readonly Atlas14Fetcher Fetcher = new Atlas14Fetcher(CacheDirectory);

        /// <summary>
        /// Resolve IDF coefficients for drawing coordinates. Tries cache, then live
        /// NOAA PFDS, then the nearest embedded preset when offline or out of coverage.
        /// </summary>
        public static Atlas14Resolution Resolve(double lat, double lon, int returnPeriodYears = 10)
        {
            return Fetcher.Resolve(
                lat,
                lon,
                returnPeriodYears,
                () => Atlas14Resolution.EmbeddedNearest(lat, lon, returnPeriodYears));
        }

        /// <summary>
        /// Tabular PFDS intensities at 10 minutes for the standard return periods.
        /// Uses the nearest embedded preset when live PFDS is unavailable.
        /// </summary>
        public static IReadOnlyDictionary<int, double> IntensitiesAt10Min(double lat, double lon)
        {
            try
            {
                return Fetcher.ResolveIntensitiesAtDuration(lat, lon, 10.0);
            }
            catch
            {
                Atlas14Presets.Preset preset = Atlas14Presets.Nearest(lat, lon);
                return preset.Intensity10MinInHr;
            }
        }
    }
}