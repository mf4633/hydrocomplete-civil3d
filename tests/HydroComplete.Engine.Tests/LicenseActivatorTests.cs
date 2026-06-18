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
    public class LicenseActivatorTests
    {
        private const string ValidToken = "hc_live_beta_tester01";

        [Theory]
        [InlineData("hc_live_beta_tester01", true)]
        [InlineData("hc_live_x", false)]
        [InlineData("HCPRO-PRO-2024-001", false)]
        [InlineData("", false)]
        public void IsWellFormedToken_MatchesExpected(string token, bool expected)
        {
            Assert.Equal(expected, LicenseActivator.IsWellFormedToken(token));
        }

        [Fact]
        public void TryParseCombinedInput_ParsesEmailAndToken()
        {
            Assert.True(LicenseActivator.TryParseCombinedInput(
                "beta@hydrocomplete.com hc_live_beta_tester01",
                out string email,
                out string token));

            Assert.Equal("beta@hydrocomplete.com", email);
            Assert.Equal(ValidToken, token);
        }

        [Fact]
        public async Task ActivateAsync_OfflineStub_WritesOneYearLicense()
        {
            string licensePath = Path.Combine(CreateTempDir(), "license.json");
            var handler = new StubHttpHandler("{}", HttpStatusCode.NotFound);
            var activator = new LicenseActivator(
                validateUrl: "https://example.test/api/licensing/validate",
                httpClientFactory: () => new HttpClient(handler));

            LicenseActivationResult result = await activator.ActivateAsync(
                "beta@hydrocomplete.com",
                ValidToken,
                licensePath);

            Assert.True(result.Success);
            Assert.Equal(LicenseValidationMode.OfflineStub, result.Mode);
            Assert.True(File.Exists(licensePath));
            Assert.True(LicenseActivator.TryReadLicense(licensePath, out LicenseRecord record));
            Assert.Equal("beta@hydrocomplete.com", record.Email);
            Assert.Equal(ValidToken, record.Token);
            Assert.Equal("offline-stub", record.ValidationMode);
            Assert.True(DateTimeOffset.TryParse(record.Expires, out var expires));
            Assert.True(expires > DateTimeOffset.UtcNow.AddDays(360));
        }

        [Fact]
        public async Task ActivateAsync_OnlineSuccess_WritesServerExpiry()
        {
            string licensePath = Path.Combine(CreateTempDir(), "license.json");
            const string response = @"{
                ""valid"": true,
                ""license"": { ""expires"": ""2027-01-15"" },
                ""accessToken"": ""server-token-abc""
            }";

            var handler = new StubHttpHandler(response);
            var activator = new LicenseActivator(
                validateUrl: "https://example.test/api/licensing/validate",
                httpClientFactory: () => new HttpClient(handler));

            LicenseActivationResult result = await activator.ActivateAsync(
                "pro@hydrocomplete.com",
                ValidToken,
                licensePath);

            Assert.True(result.Success);
            Assert.Equal(LicenseValidationMode.Online, result.Mode);
            Assert.True(LicenseActivator.TryReadLicense(licensePath, out LicenseRecord record));
            Assert.Equal("online", record.ValidationMode);
            Assert.Equal("server-token-abc", record.Token);
            Assert.Contains("2027-01-15", record.Expires, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ActivateAsync_InvalidToken_ReturnsFailure()
        {
            string licensePath = Path.Combine(CreateTempDir(), "license.json");
            var activator = new LicenseActivator(
                validateUrl: "https://example.test/api/licensing/validate",
                httpClientFactory: () => new HttpClient(new StubHttpHandler("{}")));

            LicenseActivationResult result = await activator.ActivateAsync(
                "beta@hydrocomplete.com",
                "not-a-token",
                licensePath);

            Assert.False(result.Success);
            Assert.False(File.Exists(licensePath));
        }

        [Fact]
        public async Task RefreshAsync_UsesStoredEmailAndToken()
        {
            string dir = CreateTempDir();
            string licensePath = Path.Combine(dir, "license.json");
            var existing = new LicenseRecord
            {
                Email = "beta@hydrocomplete.com",
                Token = ValidToken,
                Expires = DateTimeOffset.UtcNow.AddDays(30).ToString("o"),
                ValidationMode = "offline-stub",
            };
            LicenseActivator.WriteLicenseFile(licensePath, existing);

            const string response = @"{ ""valid"": true, ""license"": { ""expires"": ""2028-06-01"" } }";
            var handler = new StubHttpHandler(response);
            var activator = new LicenseActivator(
                validateUrl: "https://example.test/api/licensing/validate",
                httpClientFactory: () => new HttpClient(handler));

            LicenseActivationResult result = await activator.RefreshAsync(licensePath);

            Assert.True(result.Success);
            Assert.Equal(LicenseValidationMode.Online, result.Mode);
            Assert.True(LicenseActivator.TryReadLicense(licensePath, out LicenseRecord record));
            Assert.Equal("online", record.ValidationMode);
        }

        private static string CreateTempDir()
        {
            string path = Path.Combine(Path.GetTempPath(), "hc-license-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
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