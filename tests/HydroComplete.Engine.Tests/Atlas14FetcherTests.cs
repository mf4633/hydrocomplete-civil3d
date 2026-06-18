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
        private static string FixturePath(string fileName) =>
            Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

        [Fact]
        public void ParseIntensityTable_CharlotteFixture_ReadsTenYearColumn()
        {
            string csv = File.ReadAllText(FixturePath("charlotte_nc_intensity.csv"));
            var table = Atlas14Fetcher.ParseIntensityTable(csv, returnPeriodYears: 10);

            Assert.True(table.Count >= 6);
            Assert.Contains(table, p => p.DurationMin == 10.0 && p.IntensityInHr == 5.81);
            Assert.Contains(table, p => p.DurationMin == 60.0 && p.IntensityInHr == 2.31);
        }

        [Fact]
        public void ParseIntensitiesAtDuration_CharlotteFixture_ReadsStandardReturnPeriods()
        {
            string csv = File.ReadAllText(FixturePath("charlotte_nc_intensity.csv"));
            var intensities = Atlas14Fetcher.ParseIntensitiesAtDuration(csv, 10.0);

            Assert.Equal(Atlas14Fetcher.StandardReturnPeriods.Length, intensities.Count);
            Assert.Equal(4.54, intensities[2], 2);
            Assert.Equal(5.81, intensities[10], 2);
            Assert.Equal(6.40, intensities[25], 2);
            Assert.Equal(7.19, intensities[100], 2);
        }

        [Fact]
        public void ParseIntensitiesAtDuration_SplitHeader_ReadsTenMinuteRow()
        {
            const string csv = """
                PRECIPITATION FREQUENCY ESTIMATES
                by duration for ARI (years):, 1,2,5,10,25,50,100,200,500,1000
                10-min:, 3.83,4.54,5.29,5.81,6.40,6.82,7.19,7.51,7.87,8.09
                """;

            var intensities = Atlas14Fetcher.ParseIntensitiesAtDuration(csv, 10.0);

            Assert.Equal(5.81, intensities[10], 2);
            Assert.Equal(7.19, intensities[100], 2);
        }

        [Fact]
        public void FormatMultiReturnPeriodIntensities_CharlotteFixture_FormatsTenMinuteRow()
        {
            string csv = File.ReadAllText(FixturePath("charlotte_nc_intensity.csv"));
            var intensities = Atlas14Fetcher.ParseIntensitiesAtDuration(csv, 10.0);
            string label = Atlas14Fetcher.FormatMultiReturnPeriodIntensities(intensities);

            Assert.Equal("2y=4.54 10y=5.81 25y=6.40 100y=7.19", label);
        }

        [Fact]
        public void ParseAndFitAll_CharlotteFixture_FitsStandardReturnPeriods()
        {
            string csv = File.ReadAllText(FixturePath("charlotte_nc_intensity.csv"));
            var fits = Atlas14Fetcher.ParseAndFitAll(csv, 35.23, -80.84);

            Assert.Equal(4, fits.Count);
            foreach (int returnPeriod in Atlas14Fetcher.StandardReturnPeriods)
            {
                Atlas14CacheEntry entry = fits[returnPeriod];
                Assert.Equal(returnPeriod, entry.ReturnPeriodYears);
                Assert.True(entry.A > 0);
                Assert.True(entry.B > 0);
                Assert.True(entry.C > 0);
            }
        }

        [Fact]
        public void CharlottePreset_MultiReturnPeriod10MinLabel_MatchesFixture()
        {
            Atlas14Presets.Preset preset = Atlas14Presets.Find("charlotte-nc")!;
            Assert.Equal("2y=4.54 10y=5.81 25y=6.40 100y=7.19", preset.MultiReturnPeriod10MinLabel);
        }

        [Fact]
        public void CharlottePreset_ToCurve_ReturnPeriodSpecificCoefficients()
        {
            Atlas14Presets.Preset preset = Atlas14Presets.Find("charlotte-nc")!;

            Assert.Equal(81.21, preset.ToCurve(10).A, 2);
            Assert.Equal(75.09, preset.ToCurve(2).A, 2);
            Assert.Equal(69.60, preset.ToCurve(100).A, 2);
        }

        [Fact]
        public void ParseAndFit_CharlotteFixture_ProducesReasonableCoefficients()
        {
            string csv = File.ReadAllText(FixturePath("charlotte_nc_intensity.csv"));
            Atlas14CacheEntry entry = Atlas14Fetcher.ParseAndFit(csv, 35.23, -80.84, 10);

            Assert.Equal("Ohio River Basin", entry.ProjectArea);
            Assert.True(entry.A > 0);
            Assert.True(entry.B > 0);
            Assert.True(entry.C > 0);

            double i10 = entry.A / Math.Pow(10.0 + entry.B, entry.C);
            Assert.InRange(i10, 5.0, 6.5);
        }

        [Fact]
        public void ParseIntensityTable_HoustonFixture_ReadsTenYearColumn()
        {
            string csv = File.ReadAllText(FixturePath("houston_tx_intensity.csv"));
            var table = Atlas14Fetcher.ParseIntensityTable(csv, returnPeriodYears: 10);

            Assert.True(table.Count >= 6);
            Assert.Contains(table, p => p.DurationMin == 10.0 && p.IntensityInHr == 8.07);
            Assert.Contains(table, p => p.DurationMin == 60.0 && p.IntensityInHr == 3.22);
        }

        [Fact]
        public void ParseAndFit_HoustonFixture_ProducesReasonableCoefficients()
        {
            string csv = File.ReadAllText(FixturePath("houston_tx_intensity.csv"));
            Atlas14CacheEntry entry = Atlas14Fetcher.ParseAndFit(csv, 29.76, -95.37, 10);

            Assert.Equal("Texas", entry.ProjectArea);
            Assert.True(entry.A > 0);
            Assert.True(entry.B > 0);
            Assert.True(entry.C > 0);

            double i10 = entry.A / Math.Pow(10.0 + entry.B, entry.C);
            Assert.InRange(i10, 7.0, 9.0);
        }

        [Fact]
        public void ParseIntensityTable_PhoenixFixture_ReadsTenYearColumn()
        {
            string csv = File.ReadAllText(FixturePath("phoenix_az_intensity.csv"));
            var table = Atlas14Fetcher.ParseIntensityTable(csv, returnPeriodYears: 10);

            Assert.True(table.Count >= 6);
            Assert.Contains(table, p => p.DurationMin == 10.0 && p.IntensityInHr == 3.67);
            Assert.Contains(table, p => p.DurationMin == 60.0 && p.IntensityInHr == 1.26);
        }

        [Fact]
        public void ParseAndFit_PhoenixFixture_ProducesReasonableCoefficients()
        {
            string csv = File.ReadAllText(FixturePath("phoenix_az_intensity.csv"));
            Atlas14CacheEntry entry = Atlas14Fetcher.ParseAndFit(csv, 33.45, -112.07, 10);

            Assert.Equal("Southwest", entry.ProjectArea);
            Assert.True(entry.A > 0);
            Assert.True(entry.B > 0);
            Assert.True(entry.C > 0);

            double i10 = entry.A / Math.Pow(10.0 + entry.B, entry.C);
            Assert.InRange(i10, 3.0, 4.5);
        }

        [Fact]
        public void ParseAndFit_SeattleFixture_ThrowsOutOfCoverage()
        {
            string csv = File.ReadAllText(FixturePath("seattle_wa_out_of_coverage.csv"));
            var ex = Assert.Throws<InvalidDataException>(() =>
                Atlas14Fetcher.ParseAndFit(csv, 47.61, -122.33, 10));

            Assert.Contains("not within a project area", ex.Message, StringComparison.OrdinalIgnoreCase);
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
                string csv = File.ReadAllText(FixturePath("charlotte_nc_intensity.csv"));
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
                string csv = File.ReadAllText(FixturePath("charlotte_nc_intensity.csv"));
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
        public void ResolveIntensitiesAtDuration_DownloadsFixture_ReturnsMultiReturnPeriod()
        {
            string csv = File.ReadAllText(FixturePath("charlotte_nc_intensity.csv"));
            var handler = new StubHttpHandler(csv);
            var client = new HttpClient(handler);
            var fetcher = new Atlas14Fetcher(
                cacheDirectory: null,
                pfdsUrl: "https://example.test/pfds.csv",
                httpClientFactory: () => client);

            var intensities = fetcher.ResolveIntensitiesAtDuration(35.23, -80.84, 10.0);

            Assert.Equal(4.54, intensities[2], 2);
            Assert.Equal(5.81, intensities[10], 2);
            Assert.Equal(7.19, intensities[100], 2);
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