using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Activates and validates HydroComplete Pro licenses. Tries the hydrocomplete.com
    /// licensing API when online; falls back to an offline stub for well-formed beta tokens.
    /// </summary>
    public sealed class LicenseActivator
    {
        /// <summary>hc-refactored route: POST /api/licensing/validate (see server/routes/licensing.js).</summary>
        /// <remarks>Uses Fly API host until hydrocomplete.com /api/* Netlify proxy is deployed (netlify.toml).</remarks>
        public const string DefaultValidateUrl = "https://hc-refactored.fly.dev/api/licensing/validate";

        /// <summary>Beta / offline activation token prefix.</summary>
        public const string TokenPrefix = "hc_live_";

        public static readonly TimeSpan StubValidity = TimeSpan.FromDays(365);

        private static readonly HttpClient SharedHttp = CreateHttpClient();

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
        };

        private readonly string _validateUrl;
        private readonly Func<HttpClient> _httpClientFactory;

        public LicenseActivator(
            string? validateUrl = null,
            Func<HttpClient>? httpClientFactory = null)
        {
            _validateUrl = string.IsNullOrWhiteSpace(validateUrl) ? DefaultValidateUrl : validateUrl!.Trim();
            _httpClientFactory = httpClientFactory ?? (() => SharedHttp);
        }

        /// <summary>True when <paramref name="token"/> matches the beta offline format.</summary>
        public static bool IsWellFormedToken(string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return false;

            string trimmed = token!.Trim();
            if (!trimmed.StartsWith(TokenPrefix, StringComparison.Ordinal))
                return false;

            return trimmed.Length >= TokenPrefix.Length + 8;
        }

        /// <summary>Parse a single paste field: "email@domain.com hc_live_…".</summary>
        public static bool TryParseCombinedInput(string? input, out string email, out string token)
        {
            email = "";
            token = "";
            if (string.IsNullOrWhiteSpace(input))
                return false;

            string trimmed = input!.Trim();
            int space = trimmed.IndexOf(' ');
            if (space <= 0 || trimmed.IndexOf('@') <= 0)
                return false;

            email = trimmed.Substring(0, space).Trim();
            token = trimmed.Substring(space + 1).Trim();
            return !string.IsNullOrWhiteSpace(email) && IsWellFormedToken(token);
        }

        public static bool TryReadLicense(string licenseFilePath, out LicenseRecord license)
        {
            license = new LicenseRecord();
            if (!File.Exists(licenseFilePath))
                return false;

            try
            {
                string json = File.ReadAllText(licenseFilePath);
                var record = JsonSerializer.Deserialize<LicenseRecord>(json, JsonOptions);
                if (record == null || !IsLicenseFieldsValid(record))
                    return false;

                if (!DateTimeOffset.TryParse(record.Expires, out var expires) || expires <= DateTimeOffset.UtcNow)
                    return false;

                license = record;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryReadLicenseMetadata(string licenseFilePath, out LicenseRecord license)
        {
            license = new LicenseRecord();
            if (!File.Exists(licenseFilePath))
                return false;

            try
            {
                string json = File.ReadAllText(licenseFilePath);
                var record = JsonSerializer.Deserialize<LicenseRecord>(json, JsonOptions);
                if (record == null)
                    return false;

                license = record;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Activate Pro: POST token to hydrocomplete.com when reachable; otherwise accept
        /// well-formed <see cref="TokenPrefix"/> tokens and write a local license file.
        /// </summary>
        public Task<LicenseActivationResult> ActivateAsync(
            string email,
            string token,
            string licenseFilePath,
            CancellationToken cancellationToken = default)
        {
            email = (email ?? "").Trim();
            token = (token ?? "").Trim();

            if (string.IsNullOrWhiteSpace(email) || email.IndexOf('@') <= 0)
            {
                return Task.FromResult(Fail("Enter a valid email address."));
            }

            if (!IsWellFormedToken(token))
            {
                return Task.FromResult(Fail(
                    $"Activation token must start with {TokenPrefix} and be at least {TokenPrefix.Length + 8} characters."));
            }

            return ActivateCoreAsync(email, token, licenseFilePath, cancellationToken);
        }

        /// <summary>Re-validate the stored license against the API when online.</summary>
        public async Task<LicenseActivationResult> RefreshAsync(
            string licenseFilePath,
            CancellationToken cancellationToken = default)
        {
            if (!TryReadLicenseMetadata(licenseFilePath, out var existing)
                || string.IsNullOrWhiteSpace(existing.Email)
                || string.IsNullOrWhiteSpace(existing.Token))
            {
                return Fail("No license file to validate. Run HC_ACTIVATE first.");
            }

            return await ActivateCoreAsync(existing.Email, existing.Token, licenseFilePath, cancellationToken)
                .ConfigureAwait(false);
        }

        public static void WriteLicenseFile(string licenseFilePath, LicenseRecord record)
        {
            string? dir = Path.GetDirectoryName(licenseFilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(record, JsonOptions);
            File.WriteAllText(licenseFilePath, json);
        }

        private async Task<LicenseActivationResult> ActivateCoreAsync(
            string email,
            string token,
            string licenseFilePath,
            CancellationToken cancellationToken)
        {
            var online = await TryOnlineValidationAsync(email, token, cancellationToken).ConfigureAwait(false);
            if (online.Success)
            {
                WriteLicenseFile(licenseFilePath, online.Record!);
                return new LicenseActivationResult
                {
                    Success = true,
                    Mode = LicenseValidationMode.Online,
                    Message = "Pro activated (online validation).",
                    Expires = online.Record!.Expires,
                };
            }

            // A definitive server rejection (reachable, HTTP 2xx, valid:false) must DENY.
            // Falling back to the offline stub here would let any well-formed token defeat
            // server-side validation entirely. The stub is only for genuine "couldn't reach
            // the server" cases below.
            if (online.ServerSaidInvalid)
                return Fail(online.ErrorMessage ?? "License is not valid on the server. Contact support.");

            if (!IsWellFormedToken(token))
                return Fail(online.ErrorMessage ?? "Online validation failed and token format is invalid.");

            var stub = BuildOfflineStubRecord(email, token);
            WriteLicenseFile(licenseFilePath, stub);
            return new LicenseActivationResult
            {
                Success = true,
                Mode = LicenseValidationMode.OfflineStub,
                Message = online.WasNetworkAttempt
                    ? "Pro activated (offline stub — hydrocomplete.com unreachable or token not in server registry)."
                    : "Pro activated (offline stub — beta token accepted locally).",
                Expires = stub.Expires,
            };
        }

        private async Task<OnlineValidationAttempt> TryOnlineValidationAsync(
            string email,
            string token,
            CancellationToken cancellationToken)
        {
            string body = JsonSerializer.Serialize(new
            {
                licenseKey = token,
                features = new[] { "reports", "export", "civil3d" },
            });

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, _validateUrl);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                request.Headers.TryAddWithoutValidation("User-Agent", "HydroComplete-Civil3D/1.2.0");

                HttpClient client = _httpClientFactory();
                using HttpResponseMessage response = await client
                    .SendAsync(request, cancellationToken)
                    .ConfigureAwait(false);

                string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return new OnlineValidationAttempt
                    {
                        WasNetworkAttempt = true,
                        ErrorMessage = $"Server returned {(int)response.StatusCode}.",
                    };
                }

                using JsonDocument doc = JsonDocument.Parse(responseBody);
                JsonElement root = doc.RootElement;
                if (!root.TryGetProperty("valid", out JsonElement validEl) || !validEl.GetBoolean())
                {
                    return new OnlineValidationAttempt
                    {
                        WasNetworkAttempt = true,
                        ServerSaidInvalid = true,
                        ErrorMessage = ReadErrorMessage(root) ?? "License not valid on server.",
                    };
                }

                string expires = ReadExpires(root) ?? DateTimeOffset.UtcNow.Add(StubValidity).ToString("o");
                string storedToken = ReadAccessToken(root) ?? token;
                var record = new LicenseRecord
                {
                    Email = email,
                    Token = storedToken,
                    Expires = expires,
                    LastValidated = DateTimeOffset.UtcNow.ToString("o"),
                    ValidationMode = "online",
                };

                return new OnlineValidationAttempt
                {
                    Success = true,
                    WasNetworkAttempt = true,
                    Record = record,
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new OnlineValidationAttempt
                {
                    WasNetworkAttempt = true,
                    ErrorMessage = ex.Message,
                };
            }
        }

        private static LicenseRecord BuildOfflineStubRecord(string email, string token)
        {
            return new LicenseRecord
            {
                Email = email,
                Token = token,
                Expires = DateTimeOffset.UtcNow.Add(StubValidity).ToString("o"),
                LastValidated = DateTimeOffset.UtcNow.ToString("o"),
                ValidationMode = "offline-stub",
            };
        }

        private static bool IsLicenseFieldsValid(LicenseRecord record)
        {
            return !string.IsNullOrWhiteSpace(record.Email)
                && !string.IsNullOrWhiteSpace(record.Token)
                && !string.IsNullOrWhiteSpace(record.Expires);
        }

        private static string? ReadExpires(JsonElement root)
        {
            if (root.TryGetProperty("license", out JsonElement license)
                && license.TryGetProperty("expires", out JsonElement expires))
            {
                return expires.GetString();
            }

            return null;
        }

        private static string? ReadAccessToken(JsonElement root)
        {
            if (root.TryGetProperty("accessToken", out JsonElement token))
                return token.GetString();
            return null;
        }

        private static string? ReadErrorMessage(JsonElement root)
        {
            if (root.TryGetProperty("error", out JsonElement error))
            {
                if (error.ValueKind == JsonValueKind.String)
                    return error.GetString();
            }

            return null;
        }

        private static LicenseActivationResult Fail(string message)
        {
            return new LicenseActivationResult
            {
                Success = false,
                Mode = LicenseValidationMode.None,
                Message = message,
            };
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            return client;
        }

        private sealed class OnlineValidationAttempt
        {
            public bool Success { get; set; }
            public bool WasNetworkAttempt { get; set; }

            /// <summary>True when the server was reached and authoritatively rejected the key.</summary>
            public bool ServerSaidInvalid { get; set; }

            public string? ErrorMessage { get; set; }
            public LicenseRecord? Record { get; set; }
        }
    }

    /// <summary>license.json schema stored under %APPDATA%\HydroComplete\.</summary>
    public sealed class LicenseRecord
    {
        public string Email { get; set; } = "";
        public string Token { get; set; } = "";
        public string Expires { get; set; } = "";
        public string LastValidated { get; set; } = "";
        public string ValidationMode { get; set; } = "";
    }

    public enum LicenseValidationMode
    {
        None,
        DevBypass,
        Online,
        OfflineStub,
        LocalFile,
    }

    public sealed class LicenseActivationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public LicenseValidationMode Mode { get; set; }
        public string Expires { get; set; } = "";
    }
}