using System;
using System.IO;
using System.Linq;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    /// <summary>
    /// Reading real Hydraflow / Civil 3D Storm Sewers <c>.stm</c> files.
    ///
    /// Both fixtures are genuine exports from a Civil 3D project, with their
    /// coordinates moved to a local origin and every hydraulic value left
    /// alone. <c>civil3d-2015-storm-sewers.stm</c> is a four-line trunk written
    /// by the 2015 Civil 3D extension; <c>civil3d-2012-storm-layout.stm</c> is
    /// the same project's two runs as laid out in 2012, before areas were
    /// assigned.
    ///
    /// Each assertion here corresponds to a way the format silently corrupts a
    /// run if read naively — see the remarks on <see cref="StmReader"/>.
    /// </summary>
    public class StmReaderTests
    {
        private static string FixturePath(string name) =>
            Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

        private static StmProject Run1() => StmReader.Parse(FixturePath("civil3d-2015-storm-sewers.stm"));

        [Fact]
        public void Reads_the_civil3d_extension_signature()
        {
            // The file says "Storm Sewers for AutoCAD Civil 3D 2015", not
            // "Hydraflow Storm Sewers"; requiring the latter rejects every
            // real Civil 3D export.
            var project = Run1();
            Assert.True(project.Civil3DFormat);
            Assert.Equal(4, project.Pipes.Count);
        }

        [Fact]
        public void Pipe_roughness_is_the_pipe_not_the_gutter()
        {
            // Every block carries "N-Value = 0.012" and, later,
            // "Gutter N-Value = 0.013". A substring match takes the gutter.
            var project = Run1();
            Assert.All(project.Pipes, p => Assert.Equal(0.012, p.ManningN, 6));
        }

        [Fact]
        public void Rise_and_span_are_feet_in_the_civil3d_format()
        {
            // Rise = 1.5 is an 18-inch pipe, not an 18-inch-diameter read of
            // 1.5 inches. Lines are numbered from the outfall up.
            var project = Run1();
            var byName = project.Pipes.ToDictionary(p => p.Name);
            Assert.Equal(1.5, byName["Pipe - (5)"].DiameterFt, 6);
            Assert.Equal(1.0, byName["Pipe - (2)"].DiameterFt, 6);
        }

        [Fact]
        public void Return_period_is_years_in_the_civil3d_format()
        {
            var project = Run1();
            Assert.Equal(10, project.ReturnPeriodYears);
            Assert.Equal(62.50296, project.IdfA, 4);
            Assert.Equal(8.799997, project.IdfB, 5);
            Assert.Equal(0.7942153, project.IdfC, 6);
        }

        [Fact]
        public void Starting_hgl_becomes_the_tailwater()
        {
            var project = Run1();
            Assert.NotNull(project.TailwaterFt);
            Assert.Equal(757.365, project.TailwaterFt!.Value, 6);
        }

        [Fact]
        public void Each_line_keeps_its_own_end_inverts()
        {
            // Line 2 enters the structure at 757.03 while that structure's
            // outlet invert is 756.93: a 0.10 ft drop through the manhole.
            // Flattening it to the structure invert changes the slope from
            // 0.497 % to 0.554 %.
            var project = Run1();
            var line2 = project.Pipes.Single(p => p.Name == "Pipe - (4)");
            Assert.Equal(757.90, line2.StartInvertFt, 2);
            Assert.Equal(757.03, line2.EndInvertFt, 2);
            Assert.Equal(0.00497, line2.Slope, 5);
        }

        [Fact]
        public void Outfall_rim_is_not_the_zero_in_the_file()
        {
            // Hydraflow writes "Ground / Rim Elev Dn = 0" at an outfall.
            // Taken literally that is 756 ft of negative freeboard.
            var project = Run1();
            Assert.All(project.Structures, s =>
                Assert.True(
                    (s.RimFt ?? 0) >= (s.InvertFt ?? 0),
                    $"{s.Name}: rim {s.RimFt} below invert {s.InvertFt}"));
        }

        [Fact]
        public void Inlets_carry_their_grate_and_hydrology()
        {
            var project = Run1();
            Assert.Equal(4, project.Inlets.Count);

            var ai4 = project.Inlets["AI-4"];
            Assert.Equal(0.54, ai4.DrainageAreaAcres, 3);
            Assert.Equal(0.76, ai4.RunoffCoefficient, 3);
            Assert.Equal(5.0, ai4.InletTimeMinutes, 3);
            Assert.Equal(4.0, ai4.GrateWidthFt, 3);
            Assert.Equal(4.0, ai4.GrateLengthFt, 3);
            Assert.True(ai4.InSag);
        }

        [Fact]
        public void Junction_k_is_the_common_value_not_the_auto_zero()
        {
            // Three lines carry K = 0.5 and the terminal inlet K = 1.0.
            var project = Run1();
            Assert.Equal(0.5, project.JunctionK, 6);
        }

        [Fact]
        public void Reads_the_2012_layout_format_with_two_runs_and_six_storms()
        {
            var project = StmReader.Parse(FixturePath("civil3d-2012-storm-layout.stm"));

            Assert.Equal(8, project.Pipes.Count);
            Assert.Equal(new[] { 2, 5, 10, 25, 50, 100 }, project.IdfCurves.Keys.OrderBy(k => k).ToArray());
            Assert.Equal(2, project.ReturnPeriodYears);
            Assert.Equal(69.87033, project.IdfA, 4);

            // Laid out but not yet assigned: no areas, and an inlet time of 0
            // in the file means "use the 5-minute minimum", not zero.
            Assert.All(project.Inlets.Values, i =>
            {
                Assert.Equal(0.0, i.DrainageAreaAcres, 6);
                Assert.Equal(5.0, i.InletTimeMinutes, 6);
            });

            // Starting HGL = 0 means "not set", not a tailwater at elevation 0.
            Assert.Null(project.TailwaterFt);
        }

        [Fact]
        public void Rejects_a_file_that_is_not_a_storm_sewers_project()
        {
            Assert.Throws<InvalidDataException>(() => StmReader.ParseText("hello, world"));
        }
    }
}
