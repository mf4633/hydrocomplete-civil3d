using System;
using System.Collections.Generic;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Physics-based bioretention routing: Darcy media infiltration, orifice underdrain,
    /// ponding storage, and residence-time-dependent pollutant removal (Davis 2008).
    /// </summary>
    public static class BioretentionRouting
    {
        private const double GravityFtPerSec2 = 32.2;

        public sealed class BioretentionConfig
        {
            public double KsatInPerHr { get; set; } = 1.0;
            public double MediaDepthFt { get; set; } = 2.5;
            public double PondingDepthFt { get; set; } = 1.0;
            public double Porosity { get; set; } = 0.40;
            public double FieldCapacity { get; set; } = 0.20;
            public double UnderdrainDiameterIn { get; set; } = 6.0;
            public double UnderdrainCd { get; set; } = 0.6;
            public double NativeKsatInPerHr { get; set; } = 0.0;
            public double? CurrentMediaMoisture { get; set; }
        }

        public sealed class PollutantRemovalEfficiency
        {
            public double TreatedPercent { get; set; }
            public double BlendedPercent { get; set; }
            public string Method { get; set; } = "";
        }

        public sealed class BioretentionRoutingResult : TracedResult
        {
            public string BmpType { get; set; } = global::HydroComplete.Engine.BmpType.Bioretention;
            public string Method { get; set; } = "";
            public string Reference { get; set; } = "";
            public double DesignVolumeCf { get; set; }
            public double TreatedVolumeCf { get; set; }
            public double OverflowVolumeCf { get; set; }
            public double BypassFractionPercent { get; set; }
            public double DrawdownTimeHr { get; set; }
            public double ResidenceTimeHr { get; set; }
            public double MediaStorageCf { get; set; }
            public double PondingStorageCf { get; set; }
            public double TotalCapacityCf { get; set; }
            public double PostEventMoisture { get; set; }
            public double QMediaAvgCfs { get; set; }
            public double QUnderdrainCfs { get; set; }
            public Dictionary<string, PollutantRemovalEfficiency> RemovalEfficiency { get; } =
                new Dictionary<string, PollutantRemovalEfficiency>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Route a design storm volume through a bioretention cell.
        /// </summary>
        public static BioretentionRoutingResult Route(
            BioretentionConfig config,
            double designVolumeCf,
            double surfaceAreaSf)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (designVolumeCf < 0) throw new ArgumentOutOfRangeException(nameof(designVolumeCf));
            if (surfaceAreaSf <= 0) throw new ArgumentOutOfRangeException(nameof(surfaceAreaSf));

            double ksatFtPerHr = config.KsatInPerHr / BmpLibrary.InchesPerFoot;
            double mediaDepth = config.MediaDepthFt;
            double pondingMax = config.PondingDepthFt;
            double porosity = config.Porosity;
            double fieldCapacity = config.FieldCapacity;
            double udDiaFt = config.UnderdrainDiameterIn / BmpLibrary.InchesPerFoot;
            double udCd = config.UnderdrainCd;
            double nativeKsatFtPerHr = config.NativeKsatInPerHr / BmpLibrary.InchesPerFoot;

            double currentMoisture = config.CurrentMediaMoisture ?? fieldCapacity;

            double mediaStorageCf = surfaceAreaSf * mediaDepth * Math.Max(0.0, porosity - currentMoisture);
            double pondingStorageCf = surfaceAreaSf * pondingMax;
            double totalCapacityCf = mediaStorageCf + pondingStorageCf;

            double treatedVolume = Math.Min(designVolumeCf, totalCapacityCf);
            double overflowVolume = Math.Max(0.0, designVolumeCf - totalCapacityCf);

            double udAreaFt2 = Math.PI * Math.Pow(udDiaFt / 2.0, 2.0);
            double avgHead = pondingMax / 2.0 + mediaDepth;
            double qMediaAvgCfPerHr = ksatFtPerHr * surfaceAreaSf * avgHead / mediaDepth;
            double qUdAvgCfPerHr = udCd * udAreaFt2 * Math.Sqrt(2.0 * GravityFtPerSec2 * mediaDepth / 2.0);
            double qNativeCfPerHr = nativeKsatFtPerHr * surfaceAreaSf;
            double qTotalAvgCfPerHr = qMediaAvgCfPerHr + qUdAvgCfPerHr + qNativeCfPerHr;

            double drawdownTimeHr = qTotalAvgCfPerHr > 0 ? totalCapacityCf / qTotalAvgCfPerHr : 999.0;
            double residenceTimeHr = qTotalAvgCfPerHr > 0 ? treatedVolume / qTotalAvgCfPerHr : 0.0;
            double treatedFrac = designVolumeCf > 0 ? treatedVolume / designVolumeCf : 0.0;

            var removalCurves = new Dictionary<string, (double Emax, double Alpha)>
            {
                [Pollutant.Tss] = (0.92, 0.50),
                [Pollutant.Tn] = (0.50, 0.20),
                [Pollutant.Tp] = (0.65, 0.30),
                ["bacteria"] = (0.80, 0.40),
                ["metals"] = (0.90, 0.45),
            };

            var result = new BioretentionRoutingResult
            {
                Method = "Darcy infiltration + orifice underdrain",
                Reference = "Davis, A.P. (2008), ASCE J. Env. Eng. 134(6)",
                DesignVolumeCf = designVolumeCf,
                TreatedVolumeCf = treatedVolume,
                OverflowVolumeCf = overflowVolume,
                BypassFractionPercent = designVolumeCf > 0 ? (overflowVolume / designVolumeCf) * 100.0 : 0.0,
                DrawdownTimeHr = drawdownTimeHr,
                ResidenceTimeHr = residenceTimeHr,
                MediaStorageCf = mediaStorageCf,
                PondingStorageCf = pondingStorageCf,
                TotalCapacityCf = totalCapacityCf,
                PostEventMoisture = Math.Min(
                    porosity,
                    currentMoisture + treatedVolume / Math.Max(1.0, surfaceAreaSf * mediaDepth)),
                QMediaAvgCfs = qMediaAvgCfPerHr / 3600.0,
                QUnderdrainCfs = qUdAvgCfPerHr / 3600.0,
            };

            foreach (KeyValuePair<string, (double Emax, double Alpha)> curve in removalCurves)
            {
                double eTreated = curve.Value.Emax * (1.0 - Math.Exp(-curve.Value.Alpha * residenceTimeHr));
                result.RemovalEfficiency[curve.Key] = new PollutantRemovalEfficiency
                {
                    TreatedPercent = eTreated * 100.0,
                    BlendedPercent = eTreated * treatedFrac * 100.0,
                    Method = "Davis (2008) residence-time curve",
                };
            }

            result.Steps.Add(new CalcStep("V_capacity", totalCapacityCf, "ft^3",
                $"ponding + media pore space above moisture = {pondingStorageCf:0.###}+{mediaStorageCf:0.###}"));
            result.Steps.Add(new CalcStep("V_treated", treatedVolume, "ft^3",
                $"min(V_design, V_capacity) = min({designVolumeCf:0.###},{totalCapacityCf:0.###})"));
            result.Steps.Add(new CalcStep("Q_media", qMediaAvgCfPerHr, "cf/hr",
                $"K_sat*A*(h_pond+d_media)/d_media"));
            result.Steps.Add(new CalcStep("Q_ud", qUdAvgCfPerHr, "cf/hr",
                $"C_d*A_orifice*sqrt(2gh)"));
            result.Steps.Add(new CalcStep("t_res", residenceTimeHr, "hr",
                $"V_treated/Q_total = {treatedVolume:0.###}/{qTotalAvgCfPerHr:0.###}"));

            if (result.RemovalEfficiency.TryGetValue(Pollutant.Tss, out PollutantRemovalEfficiency? tss))
            {
                result.Steps.Add(new CalcStep("E_TSS", tss.TreatedPercent, "%",
                    "E = Emax*(1-exp(-alpha*t_res))"));
            }

            return result;
        }
    }
}