using System;
using System.Collections.Generic;
using System.Linq;

namespace HydroComplete.Engine
{
    public enum ComplianceStatus
    {
        Pass,
        Fail,
        Review,
        Incomplete,
        Info,
    }

    public sealed class ComplianceCriterion
    {
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public string Required { get; set; } = "";
        public string Actual { get; set; } = "";
        public ComplianceStatus Status { get; set; }
        public string Authority { get; set; } = "";
        public string Notes { get; set; } = "";
        public List<CalcStep> Steps { get; } = new List<CalcStep>();
    }

    public sealed class ComplianceReport : TracedResult
    {
        public string State { get; set; } = "";
        public string RegulatoryBody { get; set; } = "";
        public string DevelopmentType { get; set; } = "residential";
        public bool OverallPass { get; set; }
        public List<ComplianceCriterion> Criteria { get; } = new List<ComplianceCriterion>();
        public List<string> Warnings { get; } = new List<string>();
        public List<string> Recommendations { get; } = new List<string>();
        public string? Error { get; set; }
    }

    public sealed class BmpEfficiencyInput
    {
        public string Type { get; set; } = "";
        public double TssRemovalPercent { get; set; }
        public double TnRemovalPercent { get; set; }
        public double TpRemovalPercent { get; set; }
        public double? DrawdownHours { get; set; }
    }

    public sealed class WatershedSedimentInput
    {
        public string Name { get; set; } = "";
        public string RiskLevel { get; set; } = "";
    }

    public sealed class ComplianceAnalysisResults
    {
        public WaterQualityComplianceInput? WaterQuality { get; set; }
        public HydrologyComplianceInput? Hydrology { get; set; }
        public SedimentComplianceInput? Sediment { get; set; }
    }

    public sealed class WaterQualityComplianceInput
    {
        public int BmpCount { get; set; }
        public List<BmpEfficiencyInput> BmpEfficiency { get; set; } = new List<BmpEfficiencyInput>();
        public double? WqvProvidedCf { get; set; }
        public double? WqvRequiredCf { get; set; }
        public double? DrawdownHours { get; set; }
        public bool HasInfiltrationBmp { get; set; }
    }

    public sealed class HydrologyComplianceInput
    {
        public bool HasDetention { get; set; }
        public double? PrePeakCfs { get; set; }
        public double? PostPeakCfs { get; set; }
    }

    public sealed class SedimentComplianceInput
    {
        public double TotalSoilLossTonsPerAcYr { get; set; }
        public int SedimentControlCount { get; set; }
        public List<WatershedSedimentInput> WatershedResults { get; set; } = new List<WatershedSedimentInput>();
    }

    /// <summary>
    /// Checks analysis results against embedded state regulatory requirements.
    /// Ported from HydroComplete Pro ComplianceChecker.js.
    /// </summary>
    public static class ComplianceChecker
    {
        private static readonly HashSet<string> InfiltrationTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bioretention", "permeable", "infiltration", "permeable-pavement", "dry-well",
        };

        private static readonly HashSet<string> PondTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "wet-pond", "pond", "dry-pond", "detention",
        };

        /// <summary>
        /// Run full compliance check against state requirements.
        /// </summary>
        public static ComplianceReport CheckCompliance(
            ComplianceAnalysisResults results,
            string stateCode = "NC",
            string developmentType = "residential")
        {
            if (results == null) throw new ArgumentNullException(nameof(results));

            StateComplianceConfig config = StateCompliance.Get(stateCode);
            var report = new ComplianceReport
            {
                State = config.Name,
                RegulatoryBody = config.RegulatoryBody,
                DevelopmentType = developmentType ?? "residential",
                OverallPass = true,
            };

            report.Steps.Add(new CalcStep("state", 0, "", $"{config.Code} — {config.Name}"));

            CheckTssRemoval(report, results, config, report.DevelopmentType);
            CheckNutrientRemoval(report, results, config, "TN", config.TnRemovalPercent);
            CheckNutrientRemoval(report, results, config, "TP", config.TpRemovalPercent);
            CheckVolumeControl(report, results, config);
            CheckPeakFlowControl(report, results, config);
            CheckErosionControl(report, results, config);
            CheckDrawdownTimes(report, results, config);

            report.OverallPass = report.Criteria.All(c =>
                c.Status == ComplianceStatus.Pass || c.Status == ComplianceStatus.Info);

            if (!report.OverallPass)
                GenerateRecommendations(report);

            return report;
        }

        /// <summary>
        /// Treatment-train sequential removal: 1 - product(1 - eff_i/100).
        /// </summary>
        public static double? CombinedRemovalPercent(IEnumerable<double> efficiencies)
        {
            double product = 1.0;
            bool any = false;
            foreach (double eff in efficiencies)
            {
                if (double.IsNaN(eff)) continue;
                any = true;
                product *= 1.0 - eff / 100.0;
            }

            return any ? (1.0 - product) * 100.0 : null;
        }

        private static void CheckTssRemoval(
            ComplianceReport report,
            ComplianceAnalysisResults results,
            StateComplianceConfig config,
            string developmentType)
        {
            double required = StateCompliance.RequiredTssPercent(config, developmentType);
            double? actual = null;

            if (results.WaterQuality?.BmpEfficiency.Count > 0)
            {
                actual = CombinedRemovalPercent(
                    results.WaterQuality.BmpEfficiency.Select(b => b.TssRemovalPercent));
            }

            var criterion = new ComplianceCriterion
            {
                Name = "TSS Removal",
                Category = "Water Quality",
                Required = $"{required:0.#}%",
                Actual = actual.HasValue ? $"{actual.Value:0.#}%" : "Not calculated",
                Status = actual == null ? ComplianceStatus.Incomplete
                    : actual.Value >= required ? ComplianceStatus.Pass : ComplianceStatus.Fail,
                Authority = config.RegulatoryBody,
                Notes = $"{required:0.#}% TSS removal required for {developmentType} development",
            };

            if (actual.HasValue)
            {
                criterion.Steps.Add(new CalcStep("required_TSS", required, "%", "state threshold"));
                criterion.Steps.Add(new CalcStep("actual_TSS", actual.Value, "%",
                    "1 - product(1 - BMP efficiency)"));
            }

            report.Criteria.Add(criterion);
        }

        private static void CheckNutrientRemoval(
            ComplianceReport report,
            ComplianceAnalysisResults results,
            StateComplianceConfig config,
            string pollutant,
            double required)
        {
            if (required <= 0) return;

            double? actual = null;
            if (results.WaterQuality?.BmpEfficiency.Count > 0)
            {
                IEnumerable<double> values = pollutant == "TN"
                    ? results.WaterQuality.BmpEfficiency.Select(b => b.TnRemovalPercent)
                    : results.WaterQuality.BmpEfficiency.Select(b => b.TpRemovalPercent);
                actual = CombinedRemovalPercent(values);
            }

            report.Criteria.Add(new ComplianceCriterion
            {
                Name = $"{pollutant} Removal",
                Category = "Water Quality",
                Required = $"{required:0.#}%",
                Actual = actual.HasValue ? $"{actual.Value:0.#}%" : "Not calculated",
                Status = actual == null ? ComplianceStatus.Incomplete
                    : actual.Value >= required ? ComplianceStatus.Pass : ComplianceStatus.Fail,
                Authority = config.RegulatoryBody,
                Notes = $"{pollutant} removal target for post-construction stormwater management",
            });
        }

        private static void CheckVolumeControl(
            ComplianceReport report,
            ComplianceAnalysisResults results,
            StateComplianceConfig config)
        {
            if (!config.VolumeControlRequired && results.WaterQuality?.WqvRequiredCf == null)
                return;

            WaterQualityComplianceInput? wq = results.WaterQuality;
            bool hasBmps = wq != null && wq.BmpCount > 0;
            bool hasInfiltration = wq != null && (
                wq.HasInfiltrationBmp
                || wq.BmpEfficiency.Any(b => InfiltrationTypes.Contains(b.Type)));

            ComplianceStatus status;
            string actual;

            if (wq?.WqvRequiredCf > 0 && wq.WqvProvidedCf.HasValue)
            {
                double provided = wq.WqvProvidedCf.Value;
                double required = wq.WqvRequiredCf.Value;
                bool pass = provided >= required - 1e-6;
                status = pass ? ComplianceStatus.Pass : ComplianceStatus.Fail;
                actual = $"{provided:0} cf provided / {required:0} cf required";

                var criterion = new ComplianceCriterion
                {
                    Name = "Volume Control (WQV)",
                    Category = "Volume Management",
                    Required = $"{config.WqVolumeFactorInches:0.##}\" storm ({required:0} cf)",
                    Actual = actual,
                    Status = status,
                    Authority = config.RegulatoryBody,
                    Notes = $"First {config.WqVolumeFactorInches:0.##} inch runoff capture",
                };
                criterion.Steps.Add(new CalcStep("WQV_required", required, "cf", "Rv * storm * area"));
                criterion.Steps.Add(new CalcStep("WQV_provided", provided, "cf", "BMP storage"));
                report.Criteria.Add(criterion);
                return;
            }

            status = hasInfiltration ? ComplianceStatus.Pass
                : hasBmps ? ComplianceStatus.Review : ComplianceStatus.Fail;
            actual = hasInfiltration ? "Infiltration BMPs present"
                : hasBmps ? "BMPs present (verify infiltration capacity)"
                : "No volume reduction BMPs";

            report.Criteria.Add(new ComplianceCriterion
            {
                Name = "Volume Control (WQ Storm)",
                Category = "Volume Management",
                Required = $"First {config.WqVolumeFactorInches:0.##}\" of rainfall",
                Actual = actual,
                Status = status,
                Authority = config.RegulatoryBody,
                Notes = config.VolumeControlRequired
                    ? "Volume reduction required statewide"
                    : "Verify local volume requirements",
            });
        }

        private static void CheckPeakFlowControl(
            ComplianceReport report,
            ComplianceAnalysisResults results,
            StateComplianceConfig config)
        {
            HydrologyComplianceInput? hydro = results.Hydrology;
            bool hasDetention = hydro?.HasDetention == true;
            double? pre = hydro?.PrePeakCfs;
            double? post = hydro?.PostPeakCfs;

            if (pre > 0 && post.HasValue)
            {
                double attenuation = Math.Max(0.0, (1.0 - post.Value / pre.Value) * 100.0);
                bool pass = post.Value <= pre.Value + 1e-6;
                var criterion = new ComplianceCriterion
                {
                    Name = "Peak Flow Attenuation",
                    Category = "Flow Attenuation",
                    Required = $"<= pre-dev peak ({config.PeakAttenuationPercent:0.#}% match)",
                    Actual = $"Post {post.Value:0.##} cfs vs pre {pre.Value:0.##} cfs ({attenuation:0.#}% reduction)",
                    Status = pass ? ComplianceStatus.Pass : ComplianceStatus.Fail,
                    Authority = config.RegulatoryBody,
                    Notes = "Post-development peak shall not exceed pre-development",
                };
                criterion.Steps.Add(new CalcStep("Q_pre", pre.Value, "cfs", "pre-development peak"));
                criterion.Steps.Add(new CalcStep("Q_post", post.Value, "cfs", "post-development peak"));
                criterion.Steps.Add(new CalcStep("attenuation", attenuation, "%", "(1 - Qpost/Qpre)*100"));
                report.Criteria.Add(criterion);
                return;
            }

            report.Criteria.Add(new ComplianceCriterion
            {
                Name = "Peak Flow Control",
                Category = "Flow Attenuation",
                Required = "Match pre-development peak",
                Actual = hasDetention ? "Detention provided (verify peaks)" : "No detention facility",
                Status = hasDetention ? ComplianceStatus.Review : ComplianceStatus.Fail,
                Authority = config.RegulatoryBody,
                Notes = "Detention required for required design storms",
            });
        }

        private static void CheckErosionControl(
            ComplianceReport report,
            ComplianceAnalysisResults results,
            StateComplianceConfig config)
        {
            SedimentComplianceInput? sediment = results.Sediment;
            if (sediment == null) return;

            double tolerable = config.TolerableSoilLossTonsPerAcYr;
            double loss = sediment.TotalSoilLossTonsPerAcYr;
            bool pass = loss <= tolerable;

            var soilCriterion = new ComplianceCriterion
            {
                Name = "Soil Loss (Tolerable T)",
                Category = "Erosion Control",
                Required = $"< {tolerable:0.#} tons/ac/yr",
                Actual = $"{loss:0.##} tons/ac/yr",
                Status = pass ? ComplianceStatus.Pass : ComplianceStatus.Fail,
                Authority = "USDA NRCS Soil Conservation Standards",
                Notes = pass
                    ? "Soil loss within tolerable limits"
                    : "Soil loss exceeds tolerable limit. Additional erosion controls required.",
            };
            soilCriterion.Steps.Add(new CalcStep("soil_loss", loss, "tons/ac/yr", "RUSLE/MUSLE"));
            soilCriterion.Steps.Add(new CalcStep("tolerable_T", tolerable, "tons/ac/yr", "state/USDA default"));
            report.Criteria.Add(soilCriterion);

            bool highRisk = sediment.WatershedResults.Any(w =>
                string.Equals(w.RiskLevel, "High", StringComparison.OrdinalIgnoreCase));
            if (sediment.SedimentControlCount == 0 && highRisk)
            {
                report.Criteria.Add(new ComplianceCriterion
                {
                    Name = "Construction Sediment Controls",
                    Category = "Erosion Control",
                    Required = "Sediment basins for high-risk areas",
                    Actual = "No sediment control structures in model",
                    Status = ComplianceStatus.Fail,
                    Authority = config.RegulatoryBody,
                    Notes = "High-risk erosion areas require sediment basins or equivalent controls",
                });
            }
        }

        private static void CheckDrawdownTimes(
            ComplianceReport report,
            ComplianceAnalysisResults results,
            StateComplianceConfig config)
        {
            WaterQualityComplianceInput? wq = results.WaterQuality;
            if (wq == null) return;

            double? drawdown = wq.DrawdownHours;
            if (drawdown.HasValue)
            {
                // A collapsed window (Max <= Min, i.e. min==max in the config) admits only a
                // single exact value, so a continuously-computed drawdown could essentially
                // never pass. Treat that as a data-configuration problem and flag for manual
                // review rather than emitting a spurious Pass/Fail.
                bool collapsedWindow = config.DrawdownMaxHours <= config.DrawdownMinHours;
                bool pass = drawdown.Value >= config.DrawdownMinHours
                            && drawdown.Value <= config.DrawdownMaxHours;
                ComplianceStatus drawdownStatus = collapsedWindow
                    ? ComplianceStatus.Review
                    : (pass ? ComplianceStatus.Pass : ComplianceStatus.Fail);
                var criterion = new ComplianceCriterion
                {
                    Name = "BMP Drawdown Time",
                    Category = "Water Quality",
                    Required = $"{config.DrawdownMinHours:0.#}-{config.DrawdownMaxHours:0.#} hours",
                    Actual = $"{drawdown.Value:0.#} hours",
                    Status = drawdownStatus,
                    Authority = config.RegulatoryBody,
                    Notes = collapsedWindow
                        ? "Regulatory drawdown window is a single value in the config; verify min/max hours against the source table."
                        : "Water quality volume drawdown within regulatory window",
                };
                criterion.Steps.Add(new CalcStep("drawdown", drawdown.Value, "hr", "BMP drain time"));
                report.Criteria.Add(criterion);
                return;
            }

            bool hasPonds = wq.BmpEfficiency.Any(b => PondTypes.Contains(b.Type));
            if (hasPonds)
            {
                report.Criteria.Add(new ComplianceCriterion
                {
                    Name = "BMP Drawdown Time",
                    Category = "Water Quality",
                    Required = $"{config.DrawdownMinHours:0.#}-{config.DrawdownMaxHours:0.#} hours",
                    Actual = "Detention facilities present (verify drawdown)",
                    Status = ComplianceStatus.Review,
                    Authority = config.RegulatoryBody,
                    Notes = "Verify orifice sizing for WQV drawdown",
                });
            }
        }

        private static void GenerateRecommendations(ComplianceReport report)
        {
            foreach (ComplianceCriterion criterion in report.Criteria.Where(c => c.Status == ComplianceStatus.Fail))
            {
                switch (criterion.Category)
                {
                    case "Water Quality":
                        if (criterion.Name.IndexOf("TSS", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            report.Recommendations.Add(
                                "Consider adding or upsizing bioretention cells for improved TSS removal.");
                            report.Recommendations.Add(
                                "A treatment train (e.g., swale + bioretention) can achieve higher combined removal.");
                        }

                        if (criterion.Name.IndexOf("TN", StringComparison.OrdinalIgnoreCase) >= 0
                            || criterion.Name.IndexOf("TP", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            report.Recommendations.Add(
                                "Constructed wetlands and bioretention with IWS provide the highest nutrient removal.");
                        }

                        if (criterion.Name.IndexOf("Drawdown", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            report.Recommendations.Add(
                                "Resize outlet orifice to meet drawdown time for the water quality volume.");
                        }

                        break;
                    case "Volume Management":
                        report.Recommendations.Add(
                            "Add infiltration-based BMPs (bioretention, permeable pavement) to meet volume reduction.");
                        break;
                    case "Flow Attenuation":
                        report.Recommendations.Add(
                            "Add detention facilities to attenuate peak flows to pre-development levels.");
                        break;
                    case "Erosion Control":
                        report.Recommendations.Add(
                            "Implement temporary sediment basins during construction phase.");
                        report.Recommendations.Add(
                            "Consider phased grading to minimize exposed area at any given time.");
                        break;
                }
            }

            var distinct = report.Recommendations.Distinct(StringComparer.Ordinal).ToList();
            report.Recommendations.Clear();
            report.Recommendations.AddRange(distinct);
        }
    }
}