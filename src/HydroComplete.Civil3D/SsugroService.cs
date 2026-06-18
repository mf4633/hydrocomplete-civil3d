using System;
using System.IO;
using HydroComplete.Engine;

namespace HydroComplete.Civil3D
{
    /// <summary>Civil 3D host wrapper for live USDA SSURGO fetch with local cache.</summary>
    public static class SsugroService
    {
        public static readonly string CacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HydroComplete",
            "ssurgo-cache");

        private static readonly SsugroFetcher Fetcher = new SsugroFetcher(CacheDirectory);

        /// <summary>
        /// Resolve SSURGO map unit data for drawing coordinates. Tries cache, then live
        /// USDA SDA, then regional fallback when offline or out of coverage.
        /// </summary>
        public static SsugroResolution Resolve(double lat, double lon)
        {
            return Fetcher.Resolve(lat, lon, () => SsugroResolution.RegionalFallback(lat, lon));
        }
    }
}