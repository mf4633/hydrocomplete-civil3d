using System;
using System.Collections.Generic;

namespace HydroComplete.Engine
{
    public static partial class StateCompliance
    {
        /// <summary>
        /// 24-hour peak-control storm depths (inches) keyed by return period label.
        /// Values for prompt states match hc-refactored StateConfigurations.js;
        /// all other codes use EPA DEFAULT depths.
        /// </summary>
        private static readonly Dictionary<string, IReadOnlyDictionary<string, double>> PeakStormDepthTables =
            new Dictionary<string, IReadOnlyDictionary<string, double>>(StringComparer.OrdinalIgnoreCase)
            {
                ["NC"] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["2-year"] = 3.0,
                    ["10-year"] = 4.5,
                    ["25-year"] = 5.5,
                    ["100-year"] = 7.2,
                },
                ["SC"] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["2-year"] = 3.2,
                    ["10-year"] = 5.0,
                    ["25-year"] = 6.0,
                    ["100-year"] = 8.0,
                },
                ["VA"] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["2-year"] = 3.0,
                    ["10-year"] = 4.8,
                    ["25-year"] = 5.8,
                    ["100-year"] = 7.5,
                },
                ["FL"] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["2-year"] = 3.5,
                    ["10-year"] = 5.2,
                    ["25-year"] = 6.2,
                    ["100-year"] = 8.0,
                },
                ["TX"] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["2-year"] = 3.2,
                    ["10-year"] = 4.7,
                    ["25-year"] = 5.7,
                    ["100-year"] = 7.5,
                },
                ["CA"] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["2-year"] = 2.0,
                    ["10-year"] = 3.5,
                    ["25-year"] = 4.5,
                    ["100-year"] = 6.0,
                },
                ["NY"] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["2-year"] = 3.5,
                    ["10-year"] = 5.5,
                    ["25-year"] = 6.5,
                    ["100-year"] = 8.5,
                },
                [DefaultCode] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["2-year"] = 3.0,
                    ["10-year"] = 4.5,
                    ["25-year"] = 5.5,
                    ["100-year"] = 7.0,
                },
            };

        /// <summary>
        /// Peak-control storm suite (24-hr depths, inches) for pre/post comparison.
        /// </summary>
        public static IReadOnlyDictionary<string, double> GetPeakStormSuite(StateComplianceConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            if (PeakStormDepthTables.TryGetValue(config.Code, out IReadOnlyDictionary<string, double>? suite))
                return suite;

            return PeakStormDepthTables[DefaultCode];
        }
    }
}