using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HydroComplete.Engine;

namespace HydroComplete.Civil3D.Auth
{
    /// <summary>
    /// Pro-feature gate. Validates a local license file, dev bypass, or online activation.
    /// </summary>
    public static class LicenseGate
    {
        public const string ProActivationUrl = "https://hydrocomplete.com/civil3d";

        private static readonly string LicensePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HydroComplete",
            "license.json");

        private static readonly LicenseActivator Activator = new LicenseActivator();

        /// <summary>
        /// True when Pro is enabled via dev bypass, a valid local license file, or
        /// successful online/offline-stub activation.
        /// </summary>
        public static bool IsProEnabled()
        {
            if (IsDevBypassEnabled())
                return true;

            return LicenseActivator.TryReadLicense(LicensePath, out _);
        }

        /// <summary>Human-readable license tier for HC_LICENSE.</summary>
        public static string GetStatusLabel()
        {
            if (IsDevBypassEnabled())
                return "Pro (dev bypass: HYDROCOMPLETE_PRO=1)";

            if (LicenseActivator.TryReadLicense(LicensePath, out var license))
            {
                if (DateTimeOffset.TryParse(license.Expires, out var expires))
                    return $"Pro (licensed to {license.Email}, expires {expires:yyyy-MM-dd})";
                return $"Pro (licensed to {license.Email})";
            }

            if (LicenseActivator.TryReadLicenseMetadata(LicensePath, out var expired))
            {
                if (DateTimeOffset.TryParse(expired.Expires, out var expires))
                    return $"Expired (was {expired.Email}, expired {expires:yyyy-MM-dd})";
            }

            return "Free";
        }

        /// <summary>How the current license was last validated.</summary>
        public static string GetValidationModeLabel()
        {
            if (IsDevBypassEnabled())
                return "dev-bypass";

            if (!LicenseActivator.TryReadLicenseMetadata(LicensePath, out var license))
                return "none";

            if (!string.IsNullOrWhiteSpace(license.ValidationMode))
                return license.ValidationMode;

            return "local-file";
        }

        /// <summary>Last online/offline validation timestamp for HC_LICENSE.</summary>
        public static string GetLastValidatedLabel()
        {
            if (IsDevBypassEnabled())
                return "n/a (dev bypass)";

            if (!LicenseActivator.TryReadLicenseMetadata(LicensePath, out var license)
                || string.IsNullOrWhiteSpace(license.LastValidated))
            {
                return "never";
            }

            if (DateTimeOffset.TryParse(license.LastValidated, out var validated))
                return validated.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

            return license.LastValidated;
        }

        /// <summary>Whether the stored license used online validation vs offline stub.</summary>
        public static string GetOnlineOfflineLabel()
        {
            if (IsDevBypassEnabled())
                return "offline (environment bypass)";

            string mode = GetValidationModeLabel();
            if (string.Equals(mode, "online", StringComparison.OrdinalIgnoreCase))
                return "online (server validated)";
            if (string.Equals(mode, "offline-stub", StringComparison.OrdinalIgnoreCase))
                return "offline (local beta stub)";
            if (string.Equals(mode, "none", StringComparison.OrdinalIgnoreCase))
                return "offline (no license)";
            return "offline (local file)";
        }

        /// <summary>Path to the local license file (%APPDATA%\HydroComplete\license.json).</summary>
        public static string GetLicenseFilePath() => LicensePath;

        /// <summary>Activate Pro with email + token; writes license.json on success.</summary>
        public static Task<LicenseActivationResult> ActivateAsync(
            string email,
            string token,
            CancellationToken cancellationToken = default)
        {
            return Activator.ActivateAsync(email, token, LicensePath, cancellationToken);
        }

        /// <summary>
        /// Re-validate the stored license against hydrocomplete.com when online;
        /// refreshes license.json on success or offline-stub fallback.
        /// </summary>
        public static async Task<bool> ValidateOnlineAsync(CancellationToken cancellationToken = default)
        {
            LicenseActivationResult result = await Activator
                .RefreshAsync(LicensePath, cancellationToken)
                .ConfigureAwait(false);
            return result.Success;
        }

        private static bool IsDevBypassEnabled()
        {
#if DEBUG
            string? val = Environment.GetEnvironmentVariable("HYDROCOMPLETE_PRO");
            return string.Equals(val, "1", StringComparison.Ordinal);
#else
            return false;
#endif
        }
    }
}