using System;
using System.Collections.Generic;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Embedded state regulatory thresholds for stormwater compliance review.
    /// Ported from HydroComplete Pro StateConfigurations.js (key fields only).
    /// </summary>
    public sealed class StateComplianceConfig
    {
        public string Code { get; init; } = "";
        public string Name { get; init; } = "";
        public string RegulatoryBody { get; init; } = "";

        /// <summary>Water-quality design storm depth, inches (24-hr).</summary>
        public double DesignStormInches { get; init; }

        /// <summary>Runoff depth factor applied for WQV (inches over site).</summary>
        public double WqVolumeFactorInches { get; init; }

        /// <summary>Required peak attenuation vs pre-development (% of pre-dev peak).</summary>
        public double PeakAttenuationPercent { get; init; }

        /// <summary>Minimum acceptable BMP drawdown time, hours.</summary>
        public double DrawdownMinHours { get; init; }

        /// <summary>Maximum acceptable BMP drawdown time, hours.</summary>
        public double DrawdownMaxHours { get; init; }

        /// <summary>Required TSS removal, percent.</summary>
        public double TssRemovalPercent { get; init; }

        /// <summary>Required TN removal, percent (0 = not checked).</summary>
        public double TnRemovalPercent { get; init; }

        /// <summary>Required TP removal, percent (0 = not checked).</summary>
        public double TpRemovalPercent { get; init; }

        /// <summary>TSS removal for roadway projects when different from default.</summary>
        public double? RoadwayTssRemovalPercent { get; init; }

        /// <summary>USDA tolerable soil loss, tons/acre/year.</summary>
        public double TolerableSoilLossTonsPerAcYr { get; init; } = 5.0;

        /// <summary>RUSLE R-factor default for the state.</summary>
        public double DefaultRFactor { get; init; } = 170.0;

        /// <summary>Whether volume control (WQV) is required statewide.</summary>
        public bool VolumeControlRequired { get; init; }
    }

    /// <summary>
    /// Lookup table for all 50 US states, DC, PR, VI, and generic EPA defaults.
    /// Data is generated in StateComplianceData.cs from StateConfigurations.js.
    /// </summary>
    public static partial class StateCompliance
    {
        public const string DefaultCode = "DEFAULT";

        private static readonly Lazy<Dictionary<string, StateComplianceConfig>> ConfigsLazy =
            new Lazy<Dictionary<string, StateComplianceConfig>>(BuildConfigs);

        private static Dictionary<string, StateComplianceConfig> Configs => ConfigsLazy.Value;

        /// <summary>Returns the config for <paramref name="stateCode"/> or DEFAULT.</summary>
        public static StateComplianceConfig Get(string stateCode) => GetConfig(stateCode);

        /// <summary>Returns the config for <paramref name="stateCode"/> or DEFAULT.</summary>
        public static StateComplianceConfig GetConfig(string stateCode)
        {
            if (string.IsNullOrWhiteSpace(stateCode))
                return Configs[DefaultCode];

            string key = stateCode.Trim().ToUpperInvariant();
            return Configs.TryGetValue(key, out StateComplianceConfig? cfg)
                ? cfg
                : Configs[DefaultCode];
        }

        /// <summary>All configured state codes except DEFAULT.</summary>
        public static IReadOnlyList<string> AvailableStateCodes()
        {
            var list = new List<string>();
            foreach (KeyValuePair<string, StateComplianceConfig> pair in Configs)
            {
                if (!string.Equals(pair.Key, DefaultCode, StringComparison.OrdinalIgnoreCase))
                    list.Add(pair.Key);
            }

            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        /// <summary>TSS removal requirement for a development type.</summary>
        public static double RequiredTssPercent(StateComplianceConfig config, string developmentType)
        {
            if (string.Equals(developmentType, "roadway", StringComparison.OrdinalIgnoreCase)
                && config.RoadwayTssRemovalPercent.HasValue)
                return config.RoadwayTssRemovalPercent.Value;

            return config.TssRemovalPercent;
        }
    }
}