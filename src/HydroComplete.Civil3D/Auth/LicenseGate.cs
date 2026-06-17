using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace HydroComplete.Civil3D.Auth
{
    /// <summary>
    /// Pro-feature gate skeleton. Validates a local license file or dev bypass;
    /// online validation against hydrocomplete.com is not wired yet.
    /// </summary>
    public static class LicenseGate
    {
        public const string ProActivationUrl = "https://hydrocomplete.com/civil3d";

        private static readonly string LicensePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HydroComplete",
            "license.json");

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        /// <summary>
        /// True when Pro is enabled via dev bypass, a valid local license file, or
        /// (future) successful online validation.
        /// </summary>
        public static bool IsProEnabled()
        {
            if (IsDevBypassEnabled())
                return true;

            return TryReadLicense(LicensePath, out _);
        }

        /// <summary>Human-readable license tier for HC_LICENSE.</summary>
        public static string GetStatusLabel()
        {
            if (IsDevBypassEnabled())
                return "Pro (dev bypass: HYDROCOMPLETE_PRO=1)";

            if (TryReadLicense(LicensePath, out var license))
            {
                if (DateTimeOffset.TryParse(license.Expires, out var expires))
                    return $"Pro (licensed to {license.Email}, expires {expires:yyyy-MM-dd})";
                return $"Pro (licensed to {license.Email})";
            }

            return "Free";
        }

        /// <summary>Path to the local license file (%APPDATA%\HydroComplete\license.json).</summary>
        public static string GetLicenseFilePath() => LicensePath;

        /// <summary>
        /// Placeholder for future hydrocomplete.com API token validation.
        /// </summary>
        public static Task<bool> ValidateOnlineAsync()
        {
            // TODO: POST license token to hydrocomplete.com API and refresh license.json
            return Task.FromResult(false);
        }

        private static bool IsDevBypassEnabled()
        {
            string? val = Environment.GetEnvironmentVariable("HYDROCOMPLETE_PRO");
            return string.Equals(val, "1", StringComparison.Ordinal);
        }

        private static bool TryReadLicense(string path, out LicenseRecord license)
        {
            license = new LicenseRecord();
            if (!File.Exists(path))
                return false;

            try
            {
                string json = File.ReadAllText(path);
                var record = JsonSerializer.Deserialize<LicenseRecord>(json, JsonOptions);
                if (record == null
                    || string.IsNullOrWhiteSpace(record.Email)
                    || string.IsNullOrWhiteSpace(record.Token)
                    || string.IsNullOrWhiteSpace(record.Expires))
                {
                    return false;
                }

                if (!DateTimeOffset.TryParse(record.Expires, out var expires))
                    return false;

                if (expires <= DateTimeOffset.UtcNow)
                    return false;

                license = record;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>license.json schema: { "email", "token", "expires" (ISO-8601) }.</summary>
        private sealed class LicenseRecord
        {
            public string Email { get; set; } = "";
            public string Token { get; set; } = "";
            public string Expires { get; set; } = "";
        }
    }
}