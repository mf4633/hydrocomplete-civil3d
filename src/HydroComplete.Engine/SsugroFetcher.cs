using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HydroComplete.Engine
{
    /// <summary>Source of soil map unit data for a lat/lon query.</summary>
    public enum SsugroSource
    {
        Live,
        Cache,
        RegionalFallback,
        Embedded,
    }

    /// <summary>Surface horizon (hzdept_r = 0) physical properties from SSURGO.</summary>
    public sealed class SsugroSurfaceHorizon
    {
        public double? PctSand { get; set; }
        public double? PctSilt { get; set; }
        public double? PctClay { get; set; }
        public double? PctSandVcs { get; set; }
        public double? PctSandCs { get; set; }
        public double? PctSandMs { get; set; }
        public double? PctSandFs { get; set; }
        public double? PctSandVfs { get; set; }
        public double? KFactor { get; set; }
        public double? OrganicMatter { get; set; }
        public double? BulkDensity { get; set; }
    }

    /// <summary>Aggregated SSURGO map unit at a point (dominant component drives HSG/PSD/K).</summary>
    public sealed class SsugroMapUnit
    {
        public string? Mukey { get; set; }
        public string Muname { get; set; } = "";
        public string? DominantComponent { get; set; }
        public double? DominantPct { get; set; }
        public char? HydrologicSoilGroup { get; set; }
        public string? DominantTexture { get; set; }
        public SsugroSurfaceHorizon? SurfaceHorizon { get; set; }
        public bool IsFallback { get; set; }
        public string? Warning { get; set; }
    }

    /// <summary>Resolved soil data for a geographic point.</summary>
    public sealed class SsugroResolution
    {
        public SsugroSource Source { get; set; }
        public double Lat { get; set; }
        public double Lon { get; set; }
        public string DisplayLabel { get; set; } = "";
        public DateTime? FetchedUtc { get; set; }
        public SsugroMapUnit MapUnit { get; set; } = new SsugroMapUnit();

        public SoilDatabase.SoilProperties ToSoilProperties()
        {
            char hsg = MapUnit.HydrologicSoilGroup ?? 'B';
            double k = MapUnit.SurfaceHorizon?.KFactor ?? 0.30;
            string texture = MapUnit.DominantTexture ?? InferTexture(MapUnit.SurfaceHorizon);
            return new SoilDatabase.SoilProperties
            {
                Key = NormalizeSoilKey(MapUnit.Muname),
                Name = MapUnit.Muname,
                Series = MapUnit.DominantComponent ?? MapUnit.Muname,
                Region = Source == SsugroSource.Live || Source == SsugroSource.Cache
                    ? $"SSURGO @ {Lat:0.####}, {Lon:0.####}"
                    : "Regional estimate",
                Texture = texture,
                HydrologicSoilGroup = hsg,
                KFactor = k,
                InfiltrationRateInPerHr = InfiltrationForHsg(hsg),
                Drainage = DrainageForHsg(hsg),
            };
        }

        public static SsugroResolution RegionalFallback(double lat, double lon)
        {
            SsugroMapUnit unit = SsugroRegionalFallback.Nearest(lat, lon);
            unit.IsFallback = true;
            return new SsugroResolution
            {
                Source = SsugroSource.RegionalFallback,
                Lat = lat,
                Lon = lon,
                DisplayLabel = unit.Muname,
                MapUnit = unit,
            };
        }

        public static SsugroResolution Embedded(string soilName, double lat, double lon)
        {
            SoilDatabase.SoilProperties soil = SoilDatabase.Lookup(soilName);
            return new SsugroResolution
            {
                Source = SsugroSource.Embedded,
                Lat = lat,
                Lon = lon,
                DisplayLabel = soil.Name,
                MapUnit = new SsugroMapUnit
                {
                    Muname = soil.Name,
                    DominantComponent = soil.Series,
                    HydrologicSoilGroup = soil.HydrologicSoilGroup,
                    DominantTexture = soil.Texture,
                    SurfaceHorizon = new SsugroSurfaceHorizon { KFactor = soil.KFactor },
                    IsFallback = true,
                    Warning = "Embedded soil table — not live SSURGO.",
                },
            };
        }

        private static string NormalizeSoilKey(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "unknown";
            return name.Trim().ToLowerInvariant().Replace(' ', '-');
        }

        private static string InferTexture(SsugroSurfaceHorizon? hz)
        {
            if (hz?.PctSand == null || hz.PctSilt == null || hz.PctClay == null)
                return "loam";
            double sand = hz.PctSand.Value;
            double silt = hz.PctSilt.Value;
            double clay = hz.PctClay.Value;
            if (clay >= 40) return "clay";
            if (sand >= 70) return "sand";
            if (silt >= 50) return "silt loam";
            return "loam";
        }

        private static double InfiltrationForHsg(char hsg) => hsg switch
        {
            'A' => 0.60,
            'B' => 0.25,
            'C' => 0.10,
            'D' => 0.03,
            _ => 0.25,
        };

        private static string DrainageForHsg(char hsg) => hsg switch
        {
            'A' => "well drained",
            'B' => "moderately well drained",
            'C' => "somewhat poorly drained",
            'D' => "poorly drained",
            _ => "unknown",
        };
    }

    /// <summary>
    /// Fetches USDA NRCS Soil Data Access (SSURGO) map unit data for a lat/lon,
    /// caches results locally, and falls back to regional templates when offline.
    /// </summary>
    public sealed class SsugroFetcher
    {
        public const string DefaultSdaEndpoint =
            "https://sdmdataaccess.nrcs.usda.gov/Tabular/SDMTabularService/post.rest";

        public static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromDays(30);

        private const string ComponentQueryTemplate = @"
  SELECT mapunit.mukey, mapunit.muname,
         component.compname, component.comppct_r, component.hydgrp,
         chorizon.hzdept_r, chorizon.hzdepb_r,
         chorizon.sandtotal_r, chorizon.silttotal_r, chorizon.claytotal_r,
         chorizon.sandvc_r, chorizon.sandco_r, chorizon.sandmed_r,
         chorizon.sandfine_r, chorizon.sandvf_r,
         chorizon.kwfact, chorizon.kffact, chorizon.om_r, chorizon.dbthirdbar_r,
         chtexturegrp.texture
  FROM mapunit
  INNER JOIN component ON component.mukey = mapunit.mukey
  LEFT OUTER JOIN chorizon ON chorizon.cokey = component.cokey AND chorizon.hzdept_r = 0
  LEFT OUTER JOIN chtexturegrp ON chtexturegrp.chkey = chorizon.chkey AND chtexturegrp.rvindicator = 'Yes'
  WHERE component.majcompflag = 'Yes'
  AND mapunit.mukey IN (
    SELECT * FROM SDA_Get_Mukey_from_intersection_with_WktWgs84('__WKT__')
  )
  ORDER BY mapunit.mukey, component.comppct_r DESC";

        private static readonly HttpClient SharedHttp = CreateHttpClient();

        private readonly string _sdaEndpoint;
        private readonly string? _cacheDirectory;
        private readonly TimeSpan _cacheTtl;
        private readonly Func<HttpClient> _httpClientFactory;

        public SsugroFetcher(
            string? cacheDirectory = null,
            TimeSpan? cacheTtl = null,
            string? sdaEndpoint = null,
            Func<HttpClient>? httpClientFactory = null)
        {
            _cacheDirectory = cacheDirectory;
            _cacheTtl = cacheTtl ?? DefaultCacheTtl;
            _sdaEndpoint = string.IsNullOrWhiteSpace(sdaEndpoint) ? DefaultSdaEndpoint : sdaEndpoint!.Trim();
            _httpClientFactory = httpClientFactory ?? (() => SharedHttp);
        }

        public SsugroResolution Resolve(double lat, double lon, Func<SsugroResolution>? fallback = null)
        {
            return ResolveAsync(lat, lon, fallback, CancellationToken.None).GetAwaiter().GetResult();
        }

        public async Task<SsugroResolution> ResolveAsync(
            double lat,
            double lon,
            Func<SsugroResolution>? fallback = null,
            CancellationToken cancellationToken = default)
        {
            ValidateCoordinates(lat, lon);

            SsugroCacheEntry? cached = TryReadCache(lat, lon);
            if (cached != null && !cached.IsExpired(DateTime.UtcNow, _cacheTtl))
                return cached.ToResolution(SsugroSource.Cache);

            try
            {
                string json = await DownloadSdaAsync(lat, lon, cancellationToken).ConfigureAwait(false);
                List<Dictionary<string, string?>> rows = ParseSdaTable(json);
                List<SsugroMapUnit> units = AggregateComponents(rows);
                if (units.Count == 0)
                {
                    SsugroResolution regional = SsugroResolution.RegionalFallback(lat, lon);
                    regional.MapUnit.Warning =
                        "SSURGO has no coverage at this point. Using regional defaults — verify before use.";
                    return regional;
                }

                var entry = SsugroCacheEntry.FromMapUnit(lat, lon, units[0]);
                WriteCache(entry);
                return entry.ToResolution(SsugroSource.Live);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (cached != null)
                    return cached.ToResolution(SsugroSource.Cache);

                if (fallback != null)
                    return fallback();

                return SsugroResolution.RegionalFallback(lat, lon);
            }
        }

        public static void ValidateCoordinates(double lat, double lon)
        {
            if (lat < -90 || lat > 90)
                throw new ArgumentOutOfRangeException(nameof(lat), "Latitude must be between -90 and 90.");
            if (lon < -180 || lon > 180)
                throw new ArgumentOutOfRangeException(nameof(lon), "Longitude must be between -180 and 180.");
        }

        public static List<SsugroMapUnit> AggregateComponents(IReadOnlyList<Dictionary<string, string?>> rows)
        {
            var byMukey = new Dictionary<string, (string Muname, List<ComponentRow> Components)>(StringComparer.Ordinal);

            foreach (Dictionary<string, string?> row in rows)
            {
                string? mukey = Get(row, "mukey");
                if (string.IsNullOrWhiteSpace(mukey)) continue;

                string mapUnitKey = mukey!;
                if (!byMukey.TryGetValue(mapUnitKey, out var bucket))
                {
                    bucket = (Get(row, "muname") ?? mapUnitKey, new List<ComponentRow>());
                    byMukey[mapUnitKey] = bucket;
                }

                bucket.Components.Add(new ComponentRow
                {
                    Name = Get(row, "compname") ?? "",
                    Pct = ParseDouble(Get(row, "comppct_r")) ?? 0,
                    Hsg = ParseHsg(Get(row, "hydgrp")),
                    Texture = Get(row, "texture"),
                    Surface = new SsugroSurfaceHorizon
                    {
                        PctSand = ParseDouble(Get(row, "sandtotal_r")),
                        PctSilt = ParseDouble(Get(row, "silttotal_r")),
                        PctClay = ParseDouble(Get(row, "claytotal_r")),
                        PctSandVcs = ParseDouble(Get(row, "sandvc_r")),
                        PctSandCs = ParseDouble(Get(row, "sandco_r")),
                        PctSandMs = ParseDouble(Get(row, "sandmed_r")),
                        PctSandFs = ParseDouble(Get(row, "sandfine_r")),
                        PctSandVfs = ParseDouble(Get(row, "sandvf_r")),
                        KFactor = ParseDouble(Get(row, "kwfact")) ?? ParseDouble(Get(row, "kffact")),
                        OrganicMatter = ParseDouble(Get(row, "om_r")),
                        BulkDensity = ParseDouble(Get(row, "dbthirdbar_r")),
                    },
                });
            }

            var results = new List<SsugroMapUnit>();
            foreach (KeyValuePair<string, (string Muname, List<ComponentRow> Components)> pair in byMukey)
            {
                List<ComponentRow> comps = pair.Value.Components.OrderByDescending(c => c.Pct).ToList();
                ComponentRow dominant = comps[0];
                results.Add(new SsugroMapUnit
                {
                    Mukey = pair.Key,
                    Muname = pair.Value.Muname,
                    DominantComponent = dominant.Name,
                    DominantPct = dominant.Pct,
                    HydrologicSoilGroup = dominant.Hsg,
                    DominantTexture = dominant.Texture,
                    SurfaceHorizon = dominant.Surface,
                });
            }

            results.Sort((a, b) => (b.DominantPct ?? 0).CompareTo(a.DominantPct ?? 0));
            return results;
        }

        public static List<Dictionary<string, string?>> ParseSdaTable(string json)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Table", out JsonElement table) ||
                table.ValueKind != JsonValueKind.Array ||
                table.GetArrayLength() < 2)
            {
                return new List<Dictionary<string, string?>>();
            }

            var columns = new List<string>();
            foreach (JsonElement col in table[0].EnumerateArray())
                columns.Add(col.GetString() ?? "");

            var rows = new List<Dictionary<string, string?>>();
            for (int i = 1; i < table.GetArrayLength(); i++)
            {
                JsonElement rowEl = table[i];
                if (rowEl.ValueKind != JsonValueKind.Array) continue;

                var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                int colIndex = 0;
                foreach (JsonElement cell in rowEl.EnumerateArray())
                {
                    if (colIndex >= columns.Count) break;
                    row[columns[colIndex]] = cell.ValueKind == JsonValueKind.Null
                        ? null
                        : cell.ToString();
                    colIndex++;
                }

                rows.Add(row);
            }

            return rows;
        }

        private async Task<string> DownloadSdaAsync(double lat, double lon, CancellationToken cancellationToken)
        {
            string wkt = string.Format(CultureInfo.InvariantCulture, "point({0} {1})", lon, lat);
            string query = ComponentQueryTemplate.Replace("__WKT__", wkt);
            string body = JsonSerializer.Serialize(new { format = "JSON+COLUMNNAME", query });

            using var request = new HttpRequestMessage(HttpMethod.Post, _sdaEndpoint)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };

            HttpClient client = _httpClientFactory();
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 16L * 1024 * 1024)
                throw new InvalidOperationException("SSURGO response exceeds the 16 MB cap.");
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }

        private SsugroCacheEntry? TryReadCache(double lat, double lon)
        {
            if (string.IsNullOrWhiteSpace(_cacheDirectory)) return null;
            string path = CachePath(lat, lon);
            if (!File.Exists(path)) return null;
            try
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<SsugroCacheEntry>(json);
            }
            catch
            {
                return null;
            }
        }

        private void WriteCache(SsugroCacheEntry entry)
        {
            if (string.IsNullOrWhiteSpace(_cacheDirectory)) return;
            Directory.CreateDirectory(_cacheDirectory);
            string json = JsonSerializer.Serialize(entry);
            File.WriteAllText(CachePath(entry.Lat, entry.Lon), json);
        }

        private string CachePath(double lat, double lon)
        {
            string key = string.Format(CultureInfo.InvariantCulture, "{0:0.####}_{1:0.####}", lat, lon);
            return Path.Combine(_cacheDirectory!, key + ".json");
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("HydroComplete-Civil3D/1.3");
            return client;
        }

        private static string? Get(Dictionary<string, string?> row, string key) =>
            row.TryGetValue(key, out string? value) ? value : null;

        private static double? ParseDouble(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                ? v
                : (double?)null;
        }

        private static char? ParseHsg(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string trimmed = text!.Trim();
            if (trimmed.Length == 0) return null;
            char c = char.ToUpperInvariant(trimmed[0]);
            return c is 'A' or 'B' or 'C' or 'D' ? c : (char?)null;
        }

        private sealed class ComponentRow
        {
            public string Name { get; set; } = "";
            public double Pct { get; set; }
            public char? Hsg { get; set; }
            public string? Texture { get; set; }
            public SsugroSurfaceHorizon Surface { get; set; } = new SsugroSurfaceHorizon();
        }

        private sealed class SsugroCacheEntry
        {
            public double Lat { get; set; }
            public double Lon { get; set; }
            public DateTime FetchedUtc { get; set; }
            public SsugroMapUnit MapUnit { get; set; } = new SsugroMapUnit();

            public bool IsExpired(DateTime now, TimeSpan ttl) => now - FetchedUtc > ttl;

            public SsugroResolution ToResolution(SsugroSource source) => new SsugroResolution
            {
                Source = source,
                Lat = Lat,
                Lon = Lon,
                DisplayLabel = MapUnit.Muname,
                FetchedUtc = FetchedUtc,
                MapUnit = MapUnit,
            };

            public static SsugroCacheEntry FromMapUnit(double lat, double lon, SsugroMapUnit unit) =>
                new SsugroCacheEntry
                {
                    Lat = lat,
                    Lon = lon,
                    FetchedUtc = DateTime.UtcNow,
                    MapUnit = unit,
                };
        }
    }

    /// <summary>Regional SSURGO fallback templates when SDA is unreachable or has no data.</summary>
    internal static class SsugroRegionalFallback
    {
        private sealed class RegionTemplate
        {
            public double LatMin { get; set; }
            public double LatMax { get; set; }
            public double LonMin { get; set; }
            public double LonMax { get; set; }
            public string Name { get; set; } = "";
            public char Hsg { get; set; }
            public double Sand { get; set; }
            public double Silt { get; set; }
            public double Clay { get; set; }
            public double K { get; set; }
            public double Om { get; set; }
            public double Bd { get; set; }
        }

        private static readonly RegionTemplate[] Regions =
        {
            new RegionTemplate { LatMin = 24, LatMax = 31, LonMin = -90, LonMax = -78, Name = "Florida/Gulf Coast", Hsg = 'A', Sand = 75, Silt = 15, Clay = 10, K = 0.15, Om = 1.0, Bd = 1.55 },
            new RegionTemplate { LatMin = 31, LatMax = 36, LonMin = -82, LonMax = -75, Name = "Southeast Coast", Hsg = 'B', Sand = 55, Silt = 25, Clay = 20, K = 0.22, Om = 1.5, Bd = 1.50 },
            new RegionTemplate { LatMin = 31, LatMax = 36, LonMin = -90, LonMax = -82, Name = "Southeast Inland", Hsg = 'B', Sand = 45, Silt = 30, Clay = 25, K = 0.28, Om = 2.0, Bd = 1.45 },
            new RegionTemplate { LatMin = 35, LatMax = 41, LonMin = -85, LonMax = -75, Name = "Piedmont/Mid-Atlantic", Hsg = 'B', Sand = 35, Silt = 35, Clay = 30, K = 0.32, Om = 2.0, Bd = 1.45 },
            new RegionTemplate { LatMin = 41, LatMax = 47, LonMin = -80, LonMax = -67, Name = "Northeast", Hsg = 'C', Sand = 40, Silt = 40, Clay = 20, K = 0.30, Om = 3.0, Bd = 1.40 },
            new RegionTemplate { LatMin = 36, LatMax = 42, LonMin = -90, LonMax = -80, Name = "Ohio Valley", Hsg = 'C', Sand = 25, Silt = 50, Clay = 25, K = 0.35, Om = 2.5, Bd = 1.40 },
            new RegionTemplate { LatMin = 42, LatMax = 49, LonMin = -97, LonMax = -82, Name = "Upper Midwest", Hsg = 'B', Sand = 30, Silt = 45, Clay = 25, K = 0.32, Om = 3.5, Bd = 1.40 },
            new RegionTemplate { LatMin = 28, LatMax = 36, LonMin = -100, LonMax = -90, Name = "South Central", Hsg = 'C', Sand = 30, Silt = 35, Clay = 35, K = 0.30, Om = 1.5, Bd = 1.45 },
            new RegionTemplate { LatMin = 42, LatMax = 49, LonMin = -105, LonMax = -97, Name = "Northern Plains", Hsg = 'B', Sand = 40, Silt = 40, Clay = 20, K = 0.28, Om = 2.5, Bd = 1.42 },
            new RegionTemplate { LatMin = 32, LatMax = 42, LonMin = -105, LonMax = -97, Name = "Southern Plains", Hsg = 'C', Sand = 35, Silt = 35, Clay = 30, K = 0.30, Om = 1.5, Bd = 1.45 },
            new RegionTemplate { LatMin = 26, LatMax = 32, LonMin = -98, LonMax = -92, Name = "Texas Coast", Hsg = 'D', Sand = 20, Silt = 40, Clay = 40, K = 0.32, Om = 1.5, Bd = 1.40 },
            new RegionTemplate { LatMin = 32, LatMax = 49, LonMin = -114, LonMax = -105, Name = "Rockies", Hsg = 'B', Sand = 50, Silt = 30, Clay = 20, K = 0.24, Om = 2.0, Bd = 1.45 },
            new RegionTemplate { LatMin = 28, LatMax = 38, LonMin = -118, LonMax = -109, Name = "Southwest", Hsg = 'A', Sand = 70, Silt = 20, Clay = 10, K = 0.18, Om = 0.5, Bd = 1.55 },
            new RegionTemplate { LatMin = 36, LatMax = 44, LonMin = -120, LonMax = -113, Name = "Great Basin", Hsg = 'B', Sand = 55, Silt = 30, Clay = 15, K = 0.22, Om = 1.0, Bd = 1.50 },
            new RegionTemplate { LatMin = 42, LatMax = 49, LonMin = -125, LonMax = -118, Name = "Pacific Northwest", Hsg = 'B', Sand = 45, Silt = 35, Clay = 20, K = 0.28, Om = 3.0, Bd = 1.40 },
            new RegionTemplate { LatMin = 32, LatMax = 42, LonMin = -125, LonMax = -119, Name = "California Coast", Hsg = 'C', Sand = 40, Silt = 35, Clay = 25, K = 0.30, Om = 2.0, Bd = 1.45 },
            new RegionTemplate { LatMin = 32, LatMax = 42, LonMin = -119, LonMax = -114, Name = "California Inland", Hsg = 'B', Sand = 50, Silt = 30, Clay = 20, K = 0.26, Om = 1.5, Bd = 1.48 },
        };

        public static SsugroMapUnit Nearest(double lat, double lon)
        {
            RegionTemplate? match = null;
            foreach (RegionTemplate region in Regions)
            {
                if (lat >= region.LatMin && lat <= region.LatMax &&
                    lon >= region.LonMin && lon <= region.LonMax)
                {
                    match = region;
                    break;
                }
            }

            match ??= new RegionTemplate
            {
                Name = "Continental US (generic)",
                Hsg = 'B',
                Sand = 40,
                Silt = 40,
                Clay = 20,
                K = 0.30,
                Om = 2.0,
                Bd = 1.45,
            };

            return new SsugroMapUnit
            {
                Muname = $"{match.Name} regional default (not SSURGO-verified)",
                DominantComponent = $"{match.Name} representative",
                HydrologicSoilGroup = match.Hsg,
                IsFallback = true,
                SurfaceHorizon = new SsugroSurfaceHorizon
                {
                    PctSand = match.Sand,
                    PctSilt = match.Silt,
                    PctClay = match.Clay,
                    PctSandVcs = match.Sand * 0.10,
                    PctSandCs = match.Sand * 0.20,
                    PctSandMs = match.Sand * 0.30,
                    PctSandFs = match.Sand * 0.25,
                    PctSandVfs = match.Sand * 0.15,
                    KFactor = match.K,
                    OrganicMatter = match.Om,
                    BulkDensity = match.Bd,
                },
            };
        }
    }
}