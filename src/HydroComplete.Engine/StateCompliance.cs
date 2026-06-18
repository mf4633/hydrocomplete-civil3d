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

    /// <summary>Lookup table for NC, SC, VA, FL, TX, CA, NY and generic defaults.</summary>
    public static class StateCompliance
    {
        public const string DefaultCode = "DEFAULT";

        private static readonly Dictionary<string, StateComplianceConfig> Configs =
            new Dictionary<string, StateComplianceConfig>(StringComparer.OrdinalIgnoreCase)
            {
                ["NC"] = new StateComplianceConfig
                {
                    Code = "NC",
                    Name = "North Carolina",
                    RegulatoryBody = "NCDEQ (DEMLR)",
                    DesignStormInches = 1.0,
                    WqVolumeFactorInches = 1.0,
                    PeakAttenuationPercent = 100.0,
                    DrawdownMinHours = 48.0,
                    DrawdownMaxHours = 120.0,
                    TssRemovalPercent = 85.0,
                    TnRemovalPercent = 30.0,
                    TpRemovalPercent = 30.0,
                    RoadwayTssRemovalPercent = 80.0,
                    TolerableSoilLossTonsPerAcYr = 5.0,
                    DefaultRFactor = 170.0,
                    VolumeControlRequired = true,
                },
                ["SC"] = new StateComplianceConfig
                {
                    Code = "SC",
                    Name = "South Carolina",
                    RegulatoryBody = "SC DHEC",
                    DesignStormInches = 1.5,
                    WqVolumeFactorInches = 1.5,
                    PeakAttenuationPercent = 100.0,
                    DrawdownMinHours = 48.0,
                    DrawdownMaxHours = 72.0,
                    TssRemovalPercent = 80.0,
                    TnRemovalPercent = 25.0,
                    TpRemovalPercent = 25.0,
                    TolerableSoilLossTonsPerAcYr = 5.0,
                    DefaultRFactor = 190.0,
                    VolumeControlRequired = false,
                },
                ["VA"] = new StateComplianceConfig
                {
                    Code = "VA",
                    Name = "Virginia",
                    RegulatoryBody = "VA DEQ",
                    DesignStormInches = 1.0,
                    WqVolumeFactorInches = 1.0,
                    PeakAttenuationPercent = 100.0,
                    DrawdownMinHours = 48.0,
                    DrawdownMaxHours = 72.0,
                    TssRemovalPercent = 80.0,
                    TnRemovalPercent = 0.0,
                    TpRemovalPercent = 50.0,
                    TolerableSoilLossTonsPerAcYr = 5.0,
                    DefaultRFactor = 180.0,
                    VolumeControlRequired = true,
                },
                ["FL"] = new StateComplianceConfig
                {
                    Code = "FL",
                    Name = "Florida",
                    RegulatoryBody = "FDEP",
                    DesignStormInches = 1.0,
                    WqVolumeFactorInches = 1.0,
                    PeakAttenuationPercent = 100.0,
                    DrawdownMinHours = 48.0,
                    DrawdownMaxHours = 72.0,
                    TssRemovalPercent = 80.0,
                    TnRemovalPercent = 50.0,
                    TpRemovalPercent = 50.0,
                    TolerableSoilLossTonsPerAcYr = 5.0,
                    DefaultRFactor = 230.0,
                    VolumeControlRequired = true,
                },
                ["TX"] = new StateComplianceConfig
                {
                    Code = "TX",
                    Name = "Texas",
                    RegulatoryBody = "TCEQ",
                    DesignStormInches = 1.5,
                    WqVolumeFactorInches = 1.5,
                    PeakAttenuationPercent = 100.0,
                    DrawdownMinHours = 48.0,
                    DrawdownMaxHours = 72.0,
                    TssRemovalPercent = 80.0,
                    TnRemovalPercent = 30.0,
                    TpRemovalPercent = 30.0,
                    TolerableSoilLossTonsPerAcYr = 5.0,
                    DefaultRFactor = 175.0,
                    VolumeControlRequired = false,
                },
                ["CA"] = new StateComplianceConfig
                {
                    Code = "CA",
                    Name = "California",
                    RegulatoryBody = "SWRCB / Regional Water Boards",
                    DesignStormInches = 0.75,
                    WqVolumeFactorInches = 0.75,
                    PeakAttenuationPercent = 100.0,
                    DrawdownMinHours = 48.0,
                    DrawdownMaxHours = 72.0,
                    TssRemovalPercent = 80.0,
                    TnRemovalPercent = 25.0,
                    TpRemovalPercent = 30.0,
                    TolerableSoilLossTonsPerAcYr = 5.0,
                    DefaultRFactor = 30.0,
                    VolumeControlRequired = true,
                },
                ["NY"] = new StateComplianceConfig
                {
                    Code = "NY",
                    Name = "New York",
                    RegulatoryBody = "NYSDEC",
                    DesignStormInches = 1.0,
                    WqVolumeFactorInches = 1.0,
                    PeakAttenuationPercent = 100.0,
                    DrawdownMinHours = 48.0,
                    DrawdownMaxHours = 72.0,
                    TssRemovalPercent = 80.0,
                    TnRemovalPercent = 30.0,
                    TpRemovalPercent = 40.0,
                    RoadwayTssRemovalPercent = 40.0,
                    TolerableSoilLossTonsPerAcYr = 5.0,
                    DefaultRFactor = 100.0,
                    VolumeControlRequired = true,
                },
                [DefaultCode] = new StateComplianceConfig
                {
                    Code = DefaultCode,
                    Name = "United States (EPA default)",
                    RegulatoryBody = "U.S. EPA",
                    DesignStormInches = 1.0,
                    WqVolumeFactorInches = 1.0,
                    PeakAttenuationPercent = 100.0,
                    DrawdownMinHours = 48.0,
                    DrawdownMaxHours = 72.0,
                    TssRemovalPercent = 80.0,
                    TnRemovalPercent = 25.0,
                    TpRemovalPercent = 25.0,
                    TolerableSoilLossTonsPerAcYr = 5.0,
                    DefaultRFactor = 180.0,
                    VolumeControlRequired = true,
                },
            };

        /// <summary>Returns the config for <paramref name="stateCode"/> or DEFAULT.</summary>
        public static StateComplianceConfig Get(string stateCode)
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