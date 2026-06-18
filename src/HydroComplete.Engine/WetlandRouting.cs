using System;
using System.Collections.Generic;
using System.Linq;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Wet pond first-order decay (Kadlec &amp; Knight 1996) and constructed wetland
    /// 4-zone k-C* model (Kadlec &amp; Wallace 2009).
    /// </summary>
    public static class WetlandRouting
    {
        public sealed class WetPondConfig
        {
            public double AvgDepthFt { get; set; } = 4.0;
            public double ResidentTimeDays { get; set; } = 14.0;
            public Dictionary<string, double>? DecayRatesPerMin { get; set; }
        }

        public sealed class WetlandZone
        {
            public double AreaFraction { get; set; }
            public double DepthFt { get; set; }
        }

        public sealed class WetlandConfig
        {
            public Dictionary<string, WetlandZone> Zones { get; set; } = CreateDefaultZones();
            public Dictionary<string, double>? KDecayPerMeterPerYear { get; set; }
            public Dictionary<string, double>? BackgroundConcentration { get; set; }
        }

        public sealed class ZoneTreatmentStep
        {
            public string Zone { get; set; } = "";
            public double InfluentConcentration { get; set; }
            public double EffluentConcentration { get; set; }
            public double RemovalPercent { get; set; }
        }

        public sealed class PollutantRemovalEfficiency
        {
            public double TreatedPercent { get; set; }
            public double BlendedPercent { get; set; }
            public string Method { get; set; } = "";
            public List<ZoneTreatmentStep> Zones { get; } = new List<ZoneTreatmentStep>();
        }

        public sealed class WetPondRoutingResult : TracedResult
        {
            public string BmpType { get; set; } = global::HydroComplete.Engine.BmpType.WetPond;
            public string Method { get; set; } = "";
            public string Reference { get; set; } = "";
            public double DesignVolumeCf { get; set; }
            public double TreatedVolumeCf { get; set; }
            public double OverflowVolumeCf { get; set; }
            public double BypassFractionPercent { get; set; }
            public double PoolVolumeCf { get; set; }
            public double ResidenceTimeHr { get; set; }
            public double ResidenceTimeDays { get; set; }
            public Dictionary<string, PollutantRemovalEfficiency> RemovalEfficiency { get; } =
                new Dictionary<string, PollutantRemovalEfficiency>(StringComparer.OrdinalIgnoreCase);
        }

        public sealed class ConstructedWetlandRoutingResult : TracedResult
        {
            public string BmpType { get; set; } = "constructed-wetland";
            public string Method { get; set; } = "";
            public string Reference { get; set; } = "";
            public double DesignVolumeCf { get; set; }
            public double TreatedVolumeCf { get; set; }
            public double OverflowVolumeCf { get; set; }
            public double TotalAreaSf { get; set; }
            public int ZoneCount { get; set; }
            public Dictionary<string, PollutantRemovalEfficiency> RemovalEfficiency { get; } =
                new Dictionary<string, PollutantRemovalEfficiency>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Route through wet pond using Kadlec first-order decay.</summary>
        public static WetPondRoutingResult RouteWetPond(
            WetPondConfig config,
            double designVolumeCf,
            double surfaceAreaSf)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (designVolumeCf < 0) throw new ArgumentOutOfRangeException(nameof(designVolumeCf));
            if (surfaceAreaSf <= 0) throw new ArgumentOutOfRangeException(nameof(surfaceAreaSf));

            double poolDepth = config.AvgDepthFt;
            double poolVolumeCf = surfaceAreaSf * poolDepth;
            double drawdownDays = config.ResidentTimeDays;
            double outflowRateCfPerHr = poolVolumeCf / (drawdownDays * 24.0);
            double residenceTimeHr = outflowRateCfPerHr > 0 ? poolVolumeCf / outflowRateCfPerHr : drawdownDays * 24.0;
            double residenceTimeMin = residenceTimeHr * 60.0;

            double treatedVolume = Math.Min(designVolumeCf, poolVolumeCf * 1.5);
            double overflowVolume = Math.Max(0.0, designVolumeCf - poolVolumeCf * 1.5);
            double treatedFrac = designVolumeCf > 0 ? treatedVolume / designVolumeCf : 0.0;

            var decayRates = config.DecayRatesPerMin ?? new Dictionary<string, double>
            {
                [Pollutant.Tss] = 0.0015,
                [Pollutant.Tn] = 0.0005,
                [Pollutant.Tp] = 0.0008,
                ["bacteria"] = 0.0020,
                ["metals"] = 0.0010,
            };

            var result = new WetPondRoutingResult
            {
                Method = "First-order decay with residence time",
                Reference = "Kadlec & Knight (1996), Treatment Wetlands",
                DesignVolumeCf = designVolumeCf,
                TreatedVolumeCf = treatedVolume,
                OverflowVolumeCf = overflowVolume,
                BypassFractionPercent = designVolumeCf > 0 ? (overflowVolume / designVolumeCf) * 100.0 : 0.0,
                PoolVolumeCf = poolVolumeCf,
                ResidenceTimeHr = residenceTimeHr,
                ResidenceTimeDays = residenceTimeHr / 24.0,
            };

            foreach (KeyValuePair<string, double> kv in decayRates)
            {
                double efficiency = 1.0 - Math.Exp(-kv.Value * residenceTimeMin);
                result.RemovalEfficiency[kv.Key] = new PollutantRemovalEfficiency
                {
                    TreatedPercent = efficiency * 100.0,
                    BlendedPercent = efficiency * treatedFrac * 100.0,
                    Method = "Kadlec & Knight (1996) first-order decay",
                };
            }

            result.Steps.Add(new CalcStep("V_pool", poolVolumeCf, "ft^3", $"A*d_avg = {surfaceAreaSf:0.###}*{poolDepth:0.###}"));
            result.Steps.Add(new CalcStep("t_res", residenceTimeHr, "hr",
                $"V_pool/(V_pool/(t_drawdown*24)) = {drawdownDays:0.###} days"));
            if (result.RemovalEfficiency.TryGetValue(Pollutant.Tss, out PollutantRemovalEfficiency? tss))
            {
                result.Steps.Add(new CalcStep("E_TSS", tss.TreatedPercent, "%",
                    "E = 1-exp(-k*t_res_min)"));
            }

            return result;
        }

        /// <summary>Route through 4-zone constructed wetland using k-C* model.</summary>
        public static ConstructedWetlandRoutingResult RouteConstructedWetland(
            WetlandConfig config,
            double designVolumeCf,
            double surfaceAreaSf,
            IReadOnlyDictionary<string, double>? inflowConcentrations = null)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (designVolumeCf < 0) throw new ArgumentOutOfRangeException(nameof(designVolumeCf));
            if (surfaceAreaSf <= 0) throw new ArgumentOutOfRangeException(nameof(surfaceAreaSf));

            var defaultConc = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                [Pollutant.Tss] = 150.0,
                [Pollutant.Tn] = 2.5,
                [Pollutant.Tp] = 0.40,
                ["bacteria"] = 20_000.0,
                ["metals"] = 0.05,
            };

            if (inflowConcentrations != null)
            {
                foreach (KeyValuePair<string, double> kv in inflowConcentrations)
                    defaultConc[kv.Key] = kv.Value;
            }

            var kDecay = config.KDecayPerMeterPerYear ?? new Dictionary<string, double>
            {
                [Pollutant.Tss] = 20.0,
                [Pollutant.Tn] = 10.0,
                [Pollutant.Tp] = 12.0,
                ["bacteria"] = 30.0,
                ["metals"] = 15.0,
            };

            var cStar = config.BackgroundConcentration ?? new Dictionary<string, double>
            {
                [Pollutant.Tss] = 5.0,
                [Pollutant.Tn] = 1.0,
                [Pollutant.Tp] = 0.05,
                ["bacteria"] = 100.0,
                ["metals"] = 0.005,
            };

            const double cfToM3 = 0.0283168;
            const double sfToM2 = 0.0929;
            double qM3PerYear = designVolumeCf * cfToM3 * 52.0;
            double totalAreaM2 = surfaceAreaSf * sfToM2;

            string[] zoneNames = config.Zones.Keys.ToArray();
            var result = new ConstructedWetlandRoutingResult
            {
                Method = "Kadlec & Wallace (2009) k-C* model, 4-zone series",
                Reference = "Kadlec, R.H. & Wallace, S.D. (2009), Treatment Wetlands, 2nd Ed.",
                DesignVolumeCf = designVolumeCf,
                TreatedVolumeCf = designVolumeCf,
                OverflowVolumeCf = 0.0,
                TotalAreaSf = surfaceAreaSf,
                ZoneCount = zoneNames.Length,
            };

            foreach (KeyValuePair<string, double> pollutant in kDecay)
            {
                double cIn = defaultConc.TryGetValue(pollutant.Key, out double cin) ? cin : 100.0;
                double k = pollutant.Value;
                double background = cStar.TryGetValue(pollutant.Key, out double cstar) ? cstar : 0.0;
                double c = cIn;
                var zoneSteps = new List<ZoneTreatmentStep>();

                foreach (string zoneName in zoneNames)
                {
                    WetlandZone zone = config.Zones[zoneName];
                    double zoneAreaM2 = zone.AreaFraction * totalAreaM2;
                    double exponent = qM3PerYear > 0 ? -k * zoneAreaM2 / qM3PerYear : 0.0;
                    double cOut = background + (c - background) * Math.Exp(exponent);
                    cOut = Math.Max(background, cOut);

                    zoneSteps.Add(new ZoneTreatmentStep
                    {
                        Zone = zoneName,
                        InfluentConcentration = c,
                        EffluentConcentration = cOut,
                        RemovalPercent = c > 0 ? (1.0 - cOut / c) * 100.0 : 0.0,
                    });
                    c = cOut;
                }

                double overallRemoval = cIn > 0 ? (1.0 - c / cIn) * 100.0 : 0.0;
                var eff = new PollutantRemovalEfficiency
                {
                    TreatedPercent = overallRemoval,
                    BlendedPercent = overallRemoval,
                    Method = "Kadlec & Wallace (2009) k-C* model",
                };
                eff.Zones.AddRange(zoneSteps);
                result.RemovalEfficiency[pollutant.Key] = eff;
            }

            result.Steps.Add(new CalcStep("Q_annual", qM3PerYear, "m^3/yr",
                $"V_event*0.0283*52 events/yr"));
            result.Steps.Add(new CalcStep("zones", zoneNames.Length, "-",
                string.Join(" -> ", zoneNames)));
            if (result.RemovalEfficiency.TryGetValue(Pollutant.Tss, out PollutantRemovalEfficiency? tss))
            {
                result.Steps.Add(new CalcStep("E_TSS", tss.TreatedPercent, "%",
                    "C_out = C* + (C_in-C*)*exp(-k*A/Q) in series"));
            }

            return result;
        }

        private static Dictionary<string, WetlandZone> CreateDefaultZones()
        {
            return new Dictionary<string, WetlandZone>(StringComparer.OrdinalIgnoreCase)
            {
                ["forebay"] = new WetlandZone { AreaFraction = 0.10, DepthFt = 4.0 },
                ["deepPool"] = new WetlandZone { AreaFraction = 0.15, DepthFt = 4.0 },
                ["shallowMarsh"] = new WetlandZone { AreaFraction = 0.40, DepthFt = 0.5 },
                ["shallowLand"] = new WetlandZone { AreaFraction = 0.35, DepthFt = 0.0 },
            };
        }
    }
}