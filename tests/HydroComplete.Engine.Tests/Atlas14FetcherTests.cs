using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class Atlas14FetcherTests
    {
        private static string FixturePath =>
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "charlotte_nc_intensity.csv");

        [Fact]
        public void ParseIntensityTable_CharlotteFixture_ReadsTenYearColumn()
        {
            string csv = File.ReadAllText(FixturePath);
            var table = Atlas14Fetcher.ParseIntensityTable(csv, returnPeriodYears: 10);

            Assert.True(table.Count >= 6);
            Assert.Contains(table, p => p.DurationMin == 10.0 && p.IntensityInHr == 5.81);
            Assert.Contains(table, p => p.DurationMin == 60.0 && p.IntensityInHr == 2.31);
        }

        [Fact]
        public void ParseAndFit_CharlotteFixture_ProducesReasonableCoefficients()
        {
            string csv = File.ReadAllText(FixturePath);
            Atlas14CacheEntry entry = Atlas14Fetcher.ParseAndFit(csv, 35.23, -80.84, 10);

            Assert.Equal("Ohio River Basin", entry.ProjectArea);
            Assert.True(entry.A > 0);
            Assert.True(entry.B > 0);
            Assert.True(entry.C > 0);

            double i10 = entry.A / Math.Pow(10.0 + entry.B, entry.C);
            Assert.InRange(i10, 5.0, 6.5);
        }

        [Fact]
        public void ParseAndFit_ErrorResponse_Throws()
        {
            const string csv = "result = 'none'; ErrorMsg = 'Error 3.0: Selected location is not within a project area';";
            Assert.Throws<InvalidDataException>(() =>
                Atlas14Fetcher.ParseAndFit(csv, 51.0, 0.0, 10));
        }

        [Fact]
        public void CacheEntry_RoundTripsJson()
        {
            var original = new Atlas14CacheEntry(
                35.23, -80.84, 10, 88.1, 9.25, 0.81,
                "Ohio River Basin",
                new DateTime(2026, 6, 17, 23, 29, 55, DateTimeKind.Utc),
                new DateTime(2026, 7, 17, 23, 29, 55, DateTimeKind.Utc));

            Atlas14CacheEntry restored = Atlas14CacheEntry.FromJson(original.ToJson());

            Assert.Equal(original.Lat, restored.Lat);
            Assert.Equal(original.Lon, restored.Lon);
            Assert.Equal(original.A, restored.A);
            Assert.Equal(original.B, restored.B);
            Assert.Equal(original.C, restored.C);
            Assert.Equal(original.ProjectArea, restored.ProjectArea);
        }

        [Fact]
        public void Resolve_UsesFreshCacheWithoutNetwork()
        {
            string cacheDir = CreateTempDir();
            try
            {
                string csv = File.ReadAllText(FixturePath);
                Atlas14CacheEntry entry = Atlas14Fetcher.ParseAndFit(csv, 35.23, -80.84, 10);
                string path = Atlas14Fetcher.CacheFilePath(cacheDir, 35.23, -80.84, 10);
                Directory.CreateDirectory(cacheDir);
                File.WriteAllText(path, entry.ToJson());

                var fetcher = new Atlas14Fetcher(
                    cacheDir,
                    httpClientFactory: () => throw new InvalidOperationException("network should not be used"));

                Atlas14Resolution result = fetcher.Resolve(35.23, -80.84, 10);

                Assert.Equal(Atlas14Source.Cache, result.Source);
                Assert.Equal("cached live", result.SourceLabel);
            }
            finally
            {
                TryDeleteDir(cacheDir);
            }
        }

        [Fact]
        public async Task ResolveAsync_DownloadsFixtureAndWritesCache()
        {
            string cacheDir = CreateTempDir();
            try
            {
                string csv = File.ReadAllText(FixturePath);
                var handler = new StubHttpHandler(csv);
                var client = new HttpClient(handler);

                var fetcher = new Atlas14Fetcher(
                    cacheDir,
                    pfdsUrl: "https://example.test/pfds.csv",
                    httpClientFactory: () => client);

                Atlas14Resolution live = await fetcher.ResolveAsync(35.23, -80.84, 10);

                Assert.Equal(Atlas14Source.Live, live.Source);
                Assert.Equal("live", live.SourceLabel);
                Assert.True(File.Exists(Atlas14Fetcher.CacheFilePath(cacheDir, 35.23, -80.84, 10)));
            }
            finally
            {
                TryDeleteDir(cacheDir);
            }
        }

        [Fact]
        public async Task ResolveAsync_NetworkFailure_FallsBackToEmbedded()
        {
            var handler = new StubHttpHandler("", HttpStatusCode.ServiceUnavailable);
            var client = new HttpClient(handler);
            var fetcher = new Atlas14Fetcher(
                cacheDirectory: null,
                pfdsUrl: "https://example.test/pfds.csv",
                httpClientFactory: () => client);

            Atlas14Resolution result = await fetcher.ResolveAsync(
                35.23, -80.84, 10,
                () => Atlas14Resolution.EmbeddedNearest(35.23, -80.84));

            Assert.Equal(Atlas14Source.Embedded, result.Source);
            Assert.Equal("charlotte-nc", result.PresetKey);
        }

        [Fact]
        public void TryParseDurationRow_ParsesHourAndMinuteTokens()
        {
            Assert.True(Atlas14Fetcher.TryParseDurationRow("10-min:, 1,2,5,10", 3, out double min, out double intensity));
            Assert.Equal(10.0, min);
            Assert.Equal(10.0, intensity); // 4th ARI column = 10-yr

            Assert.True(Atlas14Fetcher.TryParseDurationRow("2-hr:, 0.1,0.2,0.3,0.4", 2, out min, out intensity));
            Assert.Equal(120.0, min);
            Assert.Equal(0.3, intensity);
        }

        [Fact]
        public void Atlas14Resolution_PeakFromCatchments_UsesCurve()
        {
            var catchments = new[]
            {
                new Catchment { Name = "A", RunoffC = 0.85, AreaAcres = 2.0, TcMinutes = 12.0 },
            };
            var resolution = Atlas14Resolution.FromPreset(Atlas14Presets.Find("charlotte-nc")!);
            var peak = resolution.PeakFromCatchments(catchments);

            Assert.True(peak.PeakFlowCfs > 0);
            Assert.Contains(peak.Steps, s => s.Label == "IDF_source");
        }

        private static string CreateTempDir()
        {
            return Path.Combine(Path.GetTempPath(), "hc-atlas14-" + Guid.NewGuid().ToString("N"));
        }

        private static void TryDeleteDir(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch
            {
                // best-effort cleanup for CI
            }
        }

        private sealed class StubHttpHandler : HttpMessageHandler
        {
            private readonly string _body;
            private readonly HttpStatusCode _status;

            public StubHttpHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
            {
                _body = body;
                _status = status;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var response = new HttpResponseMessage(_status)
                {
                    Content = new StringContent(_body),
                };
                return Task.FromResult(response);
            }
        }
    }
}