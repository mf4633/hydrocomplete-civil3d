using System;
using System.Collections.Generic;
using System.Linq;

namespace HydroComplete.Engine
{
    /// <summary>
    /// IDEAL water-quality engine: SCS runoff, WQV sizing, pollutant loading,
    /// BMP treatment, treatment trains, and first-flush partitioning.
    /// Every public calculation returns a <see cref="TracedResult"/> with formula steps.
    /// </summary>
    public static class WaterQualityEngine
    {
        public const double CfPerAcreInch = 3630.0;
        private const double DefaultFirstFlushVolumeFraction = 0.20;

        public sealed class WqvResult : TracedResult
        {
            public double TotalAreaAcres { get; set; }
            public double ImperviousPercent { get; set; }
            public double RunoffCoefficientRv { get; set; }
            public double DesignStormInches { get; set; }
            public double WqvCf { get; set; }
            public double WqvAcreFt { get; set; }
            public double WqvGallons { get; set; }
        }

        public sealed class ScsRunoffResult : TracedResult
        {
            public double RainfallIn { get; set; }
            public double CurveNumber { get; set; }
            public double AdjustedCurveNumber { get; set; }
            public double PotentialRetentionIn { get; set; }
            public double InitialAbstractionIn { get; set; }
            public double RunoffDepthIn { get; set; }
            public int AntecedentDryDays { get; set; }
            public AntecedentMoistureCondition MoistureCondition { get; set; }
        }

        public sealed class BuildupResult : TracedResult
        {
            public string Pollutant { get; set; } = "";
            public string LandUse { get; set; } = "";
            public int AntecedentDryDays { get; set; }
            public double DrainageAreaAcres { get; set; }
            public double BuildupPerAcre { get; set; }
            public double TotalBuildupLbs { get; set; }
        }

        public sealed class WashoffResult : TracedResult
        {
            public string Pollutant { get; set; } = "";
            public string LandUse { get; set; } = "";
            public double RunoffDepthIn { get; set; }
            public double AvailableBuildupLbs { get; set; }
            public double WashoffFraction { get; set; }
            public double WashoffLoadLbs { get; set; }
        }

        public sealed class EmcLoadResult : TracedResult
        {
            public string Pollutant { get; set; } = "";
            public string LandUse { get; set; } = "";
            public double EmcMgPerL { get; set; }
            public double RunoffDepthIn { get; set; }
            public double DrainageAreaAcres { get; set; }
            public double RunoffVolumeGallons { get; set; }
            public double EmcLoadLbs { get; set; }
        }

        public sealed class EventPollutantLoadResult : TracedResult
        {
            public string LandUse { get; set; } = "";
            public double RunoffDepthIn { get; set; }
            public double DrainageAreaAcres { get; set; }
            public int AntecedentDryDays { get; set; }
            public Dictionary<string, double> LoadsLbs { get; } = new Dictionary<string, double>();
        }

        public sealed class BmpTreatmentResult : TracedResult
        {
            public string BmpType { get; set; } = "";
            public string BmpName { get; set; } = "";
            public Dictionary<string, double> InfluentLbs { get; } = new Dictionary<string, double>();
            public Dictionary<string, double> TreatedLbs { get; } = new Dictionary<string, double>();
            public Dictionary<string, double> RemovedLbs { get; } = new Dictionary<string, double>();
            public Dictionary<string, double> RemovalEfficiency { get; } = new Dictionary<string, double>();
        }

        public sealed class TreatmentTrainBmpStep
        {
            public string BmpType { get; set; } = "";
            public Dictionary<string, double> InfluentLbs { get; } = new Dictionary<string, double>();
            public Dictionary<string, double> EffluentLbs { get; } = new Dictionary<string, double>();
            public Dictionary<string, double> RemovedLbs { get; } = new Dictionary<string, double>();
        }

        public sealed class TreatmentTrainResult : TracedResult
        {
            public int ChainLength { get; set; }
            public List<TreatmentTrainBmpStep> BmpSteps { get; } = new List<TreatmentTrainBmpStep>();
            public Dictionary<string, double> InitialLoadsLbs { get; } = new Dictionary<string, double>();
            public Dictionary<string, double> FinalEffluentLbs { get; } = new Dictionary<string, double>();
            public Dictionary<string, double> TotalRemovedLbs { get; } = new Dictionary<string, double>();
            public Dictionary<string, double> OverallRemovalEfficiency { get; } = new Dictionary<string, double>();
        }

        public sealed class BmpSizingResult : TracedResult
        {
            public string BmpType { get; set; } = "";
            public string BmpName { get; set; } = "";
            public double TotalWqvCf { get; set; }
            public double TreatedVolumeCf { get; set; }
            public double SurfaceAreaSf { get; set; }
            public double FootprintPercent { get; set; }
            public double? LengthFt { get; set; }
            public double? WidthFt { get; set; }
            public double VolumeReductionCredit { get; set; }
        }

        public sealed class FirstFlushResult : TracedResult
        {
            public double FirstFlushVolumeFraction { get; set; }
            public double TotalRunoffVolumeCf { get; set; }
            public double FirstFlushVolumeCf { get; set; }
            public double CaptureVolumeForSizingCf { get; set; }
            public Dictionary<string, double> TotalLoadsLbs { get; } = new Dictionary<string, double>();
            public Dictionary<string, double> FirstFlushLoadsLbs { get; } = new Dictionary<string, double>();
            public Dictionary<string, double> FirstFlushConcentrationsMgPerL { get; } = new Dictionary<string, double>();
        }

        /// <summary>
        /// Runoff coefficient from percent impervious: Rv = 0.05 + 0.009 * I.
        /// </summary>
        public static double RunoffCoefficientFromImpervious(double imperviousPercent)
        {
            if (imperviousPercent < 0 || imperviousPercent > 100)
                throw new ArgumentOutOfRangeException(nameof(imperviousPercent));
            return 0.05 + 0.009 * imperviousPercent;
        }

        /// <summary>Infer percent impervious from Rational C (inverse of Rv equation, clamped).</summary>
        public static double ImperviousFromRunoffC(double runoffC)
        {
            if (runoffC < 0 || runoffC > 1)
                throw new ArgumentOutOfRangeException(nameof(runoffC));
            double i = (runoffC - 0.05) / 0.009;
            return Math.Min(100.0, Math.Max(0.0, i));
        }

        /// <summary>WQV = Rv * P * A * 3630 cf/ac-in.</summary>
        public static WqvResult ComputeWqv(
            double totalAreaAcres,
            double designStormInches,
            double runoffCoefficientRv)
        {
            if (totalAreaAcres < 0) throw new ArgumentOutOfRangeException(nameof(totalAreaAcres));
            if (designStormInches < 0) throw new ArgumentOutOfRangeException(nameof(designStormInches));
            if (runoffCoefficientRv < 0 || runoffCoefficientRv > 1)
                throw new ArgumentOutOfRangeException(nameof(runoffCoefficientRv));

            double wqvCf = runoffCoefficientRv * designStormInches * totalAreaAcres * CfPerAcreInch;
            double impervious = ImperviousFromRunoffC(runoffCoefficientRv);

            var result = new WqvResult
            {
                TotalAreaAcres = totalAreaAcres,
                ImperviousPercent = impervious,
                RunoffCoefficientRv = runoffCoefficientRv,
                DesignStormInches = designStormInches,
                WqvCf = wqvCf,
                WqvAcreFt = wqvCf / BmpLibrary.SqFtPerAcre,
                WqvGallons = wqvCf / 0.133681,
            };

            result.Steps.Add(new CalcStep("Rv", runoffCoefficientRv, "", "0.05 + 0.009*I"));
            result.Steps.Add(new CalcStep("P", designStormInches, "in", "WQ design storm"));
            result.Steps.Add(new CalcStep("A", totalAreaAcres, "ac", "drainage area"));
            result.Steps.Add(new CalcStep("WQV", wqvCf, "cf", "Rv*P*A*3630"));

            return result;
        }

        /// <summary>Composite WQV from catchments using area-weighted Rv.</summary>
        public static WqvResult ComputeWqvFromCatchments(
            IEnumerable<Catchment> catchments,
            double designStormInches)
        {
            if (catchments == null) throw new ArgumentNullException(nameof(catchments));

            double sumA = 0.0;
            double sumCa = 0.0;
            foreach (Catchment cm in catchments)
            {
                sumA += cm.AreaAcres;
                sumCa += cm.RunoffC * cm.AreaAcres;
            }

            double rv = sumA > 0 ? sumCa / sumA : 0.0;
            return ComputeWqv(sumA, designStormInches, rv);
        }

        /// <summary>
        /// Water quality volume from impervious percent: WQV = P × Rv × A.
        /// </summary>
        public static WqvResult CalculateWqv(
            double designRainfallIn,
            double drainageAreaAcres,
            double imperviousPercent)
        {
            double rv = RunoffCoefficientFromImpervious(imperviousPercent);
            WqvResult result = ComputeWqv(drainageAreaAcres, designRainfallIn, rv);
            result.ImperviousPercent = imperviousPercent;
            result.Steps.Insert(0, new CalcStep("I", imperviousPercent, "%",
                $"Rv = 0.05 + 0.009*I = 0.05 + 0.009*{imperviousPercent:0.##}"));
            return result;
        }

        /// <summary>
        /// SCS curve-number runoff with initial abstraction Ia = 0.2S and
        /// Q = (P − Ia)² / (P − Ia + S) when P &gt; Ia.
        /// </summary>
        public static ScsRunoffResult CalculateScsRunoff(
            double rainfallIn,
            double curveNumber,
            int antecedentDryDays,
            AntecedentMoistureCondition moistureCondition = AntecedentMoistureCondition.Auto)
        {
            if (rainfallIn < 0) throw new ArgumentOutOfRangeException(nameof(rainfallIn));
            if (curveNumber < 40 || curveNumber > 98)
                throw new ArgumentOutOfRangeException(nameof(curveNumber), "CN must be 40..98.");
            if (antecedentDryDays < 0) throw new ArgumentOutOfRangeException(nameof(antecedentDryDays));

            AntecedentMoistureCondition amc = ResolveMoistureCondition(moistureCondition, antecedentDryDays);
            double adjustedCn = AdjustCurveNumber(curveNumber, amc);

            double s = (1000.0 / adjustedCn) - 10.0;
            double ia = 0.2 * s;

            var result = new ScsRunoffResult
            {
                RainfallIn = rainfallIn,
                CurveNumber = curveNumber,
                AdjustedCurveNumber = adjustedCn,
                PotentialRetentionIn = s,
                InitialAbstractionIn = ia,
                AntecedentDryDays = antecedentDryDays,
                MoistureCondition = amc,
            };

            result.Steps.Add(new CalcStep("CN_adj", adjustedCn, "-",
                $"AMC {amc} from CN={curveNumber:0.##} ({antecedentDryDays} antecedent dry days)"));
            result.Steps.Add(new CalcStep("S", s, "in", "S = (1000/CN) - 10"));
            result.Steps.Add(new CalcStep("Ia", ia, "in", "Ia = 0.2*S"));

            if (rainfallIn <= ia)
            {
                result.RunoffDepthIn = 0.0;
                result.Steps.Add(new CalcStep("Q", 0.0, "in", $"P={rainfallIn:0.###} <= Ia={ia:0.###} (no runoff)"));
                return result;
            }

            double excess = rainfallIn - ia;
            double runoff = (excess * excess) / (excess + s);
            result.RunoffDepthIn = runoff;
            result.Steps.Add(new CalcStep("Q", runoff, "in",
                $"Q = (P-Ia)^2/(P-Ia+S) = ({rainfallIn:0.###}-{ia:0.###})^2/({excess:0.###}+{s:0.###})"));
            return result;
        }

        /// <summary>Exponential buildup: B(t) = Bmax × (1 − e^(−k·t)) × area.</summary>
        public static BuildupResult CalculateBuildup(
            int antecedentDryDays,
            string pollutant,
            double drainageAreaAcres,
            string landUse)
        {
            if (antecedentDryDays < 0) throw new ArgumentOutOfRangeException(nameof(antecedentDryDays));
            if (drainageAreaAcres < 0) throw new ArgumentOutOfRangeException(nameof(drainageAreaAcres));
            if (string.IsNullOrWhiteSpace(pollutant)) throw new ArgumentException("Pollutant is required.", nameof(pollutant));

            BuildupParameters p = BmpLibrary.GetBuildupParameters(landUse, pollutant);
            double perAcre = p.BmaxPerAcre * (1.0 - Math.Exp(-p.KPerDay * antecedentDryDays));
            double total = perAcre * drainageAreaAcres;

            var result = new BuildupResult
            {
                Pollutant = pollutant,
                LandUse = landUse,
                AntecedentDryDays = antecedentDryDays,
                DrainageAreaAcres = drainageAreaAcres,
                BuildupPerAcre = perAcre,
                TotalBuildupLbs = total,
            };

            result.Steps.Add(new CalcStep("B/ac", perAcre, "lbs/ac",
                $"B(t) = Bmax*(1-exp(-k*t)) = {p.BmaxPerAcre:0.###}*(1-exp(-{p.KPerDay:0.###}*{antecedentDryDays}))"));
            result.Steps.Add(new CalcStep("B_total", total, "lbs", $"B_total = B/ac * A = {perAcre:0.###}*{drainageAreaAcres:0.###}"));
            return result;
        }

        /// <summary>Power-law washoff: W = B_avail × min(1, a·R^b).</summary>
        public static WashoffResult CalculateWashoff(
            double runoffDepthIn,
            double availableBuildupLbs,
            string pollutant,
            string landUse)
        {
            if (runoffDepthIn < 0) throw new ArgumentOutOfRangeException(nameof(runoffDepthIn));
            if (availableBuildupLbs < 0) throw new ArgumentOutOfRangeException(nameof(availableBuildupLbs));
            if (string.IsNullOrWhiteSpace(pollutant)) throw new ArgumentException("Pollutant is required.", nameof(pollutant));

            WashoffParameters p = BmpLibrary.GetWashoffParameters(landUse, pollutant);
            double fraction = Math.Min(1.0, p.A * Math.Pow(runoffDepthIn, p.B));
            double washoff = availableBuildupLbs * fraction;

            var result = new WashoffResult
            {
                Pollutant = pollutant,
                LandUse = landUse,
                RunoffDepthIn = runoffDepthIn,
                AvailableBuildupLbs = availableBuildupLbs,
                WashoffFraction = fraction,
                WashoffLoadLbs = washoff,
            };

            result.Steps.Add(new CalcStep("f_w", fraction, "-",
                $"f_w = min(1, a*R^b) = min(1, {p.A:0.###}*{runoffDepthIn:0.###}^{p.B:0.###})"));
            result.Steps.Add(new CalcStep("W", washoff, "lbs", $"W = B_avail*f_w = {availableBuildupLbs:0.###}*{fraction:0.###}"));
            return result;
        }

        /// <summary>EMC-based event load: L = EMC × V_gal × 8.34 / 10^6 (lbs).</summary>
        public static EmcLoadResult CalculateEmcLoad(
            string pollutant,
            string landUse,
            double runoffDepthIn,
            double drainageAreaAcres)
        {
            if (runoffDepthIn < 0) throw new ArgumentOutOfRangeException(nameof(runoffDepthIn));
            if (drainageAreaAcres < 0) throw new ArgumentOutOfRangeException(nameof(drainageAreaAcres));

            double emc = BmpLibrary.GetEmc(landUse, pollutant);
            double volumeCf = runoffDepthIn * drainageAreaAcres * BmpLibrary.SqFtPerAcre / BmpLibrary.InchesPerFoot;
            double volumeGal = volumeCf * BmpLibrary.GallonsPerCf;
            double load = emc * volumeGal * BmpLibrary.LbsPerGallon / BmpLibrary.MgPerLb;

            var result = new EmcLoadResult
            {
                Pollutant = pollutant,
                LandUse = landUse,
                EmcMgPerL = emc,
                RunoffDepthIn = runoffDepthIn,
                DrainageAreaAcres = drainageAreaAcres,
                RunoffVolumeGallons = volumeGal,
                EmcLoadLbs = load,
            };

            result.Steps.Add(new CalcStep("EMC", emc, "mg/L", $"EMC ({landUse}, {pollutant})"));
            result.Steps.Add(new CalcStep("V_runoff", volumeGal, "gal",
                $"V = Q*A*43560/12*7.48 = {runoffDepthIn:0.###}*{drainageAreaAcres:0.###}*43560/12*7.48"));
            result.Steps.Add(new CalcStep("L_EMC", load, "lbs",
                $"L = EMC*V_gal*8.34/1e6 = {emc:0.###}*{volumeGal:0.###}*8.34/1e6"));
            return result;
        }

        /// <summary>Total event pollutant loads: EMC transport + buildup/washoff for TSS, TN, TP.</summary>
        public static EventPollutantLoadResult CalculateEventPollutantLoads(
            double runoffDepthIn,
            double drainageAreaAcres,
            string landUse,
            int antecedentDryDays)
        {
            if (runoffDepthIn < 0) throw new ArgumentOutOfRangeException(nameof(runoffDepthIn));
            if (drainageAreaAcres < 0) throw new ArgumentOutOfRangeException(nameof(drainageAreaAcres));
            if (antecedentDryDays < 0) throw new ArgumentOutOfRangeException(nameof(antecedentDryDays));

            var result = new EventPollutantLoadResult
            {
                LandUse = landUse,
                RunoffDepthIn = runoffDepthIn,
                DrainageAreaAcres = drainageAreaAcres,
                AntecedentDryDays = antecedentDryDays,
            };

            foreach (string pollutant in Pollutant.Core)
            {
                EmcLoadResult emc = CalculateEmcLoad(pollutant, landUse, runoffDepthIn, drainageAreaAcres);
                BuildupResult buildup = CalculateBuildup(antecedentDryDays, pollutant, drainageAreaAcres, landUse);
                WashoffResult washoff = CalculateWashoff(runoffDepthIn, buildup.TotalBuildupLbs, pollutant, landUse);

                // The EMC transport load and the buildup/washoff load are two alternative
                // estimates of the SAME event pollutant mass; summing them double-counts.
                // Use the EMC estimate as the event load and report washoff separately.
                double total = emc.EmcLoadLbs;
                result.LoadsLbs[pollutant] = total;

                result.Steps.Add(new CalcStep($"{pollutant}_EMC", emc.EmcLoadLbs, "lbs", emc.Steps[emc.Steps.Count - 1].Formula));
                result.Steps.Add(new CalcStep($"{pollutant}_washoff", washoff.WashoffLoadLbs, "lbs", washoff.Steps[washoff.Steps.Count - 1].Formula));
                result.Steps.Add(new CalcStep($"{pollutant}_total", total, "lbs",
                    $"L_total = L_EMC = {emc.EmcLoadLbs:0.####} (washoff {washoff.WashoffLoadLbs:0.####} reported separately)"));
            }

            return result;
        }

        /// <summary>Single BMP mass-balance treatment using library trapping efficiencies.</summary>
        public static BmpTreatmentResult ApplyBmpTreatment(
            IDictionary<string, double> loadsLbs,
            string bmpType)
        {
            if (loadsLbs == null) throw new ArgumentNullException(nameof(loadsLbs));

            BmpDefinition bmp = BmpLibrary.GetBmp(bmpType);
            var result = new BmpTreatmentResult
            {
                BmpType = bmp.Key,
                BmpName = bmp.Name,
            };

            foreach (KeyValuePair<string, double> kv in loadsLbs)
            {
                double influent = kv.Value;
                double eta = bmp.TrappingEfficiency.TryGetValue(kv.Key, out double e) ? e : 0.0;
                double removed = influent * eta;
                double treated = influent - removed;

                result.InfluentLbs[kv.Key] = influent;
                result.RemovedLbs[kv.Key] = removed;
                result.TreatedLbs[kv.Key] = treated;
                result.RemovalEfficiency[kv.Key] = eta;

                result.Steps.Add(new CalcStep($"{kv.Key}_eta", eta, "-", $"{bmp.Name} trapping efficiency"));
                result.Steps.Add(new CalcStep($"{kv.Key}_removed", removed, "lbs",
                    $"removed = L_in*eta = {influent:0.####}*{eta:0.###}"));
                result.Steps.Add(new CalcStep($"{kv.Key}_effluent", treated, "lbs",
                    $"effluent = L_in*(1-eta) = {influent:0.####}*(1-{eta:0.###})"));
            }

            return result;
        }

        /// <summary>
        /// Series BMP removal: η_total = 1 − ∏(1 − η_i) applied sequentially via mass balance.
        /// </summary>
        public static TreatmentTrainResult ApplyTreatmentTrain(
            IDictionary<string, double> initialLoadsLbs,
            IEnumerable<string> bmpChain)
        {
            if (initialLoadsLbs == null) throw new ArgumentNullException(nameof(initialLoadsLbs));
            if (bmpChain == null) throw new ArgumentNullException(nameof(bmpChain));

            string[] chain = bmpChain.ToArray();
            if (chain.Length == 0) throw new ArgumentException("At least one BMP is required.", nameof(bmpChain));

            var result = new TreatmentTrainResult { ChainLength = chain.Length };
            foreach (KeyValuePair<string, double> kv in initialLoadsLbs)
            {
                result.InitialLoadsLbs[kv.Key] = kv.Value;
                result.TotalRemovedLbs[kv.Key] = 0.0;
            }

            var current = new Dictionary<string, double>();
            foreach (KeyValuePair<string, double> kv in initialLoadsLbs)
                current[kv.Key] = kv.Value;
            List<double> tssEtas = new List<double>();

            foreach (string bmpType in chain)
            {
                BmpTreatmentResult step = ApplyBmpTreatment(current, bmpType);
                var trainStep = new TreatmentTrainBmpStep { BmpType = bmpType };

                foreach (KeyValuePair<string, double> kv in step.InfluentLbs)
                {
                    trainStep.InfluentLbs[kv.Key] = kv.Value;
                    trainStep.EffluentLbs[kv.Key] = step.TreatedLbs[kv.Key];
                    trainStep.RemovedLbs[kv.Key] = step.RemovedLbs[kv.Key];
                    result.TotalRemovedLbs[kv.Key] += step.RemovedLbs[kv.Key];
                }

                result.Steps.Add(new CalcStep($"BMP_{bmpType}", chain.Length, "-", $"{bmpType}: series mass balance"));
                foreach (CalcStep calcStep in step.Steps)
                    result.Steps.Add(calcStep);

                if (step.RemovalEfficiency.TryGetValue(Pollutant.Tss, out double tssEta))
                    tssEtas.Add(tssEta);

                current = new Dictionary<string, double>(step.TreatedLbs);
                result.BmpSteps.Add(trainStep);
            }

            foreach (KeyValuePair<string, double> kv in result.InitialLoadsLbs)
            {
                result.FinalEffluentLbs[kv.Key] = current[kv.Key];
                result.OverallRemovalEfficiency[kv.Key] = kv.Value > 0
                    ? result.TotalRemovedLbs[kv.Key] / kv.Value
                    : 0.0;
            }

            if (tssEtas.Count > 0)
            {
                double product = 1.0;
                foreach (double eta in tssEtas)
                    product *= (1.0 - eta);

                double etaTotal = 1.0 - product;
                result.Steps.Add(new CalcStep("eta_total_TSS", etaTotal, "-",
                    $"eta_total = 1 - prod(1-eta_i) = 1 - {product:0.#####}"));
            }

            return result;
        }

        /// <summary>BMP sizing dispatcher: derives required volume/area/length from WQV per BMP type.</summary>
        public static BmpSizingResult SizeBmp(
            string bmpType,
            double designRainfallIn,
            double drainageAreaAcres,
            double imperviousPercent)
        {
            BmpDefinition bmp = BmpLibrary.GetBmp(bmpType);
            WqvResult wqv = CalculateWqv(designRainfallIn, drainageAreaAcres, imperviousPercent);
            double treatedVolume = wqv.WqvCf * (1.0 - bmp.VolumeReduction);

            var result = new BmpSizingResult
            {
                BmpType = bmp.Key,
                BmpName = bmp.Name,
                TotalWqvCf = wqv.WqvCf,
                TreatedVolumeCf = treatedVolume,
                VolumeReductionCredit = bmp.VolumeReduction,
            };

            foreach (CalcStep step in wqv.Steps)
                result.Steps.Add(step);

            result.Steps.Add(new CalcStep("V_treated", treatedVolume, "ft^3",
                $"V_treated = WQV*(1-VR) = {wqv.WqvCf:0.###}*(1-{bmp.VolumeReduction:0.###})"));

            double siteSf = drainageAreaAcres * BmpLibrary.SqFtPerAcre;
            double surfaceArea;
            double? lengthFt = null;
            double? widthFt = null;

            switch (bmp.Key)
            {
                case BmpType.Bioretention:
                    double ratio = bmp.SurfaceAreaRatio ?? 0.05;
                    surfaceArea = siteSf * ratio;
                    result.Steps.Add(new CalcStep("A_BMP", surfaceArea, "ft^2",
                        $"A = site*surface_ratio = {siteSf:0.###}*{ratio:0.###} (bioretention)"));
                    break;

                case BmpType.VegetatedSwale:
                    double bottomWidth = bmp.BottomWidthFt ?? 2.0;
                    double depth = bmp.DepthFt ?? 1.5;
                    double crossSection = bottomWidth * depth;
                    lengthFt = Math.Max(bmp.MinLengthFt ?? 50.0, treatedVolume / crossSection);
                    widthFt = bottomWidth;
                    surfaceArea = lengthFt.Value * bottomWidth;
                    result.Steps.Add(new CalcStep("L_swale", lengthFt.Value, "ft",
                        $"L = max(L_min, V/(b*d)) = max({bmp.MinLengthFt:0.###}, {treatedVolume:0.###}/({bottomWidth:0.###}*{depth:0.###}))"));
                    result.Steps.Add(new CalcStep("A_swale", surfaceArea, "ft^2", "A = L*b"));
                    break;

                case BmpType.SandFilter:
                    double avgDepth = bmp.AvgDepthFt ?? 2.5;
                    double areaFromVolume = treatedVolume / avgDepth;
                    if (bmp.SurfaceLoadingRateGalPerMinPerSf.HasValue && bmp.SurfaceLoadingRateGalPerMinPerSf.Value > 0)
                    {
                        double volumeGal = treatedVolume * BmpLibrary.GallonsPerCf;
                        double drawdownHr = 40.0;
                        double peakFlowGpm = volumeGal / (drawdownHr * 60.0);
                        double areaFromLoading = peakFlowGpm / bmp.SurfaceLoadingRateGalPerMinPerSf.Value;
                        surfaceArea = Math.Max(areaFromVolume, areaFromLoading);
                        result.Steps.Add(new CalcStep("A_loading", areaFromLoading, "ft^2",
                            $"A = Q_peak/SLR = {peakFlowGpm:0.###}/{bmp.SurfaceLoadingRateGalPerMinPerSf.Value:0.###} gpm/(gal/min/sf)"));
                    }
                    else
                    {
                        surfaceArea = areaFromVolume;
                    }

                    result.Steps.Add(new CalcStep("A_filter", surfaceArea, "ft^2",
                        $"A = max(V/d_avg, A_loading) with d_avg={avgDepth:0.###} ft"));
                    break;

                default:
                    double pondDepth = bmp.AvgDepthFt ?? 3.0;
                    surfaceArea = treatedVolume / pondDepth;
                    result.Steps.Add(new CalcStep("A_BMP", surfaceArea, "ft^2",
                        $"A = V_treated/d_avg = {treatedVolume:0.###}/{pondDepth:0.###}"));
                    break;
            }

            result.SurfaceAreaSf = surfaceArea;
            result.LengthFt = lengthFt;
            result.WidthFt = widthFt;
            result.FootprintPercent = siteSf > 0 ? (surfaceArea / siteSf) * 100.0 : 0.0;
            result.Steps.Add(new CalcStep("footprint", result.FootprintPercent, "%",
                $"footprint = A_BMP/site*100 = {surfaceArea:0.###}/{siteSf:0.###}*100"));
            return result;
        }

        /// <summary>First-flush partitioning: V_ff = V_total × f_ff; M_ff = M_total × m_f.</summary>
        public static FirstFlushResult AnalyzeFirstFlush(
            double totalRunoffVolumeCf,
            IDictionary<string, double> totalLoadsLbs,
            double firstFlushVolumeFraction = DefaultFirstFlushVolumeFraction)
        {
            if (totalRunoffVolumeCf < 0) throw new ArgumentOutOfRangeException(nameof(totalRunoffVolumeCf));
            if (totalLoadsLbs == null) throw new ArgumentNullException(nameof(totalLoadsLbs));
            if (firstFlushVolumeFraction <= 0 || firstFlushVolumeFraction > 1)
                throw new ArgumentOutOfRangeException(nameof(firstFlushVolumeFraction), "Fraction must be 0..1.");

            double ffVolume = totalRunoffVolumeCf * firstFlushVolumeFraction;

            var result = new FirstFlushResult
            {
                FirstFlushVolumeFraction = firstFlushVolumeFraction,
                TotalRunoffVolumeCf = totalRunoffVolumeCf,
                FirstFlushVolumeCf = ffVolume,
                CaptureVolumeForSizingCf = ffVolume,
            };

            result.Steps.Add(new CalcStep("V_ff", ffVolume, "ft^3",
                $"V_ff = V_total*f_ff = {totalRunoffVolumeCf:0.###}*{firstFlushVolumeFraction:0.###}"));

            foreach (KeyValuePair<string, double> kv in totalLoadsLbs)
            {
                double massFraction = BmpLibrary.GetFirstFlushMassFraction(kv.Key);
                double ffLoad = kv.Value * massFraction;
                result.TotalLoadsLbs[kv.Key] = kv.Value;
                result.FirstFlushLoadsLbs[kv.Key] = ffLoad;

                result.Steps.Add(new CalcStep($"M_ff_{kv.Key}", ffLoad, "lbs",
                    $"M_ff = M_total*m_f = {kv.Value:0.####}*{massFraction:0.###}"));

                if (ffVolume > 0)
                {
                    double conc = (ffLoad / ffVolume) * BmpLibrary.MgPerLiterPerLbPerCf;
                    result.FirstFlushConcentrationsMgPerL[kv.Key] = conc;
                    result.Steps.Add(new CalcStep($"C_ff_{kv.Key}", conc, "mg/L",
                        $"C_ff = M_ff/V_ff*16018.5 = {ffLoad:0.####}/{ffVolume:0.###}*16018.5"));
                }
            }

            return result;
        }

        /// <summary>Series removal efficiency: η_total = 1 − ∏(1 − η_i).</summary>
        public static double SeriesRemovalEfficiency(IEnumerable<double> bmpEfficiencies)
        {
            if (bmpEfficiencies == null) throw new ArgumentNullException(nameof(bmpEfficiencies));

            double product = 1.0;
            foreach (double eta in bmpEfficiencies)
            {
                if (eta < 0 || eta > 1)
                    throw new ArgumentOutOfRangeException(nameof(bmpEfficiencies), "Efficiency must be 0..1.");
                product *= (1.0 - eta);
            }

            return 1.0 - product;
        }

        private static AntecedentMoistureCondition ResolveMoistureCondition(
            AntecedentMoistureCondition option,
            int antecedentDryDays)
        {
            if (option != AntecedentMoistureCondition.Auto)
                return option;

            if (antecedentDryDays > 5) return AntecedentMoistureCondition.AmcI;
            if (antecedentDryDays < 2) return AntecedentMoistureCondition.AmcIII;
            return AntecedentMoistureCondition.AmcII;
        }

        private static double AdjustCurveNumber(double curveNumber, AntecedentMoistureCondition amc)
        {
            double adjusted = curveNumber;
            switch (amc)
            {
                case AntecedentMoistureCondition.AmcI:
                    adjusted = curveNumber - 13.0;
                    break;
                case AntecedentMoistureCondition.AmcIII:
                    adjusted = curveNumber + 13.0;
                    break;
            }

            return Math.Max(40.0, Math.Min(98.0, adjusted));
        }
    }
}