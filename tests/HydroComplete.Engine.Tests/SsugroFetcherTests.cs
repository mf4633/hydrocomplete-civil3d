using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class SsugroFetcherTests
    {
        private static string FixturePath(string fileName) =>
            Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

        [Fact]
        public void ParseSdaTable_CecilFixture_ReadsRows()
        {
            string json = File.ReadAllText(FixturePath("ssurgo_cecil_sda.json"));
            List<Dictionary<string, string?>> rows = SsugroFetcher.ParseSdaTable(json);

            Assert.Equal(2, rows.Count);
            Assert.Equal("Cecil sandy loam", rows[0]["muname"]);
            Assert.Equal("85", rows[0]["comppct_r"]);
        }

        [Fact]
        public void AggregateComponents_CecilFixture_SelectsDominantComponent()
        {
            string json = File.ReadAllText(FixturePath("ssurgo_cecil_sda.json"));
            List<Dictionary<string, string?>> rows = SsugroFetcher.ParseSdaTable(json);
            List<SsugroMapUnit> units = SsugroFetcher.AggregateComponents(rows);

            Assert.Single(units);
            SsugroMapUnit unit = units[0];
            Assert.Equal("Cecil", unit.DominantComponent);
            Assert.Equal('B', unit.HydrologicSoilGroup);
            Assert.Equal(0.24, unit.SurfaceHorizon!.KFactor!.Value, 2);
            Assert.Equal(34.0, unit.SurfaceHorizon.PctSand!.Value, 0);
        }

        [Fact]
        public void ToSoilProperties_LiveMapUnit_MapsHsgAndK()
        {
            var resolution = new SsugroResolution
            {
                Source = SsugroSource.Live,
                Lat = 35.5,
                Lon = -82.5,
                MapUnit = new SsugroMapUnit
                {
                    Muname = "Cecil sandy loam",
                    DominantComponent = "Cecil",
                    HydrologicSoilGroup = 'B',
                    DominantTexture = "sandy loam",
                    SurfaceHorizon = new SsugroSurfaceHorizon { KFactor = 0.24 },
                },
            };

            SoilDatabase.SoilProperties soil = resolution.ToSoilProperties();
            Assert.Equal('B', soil.HydrologicSoilGroup);
            Assert.Equal(0.24, soil.KFactor, 2);
            Assert.Equal(0.25, soil.InfiltrationRateInPerHr, 2);
        }

        [Fact]
        public void RegionalFallback_Piedmont_ReturnsHsgB()
        {
            SsugroResolution resolution = SsugroResolution.RegionalFallback(35.5, -80.0);
            Assert.Equal(SsugroSource.RegionalFallback, resolution.Source);
            Assert.Equal('B', resolution.MapUnit.HydrologicSoilGroup);
            Assert.True(resolution.MapUnit.IsFallback);
        }

        [Fact]
        public async Task ResolveAsync_UsesCacheWhenLiveFails()
        {
            string cacheDir = Path.Combine(Path.GetTempPath(), "hc-ssurgo-" + Guid.NewGuid().ToString("N"));
            try
            {
                var fetcher = new SsugroFetcher(
                    cacheDir,
                    SsugroFetcher.DefaultCacheTtl,
                    "https://example.invalid/ssurgo",
                    () => new HttpClient(new ThrowingHandler()));

                string json = File.ReadAllText(FixturePath("ssurgo_cecil_sda.json"));
                List<Dictionary<string, string?>> rows = SsugroFetcher.ParseSdaTable(json);
                SsugroMapUnit unit = SsugroFetcher.AggregateComponents(rows)[0];

                Directory.CreateDirectory(cacheDir);
                var entry = new
                {
                    Lat = 35.5,
                    Lon = -82.5,
                    FetchedUtc = DateTime.UtcNow.AddDays(-1),
                    MapUnit = unit,
                };
                File.WriteAllText(
                    Path.Combine(cacheDir, "35.5_-82.5.json"),
                    System.Text.Json.JsonSerializer.Serialize(entry));

                SsugroResolution resolution = await fetcher.ResolveAsync(
                    35.5,
                    -82.5,
                    () => SsugroResolution.RegionalFallback(35.5, -82.5));

                Assert.Equal(SsugroSource.Cache, resolution.Source);
                Assert.Equal("Cecil", resolution.MapUnit.DominantComponent);
            }
            finally
            {
                if (Directory.Exists(cacheDir))
                    Directory.Delete(cacheDir, recursive: true);
            }
        }

        private sealed class ThrowingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken) =>
                throw new HttpRequestException("Simulated offline");
        }
    }
}