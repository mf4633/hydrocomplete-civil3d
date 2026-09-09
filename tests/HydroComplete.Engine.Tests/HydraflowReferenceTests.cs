using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    /// <summary>
    /// The engine checked line by line against Autodesk Hydraflow Storm Sewers.
    ///
    /// Two real Civil 3D storm sewer runs are imported from their .stm files and
    /// pushed through hydrology, Manning capacity, the backwater gradeline and
    /// the inlet check. Every expected value below is a number Hydraflow printed
    /// on its own Storm Sewer Tabulation and HGL Computations pages for the same
    /// files (run date 2026-08-16) — nothing here is this engine's own output
    /// frozen as a baseline.
    ///
    /// Where the two disagree, the tolerance carries the reason. There are only
    /// three real method differences, and each has a comment at the assertion
    /// that depends on it:
    ///
    ///   1. Travel-time velocity. Hydraflow times a drowned reach at its
    ///      backwater (full) depth; this engine uses normal depth, which is
    ///      faster, which shortens Tc, which raises intensity and flow. That is
    ///      the entire flow difference on this network.
    ///   2. Manning's constant. Both use 1.486 here, so capacity agrees to the
    ///      printed digit; the residual is the two-decimal inverts the file
    ///      stores, which move the slope and so sqrt(S).
    ///   3. The terminal inlet. Hydraflow charges K = 1.0 at the top structure
    ///      of a run and treats it as an entrance, not the project K.
    ///
    /// The gradeline test feeds Hydraflow's own flows and full-pipe hydraulics,
    /// so it isolates the head-loss arithmetic and the backwater direction from
    /// the modelling choices above. That is why its tolerance is tight (0.15 ft)
    /// while travel time carries half a minute.
    /// </summary>
    public class HydraflowReferenceTests
    {
        // ─────────────────────────── reference data ───────────────────────────

        /// <summary>One line of the Hydraflow tabulation, outfall first.</summary>
        private sealed class RefLine
        {
            public string PipeName = "";
            public string UpstreamStructure = "";
            public double Slope;
            public double Ca;
            public double TcMin;
            public double IntensityInHr;
            public double QCfs;
            public double CapacityCfs;
            public bool Surcharged;

            /// <summary>Junction K Hydraflow applied at this line's upstream structure.</summary>
            public double JunctionK;

            /// <summary>HGL at the downstream structure, ft.</summary>
            public double HglDnFt;

            /// <summary>HGL at the upstream structure after the junction loss, ft.</summary>
            public double HglJunctionFt;

            /// <summary>
            /// How far below Hydraflow this engine's grade line sits at this
            /// line, ft. This was 0.36 on every Run 2 line above AI-8, because
            /// the backwater pass stepped straight through a structure where
            /// Hydraflow held the grade line up at the crown of the surcharged
            /// pipe above it. HglProfileOptions.EnforceFlowDepthFloor closed
            /// that, so every entry is now zero and stays that way.
            /// </summary>
            public double LowerThanHydraflowByFt;

            /// <summary>
            /// True where Hydraflow's own HGL comes out above the structure
            /// rim. Not a defect: it is the finding the report exists for.
            /// </summary>
            public bool AboveRim;
        }

        private sealed class RefRun
        {
            public string Name = "";
            public string Fixture = "";

            /// <summary>Tailwater the printed report was run with, ft.</summary>
            public double TailwaterFt;

            public RefLine[] Lines = Array.Empty<RefLine>();

            /// <summary>Inlet id and the flow it captured, cfs. All are in a sag at 100 %.</summary>
            public (string Id, double QCfs)[] Inlets = Array.Empty<(string, double)>();
        }

        private static readonly RefRun Run1 = new RefRun
        {
            Name = "Run 1",
            Fixture = "civil3d-2015-storm-sewers.stm",
            TailwaterFt = 757.365,
            Lines = new[]
            {
                new RefLine { PipeName = "Pipe - (5)", UpstreamStructure = "AI-4", Slope = 0.00500, Ca = 1.50, TcMin = 7.4, IntensityInHr = 6.83, QCfs = 10.23, CapacityCfs = 8.04, Surcharged = true,  JunctionK = 0.5, HglDnFt = 757.37, HglJunctionFt = 759.08, AboveRim = true },
                new RefLine { PipeName = "Pipe - (4)", UpstreamStructure = "AI-3", Slope = 0.00497, Ca = 1.09, TcMin = 6.8, IntensityInHr = 7.07, QCfs =  7.68, CapacityCfs = 8.02, Surcharged = false, JunctionK = 0.5, HglDnFt = 759.08, HglJunctionFt = 760.03 },
                new RefLine { PipeName = "Pipe - (3)", UpstreamStructure = "AI-2", Slope = 0.00497, Ca = 0.71, TcMin = 5.8, IntensityInHr = 7.44, QCfs =  5.28, CapacityCfs = 8.02, Surcharged = false, JunctionK = 0.5, HglDnFt = 760.03, HglJunctionFt = 760.44 },
                new RefLine { PipeName = "Pipe - (2)", UpstreamStructure = "AI-1", Slope = 0.00502, Ca = 0.38, TcMin = 5.0, IntensityInHr = 7.77, QCfs =  2.97, CapacityCfs = 2.73, Surcharged = true,  JunctionK = 1.0, HglDnFt = 760.44, HglJunctionFt = 761.69 },
            },
            Inlets = new[] { ("AI-4", 3.19), ("AI-3", 2.93), ("AI-2", 2.55), ("AI-1", 2.97) },
        };

        private static readonly RefRun Run2 = new RefRun
        {
            Name = "Run 2",
            Fixture = "civil3d-2015-storm-sewers-run2.stm",

            // The report was printed at 757.62 and the file was re-saved later
            // with 758.618. Reproducing the printed run means using the printed
            // tailwater; the file's own value is checked separately below.
            TailwaterFt = 757.62,
            Lines = new[]
            {
                new RefLine { PipeName = "Pipe - (9)", UpstreamStructure = "AI-8", Slope = 0.00496, Ca = 1.70, TcMin = 7.0, IntensityInHr = 6.98, QCfs = 11.90, CapacityCfs = 17.25, Surcharged = false, JunctionK = 0.5, HglDnFt = 757.62, HglJunctionFt = 758.16 },
                new RefLine { PipeName = "Pipe - (8)", UpstreamStructure = "AI-7", Slope = 0.00496, Ca = 1.28, TcMin = 6.4, IntensityInHr = 7.19, QCfs =  9.20, CapacityCfs =  8.01, Surcharged = true,  JunctionK = 0.5, HglDnFt = 758.52, HglJunctionFt = 759.88 },
                new RefLine { PipeName = "Pipe - (7)", UpstreamStructure = "AI-6", Slope = 0.00497, Ca = 0.86, TcMin = 5.6, IntensityInHr = 7.51, QCfs =  6.49, CapacityCfs =  8.02, Surcharged = false, JunctionK = 0.5, HglDnFt = 759.88, HglJunctionFt = 760.55 },
                new RefLine { PipeName = "Pipe - (6)", UpstreamStructure = "AI-5", Slope = 0.00495, Ca = 0.48, TcMin = 5.0, IntensityInHr = 7.77, QCfs =  3.73, CapacityCfs =  2.71, Surcharged = true,  JunctionK = 1.0, HglDnFt = 760.55, HglJunctionFt = 762.53 },
            },
            Inlets = new[] { ("AI-8", 3.30), ("AI-7", 3.23), ("AI-6", 2.98), ("AI-5", 3.73) },
        };

        public static TheoryData<string> Runs => new TheoryData<string> { "Run 1", "Run 2" };

        private static RefRun Reference(string name) => name == "Run 1" ? Run1 : Run2;

        // ──────────────────────────── tolerances ────────────────────────────

        /// <summary>Slope, ft/ft: the file stores inverts to two decimals.</summary>
        private const double TolSlope = 2e-5;

        /// <summary>C times A, acres: the tabulation prints two decimals.</summary>
        private const double TolCa = 0.006;

        /// <summary>Intensity, relative: the tabulation prints Tc to one decimal.</summary>
        private const double TolIntensityRel = 0.005;

        /// <summary>Flow, relative, when Tc is Hydraflow's own.</summary>
        private const double TolQRel = 0.006;

        /// <summary>Capacity, relative: two-decimal inverts move sqrt(S).</summary>
        private const double TolCapacityRel = 0.008;

        /// <summary>Tc, minutes: backwater-depth versus normal-depth travel time.</summary>
        private const double TolTcMin = 0.55;

        /// <summary>HGL, ft, given Hydraflow's own flows and full-pipe hydraulics.</summary>
        private const double TolHglFt = 0.15;

        /// <summary>Inlet capture, cfs.</summary>
        private const double TolInletCfs = 0.02;

        // ───────────────────────────── helpers ─────────────────────────────

        private static StmProject Import(RefRun run) =>
            StmReader.Parse(Path.Combine(AppContext.BaseDirectory, "Fixtures", run.Fixture));

        private static IdfCurve Curve(StmProject p) =>
            new IdfCurve(p.IdfA, p.IdfB, p.IdfC, p.MinimumTcMinutes);

        private static LandXmlPipeRecord Pipe(StmProject p, string name) =>
            p.Pipes.Single(x => x.Name == name);

        private static PipeSegment Segment(LandXmlPipeRecord r) => new PipeSegment
        {
            Name = r.Name,
            Shape = PipeShape.Circular,
            DiameterFt = r.DiameterFt,
            Slope = r.Slope,
            ManningN = r.ManningN,
        };

        private static double FullAreaFt2(double diameterFt) => Math.PI * diameterFt * diameterFt / 4.0;

        /// <summary>
        /// C times A accumulated at a line, by walking every structure upstream
        /// of it rather than assuming the run is a single chain.
        /// </summary>
        private static double AccumulatedCa(StmProject p, string pipeName)
        {
            var byEnd = p.Pipes.ToLookup(x => x.EndStructureName, StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stack = new Stack<string>();
            stack.Push(Pipe(p, pipeName).StartStructureName);

            double ca = 0.0;
            while (stack.Count > 0)
            {
                string node = stack.Pop();
                if (string.IsNullOrEmpty(node) || !seen.Add(node))
                {
                    continue;
                }

                if (p.Inlets.TryGetValue(node, out StmInlet inlet))
                {
                    ca += inlet.DrainageAreaAcres * inlet.RunoffCoefficient;
                }

                foreach (var upstream in byEnd[node])
                {
                    stack.Push(upstream.StartStructureName);
                }
            }

            return ca;
        }

        // ────────────────────────────── tests ──────────────────────────────

        [Theory]
        [MemberData(nameof(Runs))]
        public void Slopes_match_the_tabulation(string runName)
        {
            RefRun run = Reference(runName);
            StmProject project = Import(run);

            foreach (RefLine line in run.Lines)
            {
                Assert.Equal(line.Slope, Pipe(project, line.PipeName).Slope, TolSlope);
            }
        }

        [Theory]
        [MemberData(nameof(Runs))]
        public void Accumulated_ca_matches_the_tabulation(string runName)
        {
            RefRun run = Reference(runName);
            StmProject project = Import(run);

            foreach (RefLine line in run.Lines)
            {
                double ca = AccumulatedCa(project, line.PipeName);
                Assert.True(
                    Math.Abs(ca - line.Ca) <= TolCa,
                    run.Name + " " + line.PipeName + ": C*A " + ca.ToString("0.###") +
                    " vs Hydraflow " + line.Ca.ToString("0.##"));
            }
        }

        [Theory]
        [MemberData(nameof(Runs))]
        public void Intensity_and_flow_match_at_hydraflows_own_tc(string runName)
        {
            // With Tc taken from the report, intensity and flow are pure
            // Rational method and have to agree to the printed digit. Anything
            // that drifts here is an IDF or Rational defect, not a method
            // difference: the method difference lives in Tc itself, and is
            // measured by Travel_time_differs_only_by_the_documented_margin.
            RefRun run = Reference(runName);
            StmProject project = Import(run);
            IdfCurve idf = Curve(project);

            foreach (RefLine line in run.Lines)
            {
                double i = idf.Intensity(line.TcMin).IntensityInHr;
                Assert.True(
                    Math.Abs(i - line.IntensityInHr) <= line.IntensityInHr * TolIntensityRel,
                    run.Name + " " + line.PipeName + ": i " + i.ToString("0.###") +
                    " vs Hydraflow " + line.IntensityInHr.ToString("0.##") + " in/hr");

                double ca = AccumulatedCa(project, line.PipeName);
                double q = Rational.Peak(1.0, i, ca).PeakFlowCfs;
                Assert.True(
                    Math.Abs(q - line.QCfs) <= line.QCfs * TolQRel,
                    run.Name + " " + line.PipeName + ": Q " + q.ToString("0.###") +
                    " vs Hydraflow " + line.QCfs.ToString("0.##") + " cfs");
            }
        }

        [Theory]
        [MemberData(nameof(Runs))]
        public void Full_flow_capacity_matches_the_tabulation(string runName)
        {
            RefRun run = Reference(runName);
            StmProject project = Import(run);

            foreach (RefLine line in run.Lines)
            {
                double cap = Manning.Capacity(Segment(Pipe(project, line.PipeName))).FullFlowCfs;
                Assert.True(
                    Math.Abs(cap - line.CapacityCfs) <= line.CapacityCfs * TolCapacityRel,
                    run.Name + " " + line.PipeName + ": capacity " + cap.ToString("0.###") +
                    " vs Hydraflow " + line.CapacityCfs.ToString("0.##") + " cfs");
            }
        }

        [Theory]
        [MemberData(nameof(Runs))]
        public void Which_lines_surcharge_matches_the_tabulation(string runName)
        {
            // Agreeing on capacity is not the same as agreeing on which pipes
            // are over it. This is the line of the report a reviewer acts on.
            RefRun run = Reference(runName);
            StmProject project = Import(run);

            foreach (RefLine line in run.Lines)
            {
                var normal = Manning.NormalDepth(Segment(Pipe(project, line.PipeName)), line.QCfs);
                Assert.True(
                    normal.Surcharged == line.Surcharged,
                    run.Name + " " + line.PipeName + ": surcharged " + normal.Surcharged +
                    ", Hydraflow " + line.Surcharged);
            }
        }

        [Theory]
        [MemberData(nameof(Runs))]
        public void Travel_time_differs_only_by_the_documented_margin(string runName)
        {
            // METHOD DIFFERENCE 1. Hydraflow times a drowned reach at its
            // backwater depth, which is full-pipe here; this engine uses normal
            // depth, which is shallower and faster. The gap accumulates
            // downstream, which is why the top lines agree exactly and the
            // outfall does not. Normal depth errs toward a shorter Tc, a higher
            // intensity and so a larger design flow, which is the conservative
            // side when sizing.
            RefRun run = Reference(runName);
            StmProject project = Import(run);

            // Lines are listed outfall-first, so walking backwards walks down
            // the run, accumulating the travel time of everything above.
            double travelMin = 0.0;
            for (int i = run.Lines.Length - 1; i >= 0; i--)
            {
                RefLine line = run.Lines[i];
                LandXmlPipeRecord pipe = Pipe(project, line.PipeName);

                double tc = project.MinimumTcMinutes + travelMin;
                Assert.True(
                    Math.Abs(tc - line.TcMin) <= TolTcMin,
                    run.Name + " " + line.PipeName + ": Tc " + tc.ToString("0.##") +
                    " vs Hydraflow " + line.TcMin.ToString("0.#") + " min");

                var normal = Manning.NormalDepth(Segment(pipe), line.QCfs);
                double velocity = normal.Surcharged
                    ? line.QCfs / FullAreaFt2(pipe.DiameterFt)
                    : normal.VelocityFps;

                Assert.True(velocity > 0, run.Name + " " + line.PipeName + ": zero velocity");
                travelMin += pipe.LengthFt / velocity / 60.0;
            }
        }

        [Theory]
        [MemberData(nameof(Runs))]
        public void Backwater_gradeline_matches_the_hgl_computations(string runName)
        {
            // Fed Hydraflow's own flows and full-pipe hydraulics, so what is
            // under test is the friction loss, the HEC-22 junction loss, the
            // direction of the stepping and the flow-depth floor, not the
            // modelling choices that make the flows differ in the first place.
            //
            // Run 2 is the one that matters here. Its 24-inch outfall pipe runs
            // partly full, so the grade line arriving at AI-8 is below the
            // crown of the surcharged 18-inch pipe above it. Hydraflow holds it
            // at that crown, 758.52. Stepping straight through instead gives
            // 758.16 and carries the 0.36 ft error up the whole run.
            //
            // METHOD DIFFERENCE 3 is carried in RefLine.JunctionK: Hydraflow
            // charges K = 1.0 at the terminal inlet of a run, treating it as an
            // entrance, where the project K is 0.5.
            RefRun run = Reference(runName);
            StmProject project = Import(run);

            // SteadyBackwaterFromOutfall wants reaches upstream to downstream,
            // and charges each reach's minor loss at its own upstream
            // structure, which is where Hydraflow charges it too.
            var reaches = run.Lines
                .Reverse()
                .Select(line =>
                {
                    LandXmlPipeRecord pipe = Pipe(project, line.PipeName);

                    // The inverts and the flow depth are what let the backwater
                    // pass hold the grade line up at a structure. Without them
                    // it steps straight through and comes out low.
                    var normal = Manning.NormalDepth(Segment(pipe), line.QCfs);

                    return new NetworkReach
                    {
                        Name = line.PipeName,
                        LengthFt = pipe.LengthFt,
                        ManningN = pipe.ManningN,
                        AreaFt2 = FullAreaFt2(pipe.DiameterFt),
                        HydRadiusFt = pipe.DiameterFt / 4.0,
                        FlowCfs = line.QCfs,
                        JunctionLossK = line.JunctionK,
                        DiameterFt = pipe.DiameterFt,
                        FlowSurcharged = line.Surcharged,
                        FlowDepthFt = normal.Surcharged ? pipe.DiameterFt : normal.DepthFt,
                        InvertUpFt = pipe.StartInvertFt,
                        InvertDnFt = pipe.EndInvertFt,
                    };
                })
                .ToList();

            var options = new HglProfileOptions { IncludeJunctionLosses = true };
            var profile = Hgl.SteadyBackwaterFromOutfall(reaches, run.TailwaterFt, options);

            Assert.Equal(run.Lines.Length, profile.Count);

            for (int i = 0; i < run.Lines.Length; i++)
            {
                // profile is upstream to downstream; run.Lines is outfall-first.
                RefLine line = run.Lines[run.Lines.Length - 1 - i];
                HglProfilePoint point = profile[i];

                // Above a floored node Hydraflow's whole gradeline rides
                // higher by one fixed amount. Subtracting it compares the two
                // shapes, instead of re-reporting the same known gap at every
                // structure above it.
                double expectDn = line.HglDnFt - line.LowerThanHydraflowByFt;
                double expectUp = line.HglJunctionFt - line.LowerThanHydraflowByFt;

                Assert.True(
                    Math.Abs(point.HglFt - expectDn) <= TolHglFt,
                    run.Name + " " + line.PipeName + ": HGL down " + point.HglFt.ToString("0.###") +
                    " vs Hydraflow " + line.HglDnFt.ToString("0.##") + " less the recorded " +
                    line.LowerThanHydraflowByFt.ToString("0.##") + " ft floor gap");

                Assert.True(
                    Math.Abs(point.HglUpstreamFt - expectUp) <= TolHglFt,
                    run.Name + " " + line.PipeName + ": HGL at " + line.UpstreamStructure + " " +
                    point.HglUpstreamFt.ToString("0.###") + " vs Hydraflow " +
                    line.HglJunctionFt.ToString("0.##") + " less the recorded " +
                    line.LowerThanHydraflowByFt.ToString("0.##") + " ft floor gap");
            }
        }

        [Theory]
        [MemberData(nameof(Runs))]
        public void Rims_reproduce_hydraflows_own_flooding_call(string runName)
        {
            // The finding a reviewer looks for first, and the one that turns on
            // the rims being imported correctly rather than on any hydraulics.
            // Hydraflow floods exactly one structure across the two runs: AI-4
            // on Run 1, where the HGL lands 0.08 ft over a 759.00 rim. Run 2
            // raised that structure and stays under.
            //
            // An importer that took the outfall's "Ground / Rim Elev Dn = 0"
            // literally, or that pulled a rim off the wrong end of the line,
            // would move this call. That is why it is asserted per structure.
            RefRun run = Reference(runName);
            StmProject project = Import(run);
            var rimAt = project.Structures.ToDictionary(s => s.Name, s => s.RimFt ?? 0.0);

            foreach (RefLine line in run.Lines)
            {
                double rim = rimAt[line.UpstreamStructure];
                bool floods = line.HglJunctionFt > rim;

                Assert.True(
                    floods == line.AboveRim,
                    run.Name + " " + line.UpstreamStructure + ": HGL " +
                    line.HglJunctionFt.ToString("0.##") + " against rim " + rim.ToString("0.##") +
                    " reads as " + (floods ? "flooded" : "contained") + ", Hydraflow reports " +
                    (line.AboveRim ? "flooded" : "contained"));
            }

            // And an outfall's rim is never the zero the file carries.
            Assert.All(project.Structures, s => Assert.True(
                (s.RimFt ?? 0.0) >= (s.InvertFt ?? 0.0),
                s.Name + ": rim " + s.RimFt + " below invert " + s.InvertFt));
        }

        [Theory]
        [MemberData(nameof(Runs))]
        public void Inlets_capture_what_hydraflow_captured(string runName)
        {
            RefRun run = Reference(runName);
            StmProject project = Import(run);
            IdfCurve idf = Curve(project);

            Assert.Equal(run.Inlets.Length, project.Inlets.Count);

            foreach ((string id, double captured) in run.Inlets)
            {
                StmInlet inlet = project.Inlets[id];

                // Every inlet on these runs is in a sag with a 5-minute inlet
                // time, so the local flow is C*A*i at the minimum duration.
                double i = idf.Intensity(inlet.InletTimeMinutes).IntensityInHr;
                double local = Rational
                    .Peak(inlet.RunoffCoefficient, i, inlet.DrainageAreaAcres)
                    .PeakFlowCfs;

                Assert.True(
                    Math.Abs(local - captured) <= TolInletCfs,
                    run.Name + " " + id + ": local flow " + local.ToString("0.###") +
                    " vs Hydraflow " + captured.ToString("0.##") + " cfs");

                Assert.True(
                    inlet.InSag,
                    run.Name + " " + id + ": read as on-grade, Hydraflow has it in a sag");

                // Hydraflow captures 100 % in a sag and reports the ponding it
                // takes. HEC-22 Eq. 4-26 is Q = Cw * P * d^1.5 with P the grate
                // perimeter less the curb side, so the depth needed to pass the
                // local flow has to fit under the rim.
                //
                // NOTE: InletCapacity.SagCapacityCfs takes a LENGTH where
                // HEC-22 wants that perimeter, so calling it with the grate
                // length alone is about three times conservative on a 4 x 4
                // grate. The perimeter is formed explicitly here. See
                // VALIDATION-HYDRAFLOW.md.
                double perimeter = (2.0 * inlet.GrateLengthFt) + inlet.GrateWidthFt;
                double pondingFt = Math.Pow(local / (InletCapacity.SagGrateCw * perimeter), 2.0 / 3.0);

                double available = project.Structures
                    .Where(s => s.Name == id)
                    .Select(s => (s.RimFt ?? 0.0) - (s.InvertFt ?? 0.0))
                    .Single();

                Assert.True(
                    pondingFt < available,
                    run.Name + " " + id + ": needs " + pondingFt.ToString("0.###") +
                    " ft of ponding, only " + available.ToString("0.##") + " ft to the rim");
            }
        }

        [Fact]
        public void Run2_file_stores_the_tailwater_it_was_re_saved_with()
        {
            // The printed report used 757.62; the file on disk was saved later
            // with 758.618. Reading the file's own value keeps the fixture
            // honest about which number produced the reference above.
            StmProject project = Import(Run2);
            Assert.NotNull(project.TailwaterFt);
            Assert.Equal(758.618, project.TailwaterFt.Value, 3);
        }
    }
}
