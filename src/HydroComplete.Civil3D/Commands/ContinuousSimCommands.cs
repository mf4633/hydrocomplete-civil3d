using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using HydroComplete.Engine;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace HydroComplete.Civil3D.Commands
{
    /// <summary>HC_LOSS — infiltration/loss method on a SCS Type II storm; HC_CONTINUOUS — multi-year continuous simulation.</summary>
    public sealed class ContinuousSimCommands
    {
        // city-slug → AutoCAD keyword (no hyphens/spaces)
        private static readonly (string Slug, string Keyword)[] Cities =
        {
            ("charlotte-nc",    "Charlotte"),
            ("raleigh-nc",      "Raleigh"),
            ("atlanta-ga",      "Atlanta"),
            ("new-york-ny",     "NewYork"),
            ("los-angeles-ca",  "LosAngeles"),
            ("chicago-il",      "Chicago"),
            ("dallas-tx",       "Dallas"),
            ("houston-tx",      "Houston"),
            ("washington-dc",   "WashDC"),
            ("miami-fl",        "Miami"),
            ("philadelphia-pa", "Philly"),
            ("phoenix-az",      "Phoenix"),
            ("boston-ma",       "Boston"),
            ("detroit-mi",      "Detroit"),
            ("seattle-wa",      "Seattle"),
            ("minneapolis-mn",  "Mpls"),
            ("denver-co",       "Denver"),
        };

        private static readonly string[] LandUseKeywords =
        {
            "ResLow", "ResMed", "ResHigh", "Commercial", "Industrial",
            "Roadway", "Parking", "Institutional", "OpenSpace", "Construction", "Agricultural",
        };

        private static readonly string[] BmpKeywords = { "Bioretention", "WetPond", "Wetland", "None" };

        [CommandMethod("HC_LOSS")]
        public void LossMethodCommand()
        {
            Document doc = Active();
            Editor ed = doc.Editor;

            var methodOpts = new PromptKeywordOptions(
                "\nInfiltration/loss method [GreenAmpt/Horton/CN/InitConst/ConstRate]")
            {
                AllowNone = true,
            };
            foreach (string m in new[] { "GreenAmpt", "Horton", "CN", "InitConst", "ConstRate" })
                methodOpts.Keywords.Add(m);
            methodOpts.Keywords.Default = "GreenAmpt";
            PromptResult methodRes = ed.GetKeywords(methodOpts);
            string methodKey = methodRes.Status == PromptStatus.OK ? methodRes.StringResult : "GreenAmpt";

            double rainfallIn = PromptPositiveDouble(ed, "\nTotal storm rainfall, in", 3.5);
            double durationHr = PromptPositiveDouble(ed, "\nStorm duration, hr", 24.0);

            const int steps = 24;
            double dtHr = durationHr / steps;
            IReadOnlyList<double> hyetograph = BuildTypeIiHyetograph(rainfallIn, steps);

            LossMethods.LossParameters lossParams;
            string methodLabel;

            switch (methodKey)
            {
                case "GreenAmpt":
                {
                    double ks = PromptPositiveDouble(ed, "\nSaturated hydraulic conductivity Ks, in/hr", 1.32);
                    double psi = PromptPositiveDouble(ed, "\nWetting-front suction ψ, in", 8.74);
                    double dTheta = PromptPositiveDouble(ed, "\nMoisture deficit Δθ", 0.45);
                    lossParams = new LossMethods.LossParameters
                    {
                        Method = LossMethods.LossMethodType.GreenAmpt,
                        GreenAmpt = new LossMethods.GreenAmptParameters
                            { KsInPerHr = ks, PsiIn = psi, MoistureDeficit = dTheta },
                    };
                    methodLabel = string.Format(CultureInfo.InvariantCulture,
                        "Green-Ampt (Ks={0:0.##} in/hr, ψ={1:0.##} in, Δθ={2:0.###})", ks, psi, dTheta);
                    break;
                }
                case "Horton":
                {
                    double f0 = PromptPositiveDouble(ed, "\nInitial infiltration f₀, in/hr", 3.0);
                    double fc = PromptPositiveDouble(ed, "\nFinal infiltration fc, in/hr", 0.30);
                    double k = PromptPositiveDouble(ed, "\nDecay constant k, 1/hr", 3.5);
                    lossParams = new LossMethods.LossParameters
                    {
                        Method = LossMethods.LossMethodType.Horton,
                        Horton = new LossMethods.HortonParameters { F0InPerHr = f0, FcInPerHr = fc, KPerHr = k },
                    };
                    methodLabel = string.Format(CultureInfo.InvariantCulture,
                        "Horton (f₀={0:0.##}, fc={1:0.##}, k={2:0.##})", f0, fc, k);
                    break;
                }
                case "InitConst":
                {
                    double cn = PromptPositiveDouble(ed, "\nSCS Curve Number", 75.0);
                    double fc = PromptPositiveDouble(ed, "\nConstant loss rate fc, in/hr", 0.15);
                    lossParams = new LossMethods.LossParameters
                    {
                        Method = LossMethods.LossMethodType.InitialConstant,
                        CurveNumber = cn,
                        ConstantLossRateInPerHr = fc,
                    };
                    methodLabel = string.Format(CultureInfo.InvariantCulture,
                        "Initial+Constant (CN={0:0.#}, fc={1:0.##} in/hr)", cn, fc);
                    break;
                }
                case "ConstRate":
                {
                    double fc = PromptPositiveDouble(ed, "\nConstant loss rate fc, in/hr", 0.15);
                    lossParams = new LossMethods.LossParameters
                    {
                        Method = LossMethods.LossMethodType.ConstantRate,
                        ConstantLossRateInPerHr = fc,
                    };
                    methodLabel = string.Format(CultureInfo.InvariantCulture,
                        "Constant Rate (fc={0:0.##} in/hr)", fc);
                    break;
                }
                default: // CN
                {
                    double cn = PromptPositiveDouble(ed, "\nSCS Curve Number", 75.0);
                    lossParams = new LossMethods.LossParameters
                        { Method = LossMethods.LossMethodType.CurveNumber, CurveNumber = cn };
                    methodLabel = string.Format(CultureInfo.InvariantCulture, "SCS CN (CN={0:0.#})", cn);
                    break;
                }
            }

            LossMethods.IncrementalLossResult result;
            try
            {
                result = LossMethods.ComputeIncremental(hyetograph, dtHr, lossParams);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nError: {ex.Message}\n");
                return;
            }

            double peakExcess = result.Increments.Max(x => x.ExcessRainfallIn / dtHr);
            int peakIdx = result.Increments.FindIndex(x =>
                Math.Abs(x.ExcessRainfallIn / dtHr - peakExcess) < 1e-9);

            ed.WriteMessage("\n--- HydroComplete: infiltration/loss method ---");
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Method:   {0}", methodLabel));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Rainfall: {0:0.##} in over {1:0.#} hr — SCS Type II, {2} steps × {3:0.##} hr",
                rainfallIn, durationHr, steps, dtHr));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Total excess:  {0:0.###} in  ({1:0.#}% of rainfall)",
                result.TotalExcessIn, rainfallIn > 0 ? result.TotalExcessIn / rainfallIn * 100 : 0));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Total loss:    {0:0.###} in", result.TotalLossIn));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Peak excess:   {0:0.###} in/hr at t = {1:0.#} hr",
                peakExcess, peakIdx * dtHr));
            ed.WriteMessage("\n\n  t(hr)  Rain(in)  Loss(in)  Excess(in/hr)");
            foreach (LossMethods.ExcessRainfallIncrement inc in result.Increments)
            {
                if (inc.ExcessRainfallIn > 0 || inc.Index == 0 || inc.Index == result.Increments.Count - 1)
                {
                    ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                        "\n  {0,5:0.#}  {1,8:0.###}  {2,8:0.###}  {3,12:0.###}",
                        inc.Index * dtHr, inc.RainfallIn, inc.LossIn, inc.ExcessRainfallIn / dtHr));
                }
            }
            ed.WriteMessage("\n");
        }

        [CommandMethod("HC_CONTINUOUS")]
        public void ContinuousSimCommand()
        {
            Document doc = Active();
            Editor ed = doc.Editor;

            // City selection
            var locOpts = new PromptKeywordOptions(
                "\nCity [Charlotte/Raleigh/Atlanta/NewYork/LosAngeles/Chicago/Dallas/Houston/" +
                "WashDC/Miami/Philly/Phoenix/Boston/Detroit/Seattle/Mpls/Denver]")
            {
                AllowNone = true,
            };
            foreach ((_, string kw) in Cities) locOpts.Keywords.Add(kw);
            locOpts.Keywords.Default = "Charlotte";
            PromptResult locRes = ed.GetKeywords(locOpts);
            string keyword = locRes.Status == PromptStatus.OK ? locRes.StringResult : "Charlotte";
            string location = Array.Find(Cities, c =>
                string.Equals(c.Keyword, keyword, StringComparison.OrdinalIgnoreCase)).Slug
                ?? "charlotte-nc";

            double area = PromptPositiveDouble(ed, "\nDrainage area, acres", 5.0);
            double cn = PromptPositiveDouble(ed, "\nSCS Curve Number", 75.0);
            string landUse = PromptLandUse(ed);
            int years = (int)Math.Max(1, Math.Min(10, PromptPositiveDouble(ed, "\nSimulation years (1-10)", 3.0)));

            // Optional BMP
            var bmpOpts = new PromptKeywordOptions(
                "\nBMP for continuous treatment [Bioretention/WetPond/Wetland/None]")
            { AllowNone = true };
            foreach (string b in BmpKeywords) bmpOpts.Keywords.Add(b);
            bmpOpts.Keywords.Default = "None";
            PromptResult bmpRes = ed.GetKeywords(bmpOpts);
            string bmpKey = bmpRes.Status == PromptStatus.OK ? bmpRes.StringResult : "None";

            ContinuousSimulation.BmpSimulationConfig? bmpConfig = null;
            if (!string.Equals(bmpKey, "None", StringComparison.OrdinalIgnoreCase))
            {
                double sf = PromptNonNegativeDouble(ed, "\nBMP surface area, sf (0 = 5% of site)", 0.0);
                if (sf <= 0) sf = area * BmpLibrary.SqFtPerAcre * 0.05;
                string bmpType = string.Equals(bmpKey, "Wetland", StringComparison.OrdinalIgnoreCase)
                    ? "constructed-wetland"
                    : string.Equals(bmpKey, "WetPond", StringComparison.OrdinalIgnoreCase)
                        ? BmpType.WetPond
                        : BmpType.Bioretention;
                bmpConfig = new ContinuousSimulation.BmpSimulationConfig
                {
                    BmpType = bmpType,
                    SurfaceAreaSf = sf,
                    Bioretention = new BioretentionRouting.BioretentionConfig(),
                };
            }

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Running {0}-year continuous simulation for {1}...", years, location));

            ContinuousSimulation.ContinuousSimulationResult result;
            try
            {
                result = ContinuousSimulation.Run(
                    new ContinuousSimulation.SiteData
                    {
                        Location = location,
                        AreaAcres = area,
                        CurveNumber = cn,
                        LandUse = landUse,
                        ImperviousPercent = 50.0,
                        Years = years,
                    },
                    bmpConfig);
            }
            catch (ArgumentException ex)
            {
                ed.WriteMessage($"\nError: {ex.Message}\n");
                return;
            }

            string[] mon = { "Jan","Feb","Mar","Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec" };
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n--- HydroComplete: continuous simulation ({0}, {1} yr) ---", location, years));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Area={0:0.000} ac  CN={1:0.#}  LandUse={2}", area, cn, landUse));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Events: {0}  Rainfall: {1:0.#} in  ET: {2:0.#} in  (total over {3} yr)",
                result.EventCount, result.TotalRainfallIn, result.TotalEtIn, years));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n  Annual avg runoff: {0:0.##} ac-in   Annual avg ET: {1:0.##} in",
                result.AnnualAvgRunoffAcreIn, result.AnnualAvgEtIn));

            ed.WriteMessage("\n\n  Monthly averages (per year):");
            ed.WriteMessage("\n  Month  Rain(in)  Q(ac-in)  TSS(lbs)  TN(lbs)");
            foreach (ContinuousSimulation.MonthlyAverageSummary m in result.MonthlyAverage)
            {
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  {0,-5}  {1,7:0.00}  {2,8:0.00}  {3,8:0.1}  {4,7:0.2}",
                    mon[m.Month], m.AvgRainfallIn, m.AvgRunoffAcreIn, m.AvgTssLbs, m.AvgTnLbs));
            }

            if (bmpConfig != null && result.OverallRemovalPercent != null)
            {
                ed.WriteMessage("\n\n  Annual loads and BMP removal:");
                ed.WriteMessage("\n  Pollutant   Load(lbs/yr)  Treated(lbs/yr)  Removal(%)");
                foreach (string p in Pollutant.Core)
                {
                    double load = result.TotalLoadsLbs.TryGetValue(p, out double l) ? l / years : 0;
                    double treated = result.TotalTreatedLbs.TryGetValue(p, out double t) ? t / years : 0;
                    result.OverallRemovalPercent.TryGetValue(p, out double pct);
                    ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                        "\n  {0,-10}  {1,12:0.0}  {2,15:0.0}  {3,10:0.1}",
                        p.ToUpperInvariant(), load, treated, pct));
                }
            }
            else
            {
                ed.WriteMessage("\n\n  Annual pollutant loads (no BMP):");
                ed.WriteMessage("\n  Pollutant   Load(lbs/yr)");
                foreach (string p in Pollutant.Core)
                {
                    double load = result.TotalLoadsLbs.TryGetValue(p, out double l) ? l / years : 0;
                    ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                        "\n  {0,-10}  {1,12:0.0}", p.ToUpperInvariant(), load));
                }
            }

            ed.WriteMessage("\n");
        }

        // ── helpers ─────────────────────────────────────────────────────────

        private static IReadOnlyList<double> BuildTypeIiHyetograph(double totalIn, int steps)
        {
            // SCS Type II 24-hr cumulative fraction (25 breakpoints)
            double[] cum = {
                0, 0.005, 0.010, 0.015, 0.020, 0.026, 0.033, 0.041, 0.049, 0.057,
                0.067, 0.076, 0.100, 0.220, 0.430, 0.570, 0.663, 0.727, 0.767, 0.800,
                0.820, 0.840, 0.860, 0.880, 1.000,
            };
            var inc = new double[steps];
            for (int i = 0; i < steps; i++)
                inc[i] = (cum[i + 1] - cum[i]) * totalIn;
            return inc;
        }

        private static string PromptLandUse(Editor ed)
        {
            var opts = new PromptKeywordOptions(
                "\nLand use [ResLow/ResMed/ResHigh/Commercial/Industrial/" +
                "Roadway/Parking/Institutional/OpenSpace/Construction/Agricultural]")
            { AllowNone = true };
            foreach (string lu in LandUseKeywords) opts.Keywords.Add(lu);
            opts.Keywords.Default = "ResMed";
            PromptResult res = ed.GetKeywords(opts);
            string sel = res.Status == PromptStatus.OK ? res.StringResult : "ResMed";
            return sel switch
            {
                "ResLow"        => "residential-low",
                "ResHigh"       => "residential-high",
                "Commercial"    => LandUse.Commercial,
                "Industrial"    => LandUse.Industrial,
                "Roadway"       => "roadway",
                "Parking"       => "parking",
                "Institutional" => "institutional",
                "OpenSpace"     => "open-space",
                "Construction"  => "construction",
                "Agricultural"  => "agricultural",
                _               => "residential-medium",
            };
        }

        private static double PromptPositiveDouble(Editor ed, string message, double defaultValue)
        {
            var opts = new PromptDoubleOptions(message)
            {
                DefaultValue = defaultValue,
                UseDefaultValue = true,
                AllowZero = false,
                AllowNegative = false,
            };
            PromptDoubleResult res = ed.GetDouble(opts);
            return res.Status == PromptStatus.OK ? res.Value : defaultValue;
        }

        private static double PromptNonNegativeDouble(Editor ed, string message, double defaultValue)
        {
            var opts = new PromptDoubleOptions(message)
            {
                DefaultValue = defaultValue,
                UseDefaultValue = true,
                AllowZero = true,
                AllowNegative = false,
            };
            PromptDoubleResult res = ed.GetDouble(opts);
            return res.Status == PromptStatus.OK ? res.Value : defaultValue;
        }

        private static Document Active()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) throw new InvalidOperationException("No active drawing.");
            return doc;
        }
    }
}
