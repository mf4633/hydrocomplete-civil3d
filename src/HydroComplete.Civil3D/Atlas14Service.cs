using System;
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
                () => Atlas14Resolution.EmbeddedNearest(lat, lon));
        }
    }
}