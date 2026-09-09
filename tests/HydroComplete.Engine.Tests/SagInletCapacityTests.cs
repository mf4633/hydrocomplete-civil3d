using System;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    /// <summary>
    /// A grate sitting in a sag, per HEC-22 section 4.4.4.
    ///
    /// Two things were wrong before. The weir form was fed the grate's LENGTH
    /// where Eq. 4-26 wants its perimeter, which understates a square grate
    /// about threefold. And there was no orifice branch at all, so once the
    /// grate drowned the weir form kept growing as d^1.5 and over-promised
    /// capacity at exactly the ponding depth where a sag inlet gets interesting.
    ///
    /// The first error was conservative and cost money. The second was not
    /// conservative and cost safety. They partly cancelled, which is the worst
    /// way for two bugs to sit next to each other, because the answer looks
    /// reasonable until the geometry changes.
    /// </summary>
    public class SagInletCapacityTests
    {
        // A 4 ft by 4 ft grate, the size on both Hydraflow reference runs.
        private const double LengthFt = 4.0;
        private const double WidthFt = 4.0;

        [Fact]
        public void Perimeter_excludes_the_side_against_the_curb()
        {
            // Flow cannot enter across the curb face, so that side does not count.
            Assert.Equal(12.0, InletCapacity.SagGratePerimeterFt(LengthFt, WidthFt), 6);

            // Standing clear of the curb, all four sides take flow.
            Assert.Equal(
                16.0,
                InletCapacity.SagGratePerimeterFt(LengthFt, WidthFt, againstCurb: false),
                6);
        }

        [Fact]
        public void Using_the_bare_length_understates_a_square_grate_threefold()
        {
            // This is the defect, kept as a measurement rather than a description.
            const double PondingFt = 0.20;

            double bareLength = InletCapacity.SagCapacityCfs(LengthFt, PondingFt);
            double realPerimeter = InletCapacity.SagCapacityCfs(
                InletCapacity.SagGratePerimeterFt(LengthFt, WidthFt), PondingFt);

            Assert.Equal(3.0, realPerimeter / bareLength, 6);
        }

        [Fact]
        public void Weir_governs_in_shallow_ponding()
        {
            // HEC-22 puts weir flow below roughly 0.4 ft.
            const double PondingFt = 0.20;

            double weir = InletCapacity.SagCapacityCfs(
                InletCapacity.SagGratePerimeterFt(LengthFt, WidthFt), PondingFt);
            double orifice = InletCapacity.SagOrificeCapacityCfs(
                LengthFt * WidthFt * InletCapacity.DefaultClearOpeningRatio, PondingFt);
            double combined = InletCapacity.SagGrateCapacityCfs(LengthFt, WidthFt, PondingFt);

            Assert.True(weir < orifice, "weir should be the binding branch when shallow");
            Assert.Equal(weir, combined, 6);
        }

        [Fact]
        public void Orifice_governs_once_the_grate_drowns()
        {
            // The half the old code was missing. Weir flow grows as d^1.5 and
            // orifice flow only as d^0.5, so past about 1.4 ft the weir form
            // promises capacity the grate does not have.
            const double PondingFt = 2.0;

            double weir = InletCapacity.SagCapacityCfs(
                InletCapacity.SagGratePerimeterFt(LengthFt, WidthFt), PondingFt);
            double combined = InletCapacity.SagGrateCapacityCfs(LengthFt, WidthFt, PondingFt);

            Assert.True(combined < weir, "orifice should cap the weir form at depth");

            double orifice = InletCapacity.SagOrificeCapacityCfs(
                LengthFt * WidthFt * InletCapacity.DefaultClearOpeningRatio, PondingFt);
            Assert.Equal(orifice, combined, 6);
        }

        [Fact]
        public void Capacity_never_decreases_as_the_water_gets_deeper()
        {
            // Taking the lesser of two branches must not produce a curve that
            // dips: a deeper sag has to intercept at least as much.
            double previous = 0.0;
            for (double d = 0.05; d <= 3.0; d += 0.05)
            {
                double q = InletCapacity.SagGrateCapacityCfs(LengthFt, WidthFt, d);
                Assert.True(
                    q >= previous - 1e-9,
                    "capacity dropped at d = " + d.ToString("0.00") + " ft");
                previous = q;
            }
        }

        [Fact]
        public void The_dispatcher_uses_the_full_form_only_when_given_a_width()
        {
            const double PondingFt = 0.20;

            double withWidth = InletCapacity.CapacityCfs(
                InletCapacity.InletType.Sag, LengthFt, PondingFt, 0.01,
                curbOpeningHeightFt: 0.0, grateWidthFt: WidthFt);

            double withoutWidth = InletCapacity.CapacityCfs(
                InletCapacity.InletType.Sag, LengthFt, PondingFt, 0.01);

            Assert.Equal(
                InletCapacity.SagGrateCapacityCfs(LengthFt, WidthFt, PondingFt), withWidth, 6);

            // No width means the old conservative fallback, unchanged, so nobody
            // calling the previous signature sees a different number.
            Assert.Equal(InletCapacity.SagCapacityCfs(LengthFt, PondingFt), withoutWidth, 6);
            Assert.True(withWidth > withoutWidth);
        }

        [Fact]
        public void The_trace_shows_both_branches_when_a_width_is_given()
        {
            // A reviewer has to be able to see which branch governed.
            var check = InletCapacity.CheckInlet(
                designQCfs: 1.0,
                inletType: InletCapacity.InletType.Sag,
                lengthFt: LengthFt,
                flowDepthFt: 0.20,
                gutterSlope: 0.01,
                curbOpeningHeightFt: 0.0,
                grateWidthFt: WidthFt);

            Assert.Contains(check.Steps, s => s.Label == "P");
            Assert.Contains(check.Steps, s => s.Label == "Q_weir");
            Assert.Contains(check.Steps, s => s.Label == "Q_orifice");
        }

        [Fact]
        public void A_nonsense_clear_opening_ratio_is_refused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => InletCapacity.SagGrateCapacityCfs(LengthFt, WidthFt, 0.2, clearOpeningRatio: 0.0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => InletCapacity.SagGrateCapacityCfs(LengthFt, WidthFt, 0.2, clearOpeningRatio: 1.5));
        }

        [Fact]
        public void The_reference_inlets_pond_less_than_a_fifth_of_a_foot()
        {
            // AI-4 on Hydraflow Run 1 captures 3.19 cfs at 100 % in a sag. With
            // the real perimeter that needs about 0.19 ft of ponding against
            // 2.07 ft of structure depth. On the bare length it read 0.41 ft,
            // which is not wrong so much as twice the answer.
            const double CapturedCfs = 3.19;

            double depthFt = 0.01;
            while (InletCapacity.SagGrateCapacityCfs(LengthFt, WidthFt, depthFt) < CapturedCfs
                   && depthFt < 5.0)
            {
                depthFt += 0.001;
            }

            Assert.True(depthFt < 0.20, "ponding came out " + depthFt.ToString("0.###") + " ft");
        }
    }
}
