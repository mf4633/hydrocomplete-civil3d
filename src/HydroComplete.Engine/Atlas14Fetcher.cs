using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Fetches NOAA Atlas 14 PFDS intensity-duration data for a lat/lon, fits
    /// i = a/(t+b)^c for the requested return period, and caches results locally.
    /// </summary>
    public sealed class Atlas14Fetcher
    {
        /// <summary>
        /// Public PFDS CSV endpoint documented at https://hdsc.nws.noaa.gov/pfds/.
        /// NOTE: the path has no "/hdsc/" segment — the old /cgi-bin/hdsc/new/ URL
        /// 301-redirects here, which only worked via HttpClient auto-redirect.
        /// </summary>
        public const string DefaultPfdsIntensityUrl =
            "https://hdsc.nws.noaa.gov/cgi-bin/new/fe_text.csv";

        /// <summary>Standard design return periods surfaced in presets and HC_ATLAS14.</summary>
        public static readonly int[] StandardReturnPeriods = { 2, 10, 25, 100 };

        /// <summary>All ARI columns present in NOAA PFDS intensity tables.</summary>
        public static readonly int[] SupportedReturnPeriods =
            { 1, 2, 5, 10, 25, 50, 100, 200, 500, 1000 };

        public static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromDays(30);

        private static readonly HttpClient SharedHttp = CreateHttpClient();

        private readonly string _pfdsUrl;
        private readonly string? _cacheDirectory;
        private readonly TimeSpan _cacheTtl;
        private readonly Func<HttpClient> _httpClientFactory;

        public Atlas14Fetcher(
            string? cacheDirectory = null,
            TimeSpan? cacheTtl = null,
            string? pfdsUrl = null,
            Func<HttpClient>? httpClientFactory = null)
        {
            _cacheDirectory = cacheDirectory;
            _cacheTtl = cacheTtl ?? DefaultCacheTtl;
            _pfdsUrl = string.IsNullOrWhiteSpace(pfdsUrl) ? DefaultPfdsIntensityUrl : pfdsUrl!.Trim();
            _httpClientFactory = httpClientFactory ?? (() => SharedHttp);
        }

        /// <summary>
        /// Resolve IDF coefficients for a location. Uses cache when fresh, then live
        /// NOAA PFDS, then <paramref name="fallback"/> when offline or out of coverage.
        /// </summary>
        public Atlas14Resolution Resolve(
            double lat,
            double lon,
            int returnPeriodYears = 10,
            Func<Atlas14Resolution>? fallback = null)
        {
            return ResolveAsync(lat, lon, returnPeriodYears, fallback, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        public async Task<Atlas14Resolution> ResolveAsync(
            double lat,
            double lon,
            int returnPeriodYears = 10,
            Func<Atlas14Resolution>? fallback = null,
            CancellationToken cancellationToken = default)
        {
            ValidateCoordinates(lat, lon);

            Atlas14CacheEntry? cached = TryReadCache(lat, lon, returnPeriodYears);
            if (cached != null && !cached.IsExpired(DateTime.UtcNow))
            {
                return cached.ToResolution(Atlas14Source.Cache);
            }

            try
            {
                string csv = await DownloadCsvAsync(lat, lon, cancellationToken).ConfigureAwait(false);
                Atlas14CacheEntry entry = ParseAndFit(csv, lat, lon, returnPeriodYears);
                WriteCache(entry);
                return entry.ToResolution(Atlas14Source.Live);
            }
            catch
            {
                if (cached != null)
                    return cached.ToResolution(Atlas14Source.Cache);

                if (fallback != null)
                    return fallback();

                throw;
            }
        }

        /// <summary>
        /// Read tabular PFDS intensities at a duration without fitting IDF curves.
        /// Uses a single NOAA download and does not write per-return-period cache files.
        /// </summary>
        public IReadOnlyDictionary<int, double> ResolveIntensitiesAtDuration(
            double lat,
            double lon,
            double durationMin,
            IReadOnlyList<int>? returnPeriodYears = null)
        {
            return ResolveIntensitiesAtDurationAsync(
                    lat,
                    lon,
                    durationMin,
                    returnPeriodYears,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        public async Task<IReadOnlyDictionary<int, double>> ResolveIntensitiesAtDurationAsync(
            double lat,
            double lon,
            double durationMin,
            IReadOnlyList<int>? returnPeriodYears = null,
            CancellationToken cancellationToken = default)
        {
            ValidateCoordinates(lat, lon);

            try
            {
                string csv = await DownloadCsvAsync(lat, lon, cancellationToken).ConfigureAwait(false);
                return ParseIntensitiesAtDuration(csv, durationMin, returnPeriodYears);
            }
            catch
            {
                IReadOnlyList<int> periods = returnPeriodYears ?? StandardReturnPeriods;
                var fromCache = new Dictionary<int, double>(periods.Count);
                foreach (int returnPeriod in periods)
                {
                    Atlas14CacheEntry? cached = TryReadCache(lat, lon, returnPeriod);
                    if (cached == null) continue;

                    fromCache[returnPeriod] =
                        cached.A / Math.Pow(durationMin + cached.B, cached.C);
                }

                if (fromCache.Count > 0)
                    return fromCache;

                throw;
            }
        }

        /// <summary>Parse a PFDS intensity CSV (live or fixture) without network or cache.</summary>
        public static Atlas14CacheEntry ParseAndFit(
            string csv,
            double lat,
            double lon,
            int returnPeriodYears = 10)
        {
            if (string.IsNullOrWhiteSpace(csv))
                throw new InvalidDataException("NOAA PFDS response was empty.");

            if (csv.IndexOf("ErrorMsg", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string msg = ExtractErrorMessage(csv);
                throw new InvalidDataException(msg);
            }

            string? projectArea = ExtractMetadata(csv, "Project area:");
            var table = ParseIntensityTable(csv, returnPeriodYears);
            if (table.Count < 3)
                throw new InvalidDataException("NOAA PFDS table did not contain enough duration rows.");

            IdfFit fit = IdfCurveFitter.Fit(table);
            DateTime fetchedUtc = ExtractTimestampUtc(csv) ?? DateTime.UtcNow;

            return new Atlas14CacheEntry(
                lat,
                lon,
                returnPeriodYears,
                fit.A,
                fit.B,
                fit.C,
                projectArea,
                fetchedUtc,
                fetchedUtc.Add(DefaultCacheTtl));
        }

        private async Task<string> DownloadCsvAsync(
            double lat, double lon, CancellationToken cancellationToken)
        {
            string url = BuildRequestUrl(lat, lon);
            HttpClient client = _httpClientFactory();
            using (var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }

        internal string BuildRequestUrl(double lat, double lon)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}?lat={1:0.####}&lon={2:0.####}&data=intensity&units=english&series=pds",
                _pfdsUrl.TrimEnd('?'),
                lat,
                lon);
        }

        private Atlas14CacheEntry? TryReadCache(double lat, double lon, int returnPeriodYears)
        {
            if (string.IsNullOrWhiteSpace(_cacheDirectory)) return null;

            string path = CacheFilePath(_cacheDirectory!, lat, lon, returnPeriodYears);
            if (!File.Exists(path)) return null;

            try
            {
                string json = File.ReadAllText(path);
                return Atlas14CacheEntry.FromJson(json);
            }
            catch
            {
                return null;
            }
        }

        private void WriteCache(Atlas14CacheEntry entry)
        {
            if (string.IsNullOrWhiteSpace(_cacheDirectory)) return;

            Directory.CreateDirectory(_cacheDirectory!);
            string path = CacheFilePath(_cacheDirectory!, entry.Lat, entry.Lon, entry.ReturnPeriodYears);
            File.WriteAllText(path, entry.ToJson());
        }

        public static string CacheFilePath(string cacheDirectory, double lat, double lon, int returnPeriodYears)
        {
            string key = string.Format(
                CultureInfo.InvariantCulture,
                "{0:0.####}_{1:0.####}_{2}yr.json",
                lat,
                lon,
                returnPeriodYears);
            return Path.Combine(cacheDirectory, key);
        }

        private static void ValidateCoordinates(double lat, double lon)
        {
            if (double.IsNaN(lat) || double.IsNaN(lon) || double.IsInfinity(lat) || double.IsInfinity(lon))
                throw new ArgumentOutOfRangeException(nameof(lat), "Latitude and longitude must be finite.");
            if (lat < -90.0 || lat > 90.0)
                throw new ArgumentOutOfRangeException(nameof(lat), "Latitude must be between -90 and 90.");
            if (lon < -180.0 || lon > 180.0)
                throw new ArgumentOutOfRangeException(nameof(lon), "Longitude must be between -180 and 180.");
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient
            {
                // Interactive: Resolve() runs synchronously on the CAD command thread,
                // so this caps how long the UI can freeze waiting on NOAA before the
                // embedded/cached fallback kicks in.
                Timeout = TimeSpan.FromSeconds(8),
            };
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "HydroComplete/0.9.0 (NOAA Atlas 14 PFDS client)");
            return client;
        }

        private static string ExtractErrorMessage(string csv)
        {
            const string marker = "ErrorMsg =";
            int idx = csv.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return "NOAA PFDS returned an error.";
            string tail = csv.Substring(idx + marker.Length).Trim();
            int end = tail.IndexOf(';');
            if (end >= 0) tail = tail.Substring(0, end);
            return tail.Trim().Trim('\'', '"', ' ');
        }

        private static string? ExtractMetadata(string csv, string label)
        {
            foreach (string line in SplitLines(csv))
            {
                if (!line.StartsWith(label, StringComparison.OrdinalIgnoreCase)) continue;
                return line.Substring(label.Length).Trim();
            }
            return null;
        }

        private static DateTime? ExtractTimestampUtc(string csv)
        {
            foreach (string line in SplitLines(csv))
            {
                if (!line.StartsWith("Date/time (GMT):", StringComparison.OrdinalIgnoreCase)) continue;
                string value = line.Substring("Date/time (GMT):".Length).Trim();
                if (DateTime.TryParseExact(
                        value,
                        "ddd MMM dd HH:mm:ss yyyy",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out DateTime parsed))
                {
                    return parsed.ToUniversalTime();
                }
            }
            return null;
        }

        public static List<DurationIntensityPoint> ParseIntensityTable(
            string csv, int returnPeriodYears)
        {
            int ariColumn = ReturnPeriodColumnIndex(returnPeriodYears);
            var points = new List<DurationIntensityPoint>();
            bool inMainTable = false;

            foreach (string rawLine in SplitLines(csv))
            {
                string line = rawLine.Trim();
                if (line.Length == 0) continue;

                if (TryEnterIntensityTable(line, ref inMainTable))
                    continue;

                if (!inMainTable) continue;

                if (line.StartsWith("PRECIPITATION FREQUENCY ESTIMATES AT", StringComparison.OrdinalIgnoreCase))
                    break;

                if (!TryParseDurationRow(line, ariColumn, out double durationMin, out double intensityInHr))
                    continue;

                if (durationMin > 0.0 && intensityInHr > 0.0)
                    points.Add(new DurationIntensityPoint(durationMin, intensityInHr));
            }

            return points;
        }

        /// <summary>
        /// Read tabular PFDS intensities (in/hr) at a single duration for one or more return periods.
        /// </summary>
        public static IReadOnlyDictionary<int, double> ParseIntensitiesAtDuration(
            string csv,
            double durationMin,
            IReadOnlyList<int>? returnPeriodYears = null)
        {
            if (string.IsNullOrWhiteSpace(csv))
                throw new InvalidDataException("NOAA PFDS response was empty.");

            IReadOnlyList<int> periods = returnPeriodYears ?? StandardReturnPeriods;
            bool inMainTable = false;

            foreach (string rawLine in SplitLines(csv))
            {
                string line = rawLine.Trim();
                if (line.Length == 0) continue;

                if (TryEnterIntensityTable(line, ref inMainTable))
                    continue;

                if (!inMainTable) continue;

                if (line.StartsWith("PRECIPITATION FREQUENCY ESTIMATES AT", StringComparison.OrdinalIgnoreCase))
                    break;

                if (!TryParseDurationRow(line, 0, out double rowDurationMin, out _))
                    continue;

                if (Math.Abs(rowDurationMin - durationMin) > 0.001)
                    continue;

                var intensities = new Dictionary<int, double>(periods.Count);
                foreach (int returnPeriod in periods)
                {
                    int column = ReturnPeriodColumnIndex(returnPeriod);
                    if (!TryParseDurationRow(line, column, out _, out double intensityInHr))
                        continue;
                    intensities[returnPeriod] = intensityInHr;
                }

                if (intensities.Count == 0)
                {
                    throw new InvalidDataException(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "NOAA PFDS table did not contain intensities at {0:0.#} min.",
                            durationMin));
                }

                return intensities;
            }

            throw new InvalidDataException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "NOAA PFDS table did not contain a row for {0:0.#} min duration.",
                    durationMin));
        }

        /// <summary>Fit IDF coefficients for each requested return period from one PFDS CSV.</summary>
        public static IReadOnlyDictionary<int, Atlas14CacheEntry> ParseAndFitAll(
            string csv,
            double lat,
            double lon,
            IReadOnlyList<int>? returnPeriodYears = null)
        {
            IReadOnlyList<int> periods = returnPeriodYears ?? StandardReturnPeriods;
            var entries = new Dictionary<int, Atlas14CacheEntry>(periods.Count);
            foreach (int returnPeriod in periods)
                entries[returnPeriod] = ParseAndFit(csv, lat, lon, returnPeriod);
            return entries;
        }

        public static string FormatMultiReturnPeriodIntensities(
            IReadOnlyDictionary<int, double> intensitiesInHr,
            IReadOnlyList<int>? returnPeriodYears = null)
        {
            IReadOnlyList<int> periods = returnPeriodYears ?? StandardReturnPeriods;
            var parts = new List<string>(periods.Count);
            foreach (int returnPeriod in periods)
            {
                if (!intensitiesInHr.TryGetValue(returnPeriod, out double intensityInHr))
                    continue;
                parts.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}y={1:0.00}",
                    returnPeriod,
                    intensityInHr));
            }

            return string.Join(" ", parts);
        }

        private static int ReturnPeriodColumnIndex(int returnPeriodYears)
        {
            for (int i = 0; i < SupportedReturnPeriods.Length; i++)
            {
                if (SupportedReturnPeriods[i] == returnPeriodYears) return i;
            }
            throw new ArgumentOutOfRangeException(
                nameof(returnPeriodYears),
                "Return period must be one of 1,2,5,10,25,50,100,200,500,1000.");
        }

        private static bool TryEnterIntensityTable(string line, ref bool inMainTable)
        {
            if (line.StartsWith("PRECIPITATION FREQUENCY ESTIMATES AT", StringComparison.OrdinalIgnoreCase))
                return false;

            if (line.StartsWith("by duration for ARI", StringComparison.OrdinalIgnoreCase))
            {
                inMainTable = true;
                return true;
            }

            if (!line.StartsWith("PRECIPITATION FREQUENCY ESTIMATES", StringComparison.OrdinalIgnoreCase))
                return false;

            if (line.IndexOf("by duration for ARI", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                inMainTable = true;
                return true;
            }

            // Live PFDS may split the header across two lines; the next row begins the table.
            inMainTable = false;
            return true;
        }

        public static bool TryParseDurationRow(
            string line, int ariColumnIndex, out double durationMin, out double intensityInHr)
        {
            durationMin = 0.0;
            intensityInHr = 0.0;

            int colon = line.IndexOf(':');
            if (colon <= 0) return false;

            string durationToken = line.Substring(0, colon).Trim();
            if (!TryParseDurationMinutes(durationToken, out durationMin))
                return false;

            string[] parts = line.Substring(colon + 1).Split(',');
            var values = new List<string>(parts.Length);
            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                if (trimmed.Length > 0)
                    values.Add(trimmed);
            }

            if (values.Count <= ariColumnIndex) return false;

            return double.TryParse(
                values[ariColumnIndex],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out intensityInHr);
        }

        public static bool TryParseDurationMinutes(string token, out double minutes)
        {
            minutes = 0.0;
            token = token.Trim();
            if (token.EndsWith("-min", StringComparison.OrdinalIgnoreCase))
            {
                string num = token.Substring(0, token.Length - 4);
                return double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out minutes);
            }

            if (token.EndsWith("-hr", StringComparison.OrdinalIgnoreCase))
            {
                string num = token.Substring(0, token.Length - 3);
                if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out double hours))
                    return false;
                minutes = hours * 60.0;
                return true;
            }

            if (token.EndsWith("-day", StringComparison.OrdinalIgnoreCase))
            {
                string num = token.Substring(0, token.Length - 4);
                if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out double days))
                    return false;
                minutes = days * 24.0 * 60.0;
                return true;
            }

            return false;
        }

        private static IEnumerable<string> SplitLines(string text)
        {
            using (var reader = new StringReader(text))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                    yield return line;
            }
        }
    }

    public enum Atlas14Source
    {
        Live,
        Cache,
        Embedded,
    }

    /// <summary>Resolved Atlas 14 IDF coefficients and provenance.</summary>
    public sealed class Atlas14Resolution
    {
        public Atlas14Resolution(
            Atlas14Source source,
            double lat,
            double lon,
            int returnPeriodYears,
            double a,
            double b,
            double c,
            string displayLabel,
            string? presetKey = null,
            string? projectArea = null,
            DateTime? fetchedUtc = null)
        {
            Source = source;
            Lat = lat;
            Lon = lon;
            ReturnPeriodYears = returnPeriodYears;
            A = a;
            B = b;
            C = c;
            DisplayLabel = displayLabel ?? throw new ArgumentNullException(nameof(displayLabel));
            PresetKey = presetKey;
            ProjectArea = projectArea;
            FetchedUtc = fetchedUtc;
        }

        public Atlas14Source Source { get; }
        public double Lat { get; }
        public double Lon { get; }
        public int ReturnPeriodYears { get; }
        public double A { get; }
        public double B { get; }
        public double C { get; }
        public string DisplayLabel { get; }
        public string? PresetKey { get; }
        public string? ProjectArea { get; }
        public DateTime? FetchedUtc { get; }

        public IdfCurve ToCurve() => new IdfCurve(A, B, C);

        public string SourceLabel =>
            Source == Atlas14Source.Live ? "live" :
            Source == Atlas14Source.Cache ? "cached live" :
            "embedded";

        public static Atlas14Resolution FromPreset(
            Atlas14Presets.Preset preset, int returnPeriodYears = 10)
        {
            IdfCurve curve = preset.ToCurve(returnPeriodYears);
            return new Atlas14Resolution(
                Atlas14Source.Embedded,
                preset.Lat,
                preset.Lon,
                returnPeriodYears,
                curve.A,
                curve.B,
                curve.C,
                preset.DisplayName,
                preset.Key);
        }

        public static Atlas14Resolution EmbeddedNearest(double lat, double lon, int returnPeriodYears = 10)
        {
            Atlas14Presets.Preset preset = Atlas14Presets.Nearest(lat, lon);
            return FromPreset(preset, returnPeriodYears);
        }

        public Rational.PeakFlowResult PeakFromCatchments(IReadOnlyList<Catchment> catchments)
        {
            if (catchments == null) throw new ArgumentNullException(nameof(catchments));
            if (catchments.Count == 0)
                throw new ArgumentException("At least one catchment is required.", nameof(catchments));

            double systemTc = 0.0;
            foreach (Catchment cm in catchments)
                systemTc = Math.Max(systemTc, cm.TcMinutes);

            var idf = ToCurve();
            var intensity = idf.Intensity(systemTc);
            var peak = Rational.Peak(catchments, intensity.IntensityInHr);
            peak.Steps.Insert(0, new CalcStep("IDF_source", 0, "",
                $"{SourceLabel} — {DisplayLabel}"));
            peak.Steps.Insert(1, new CalcStep("IDF_return", ReturnPeriodYears, "yr",
                $"{ReturnPeriodYears}-yr Atlas 14"));
            foreach (CalcStep step in intensity.Steps)
                peak.Steps.Insert(2, step);
            return peak;
        }
    }

    public readonly struct DurationIntensityPoint
    {
        public DurationIntensityPoint(double durationMin, double intensityInHr)
        {
            DurationMin = durationMin;
            IntensityInHr = intensityInHr;
        }

        public double DurationMin { get; }
        public double IntensityInHr { get; }
    }

    internal readonly struct IdfFit
    {
        public IdfFit(double a, double b, double c)
        {
            A = a;
            B = b;
            C = c;
        }

        public double A { get; }
        public double B { get; }
        public double C { get; }
    }

    internal static class IdfCurveFitter
    {
        private static readonly double[] FitDurationsMin = { 5.0, 10.0, 15.0, 30.0, 60.0, 120.0 };

        public static IdfFit Fit(IReadOnlyList<DurationIntensityPoint> table)
        {
            var fitPoints = SelectFitPoints(table);
            if (fitPoints.Count < 3)
                throw new InvalidDataException("Not enough NOAA duration points to fit an IDF curve.");

            IdfFit best = default;
            double bestError = double.MaxValue;

            for (double b = 4.0; b <= 20.0; b += 0.25)
            {
                if (!TryFitForB(fitPoints, b, out IdfFit candidate))
                    continue;

                double err = SumSquaredRelativeError(fitPoints, candidate);
                if (err < bestError)
                {
                    bestError = err;
                    best = candidate;
                }
            }

            if (bestError == double.MaxValue)
                throw new InvalidDataException("IDF curve fit did not converge.");

            return best;
        }

        private static List<DurationIntensityPoint> SelectFitPoints(
            IReadOnlyList<DurationIntensityPoint> table)
        {
            var byDuration = table.ToDictionary(p => p.DurationMin, p => p.IntensityInHr);
            var selected = new List<DurationIntensityPoint>();
            foreach (double duration in FitDurationsMin)
            {
                if (!byDuration.TryGetValue(duration, out double intensity)) continue;
                selected.Add(new DurationIntensityPoint(duration, intensity));
            }

            if (selected.Count >= 3) return selected;
            return table
                .Where(p => p.DurationMin <= 180.0)
                .OrderBy(p => p.DurationMin)
                .Take(6)
                .ToList();
        }

        private static bool TryFitForB(
            IReadOnlyList<DurationIntensityPoint> points, double b, out IdfFit fit)
        {
            fit = default;
            double sumX = 0.0;
            double sumY = 0.0;
            double sumXX = 0.0;
            double sumXY = 0.0;
            int n = 0;

            foreach (DurationIntensityPoint point in points)
            {
                double x = Math.Log(point.DurationMin + b);
                double y = Math.Log(point.IntensityInHr);
                sumX += x;
                sumY += y;
                sumXX += x * x;
                sumXY += x * y;
                n++;
            }

            if (n < 2) return false;

            double denom = n * sumXX - sumX * sumX;
            if (Math.Abs(denom) < 1e-12) return false;

            double c = -(n * sumXY - sumX * sumY) / denom;
            double logA = (sumY + c * sumX) / n;
            double a = Math.Exp(logA);

            if (double.IsNaN(a) || double.IsInfinity(a) || a <= 0.0) return false;
            if (double.IsNaN(c) || double.IsInfinity(c) || c <= 0.0) return false;

            fit = new IdfFit(a, b, c);
            return true;
        }

        private static double SumSquaredRelativeError(
            IReadOnlyList<DurationIntensityPoint> points, IdfFit fit)
        {
            double sum = 0.0;
            foreach (DurationIntensityPoint point in points)
            {
                double model = fit.A / Math.Pow(point.DurationMin + fit.B, fit.C);
                double rel = (model - point.IntensityInHr) / point.IntensityInHr;
                sum += rel * rel;
            }
            return sum;
        }
    }

    public sealed class Atlas14CacheEntry
    {
        public Atlas14CacheEntry(
            double lat,
            double lon,
            int returnPeriodYears,
            double a,
            double b,
            double c,
            string? projectArea,
            DateTime fetchedUtc,
            DateTime expiresUtc)
        {
            Lat = lat;
            Lon = lon;
            ReturnPeriodYears = returnPeriodYears;
            A = a;
            B = b;
            C = c;
            ProjectArea = projectArea;
            FetchedUtc = fetchedUtc;
            ExpiresUtc = expiresUtc;
        }

        public double Lat { get; }
        public double Lon { get; }
        public int ReturnPeriodYears { get; }
        public double A { get; }
        public double B { get; }
        public double C { get; }
        public string? ProjectArea { get; }
        public DateTime FetchedUtc { get; }
        public DateTime ExpiresUtc { get; }

        public bool IsExpired(DateTime utcNow) => utcNow >= ExpiresUtc;

        public Atlas14Resolution ToResolution(Atlas14Source source)
        {
            string label = string.Format(
                CultureInfo.InvariantCulture,
                "NOAA Atlas 14 @ {0:0.####}, {1:0.####}",
                Lat,
                Lon);
            return new Atlas14Resolution(
                source,
                Lat,
                Lon,
                ReturnPeriodYears,
                A,
                B,
                C,
                label,
                projectArea: ProjectArea,
                fetchedUtc: FetchedUtc);
        }

        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append('{');
            AppendJsonNumber(sb, "lat", Lat); sb.Append(',');
            AppendJsonNumber(sb, "lon", Lon); sb.Append(',');
            AppendJsonInt(sb, "returnPeriodYears", ReturnPeriodYears); sb.Append(',');
            AppendJsonNumber(sb, "a", A); sb.Append(',');
            AppendJsonNumber(sb, "b", B); sb.Append(',');
            AppendJsonNumber(sb, "c", C); sb.Append(',');
            AppendJsonString(sb, "projectArea", ProjectArea); sb.Append(',');
            AppendJsonDate(sb, "fetchedUtc", FetchedUtc); sb.Append(',');
            AppendJsonDate(sb, "expiresUtc", ExpiresUtc);
            sb.Append('}');
            return sb.ToString();
        }

        public static Atlas14CacheEntry FromJson(string json)
        {
            return new Atlas14CacheEntry(
                ReadDouble(json, "lat"),
                ReadDouble(json, "lon"),
                ReadInt(json, "returnPeriodYears"),
                ReadDouble(json, "a"),
                ReadDouble(json, "b"),
                ReadDouble(json, "c"),
                ReadString(json, "projectArea"),
                ReadDate(json, "fetchedUtc"),
                ReadDate(json, "expiresUtc"));
        }

        private static void AppendJsonNumber(StringBuilder sb, string name, double value)
        {
            sb.Append('"').Append(name).Append("\":");
            sb.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void AppendJsonInt(StringBuilder sb, string name, int value)
        {
            sb.Append('"').Append(name).Append("\":");
            sb.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendJsonString(StringBuilder sb, string name, string? value)
        {
            sb.Append('"').Append(name).Append("\":");
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            sb.Append('"');
            foreach (char ch in value)
            {
                if (ch == '\\' || ch == '"') sb.Append('\\');
                sb.Append(ch);
            }
            sb.Append('"');
        }

        private static void AppendJsonDate(StringBuilder sb, string name, DateTime value)
        {
            sb.Append('"').Append(name).Append("\":\"");
            sb.Append(value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
            sb.Append('"');
        }

        private static double ReadDouble(string json, string name)
        {
            string raw = ReadRawValue(json, name);
            return double.Parse(raw, CultureInfo.InvariantCulture);
        }

        private static int ReadInt(string json, string name)
        {
            string raw = ReadRawValue(json, name);
            return int.Parse(raw, CultureInfo.InvariantCulture);
        }

        private static string? ReadString(string json, string name)
        {
            string raw = ReadRawValue(json, name);
            if (string.Equals(raw, "null", StringComparison.OrdinalIgnoreCase)) return null;
            if (raw.Length >= 2 && raw[0] == '"' && raw[raw.Length - 1] == '"')
                return raw.Substring(1, raw.Length - 2);
            return raw;
        }

        private static DateTime ReadDate(string json, string name)
        {
            string? text = ReadString(json, name);
            return DateTime.Parse(text!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }

        private static string ReadRawValue(string json, string name)
        {
            string marker = "\"" + name + "\":";
            int start = json.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) throw new InvalidDataException("Missing JSON field: " + name);
            start += marker.Length;

            while (start < json.Length && char.IsWhiteSpace(json[start])) start++;

            if (start < json.Length && json[start] == '"')
            {
                int end = start + 1;
                while (end < json.Length)
                {
                    if (json[end] == '"' && json[end - 1] != '\\') break;
                    end++;
                }
                return json.Substring(start, end - start + 1);
            }

            int comma = json.IndexOf(',', start);
            if (comma < 0) comma = json.IndexOf('}', start);
            if (comma < 0) throw new InvalidDataException("Malformed JSON near field: " + name);
            return json.Substring(start, comma - start).Trim();
        }
    }
}