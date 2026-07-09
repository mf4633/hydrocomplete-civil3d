using System;
using System.Linq;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    /// <summary>
    /// Regression tests locking the corrected behavior of the 2026-07 engine audit
    /// (see docs/AUDIT-2026-07.md). Each test pins a value or invariant that was wrong
    /// before the fix, so the defect cannot silently return.
    /// </summary>
    public class AuditRegressionTests
    {
        // F1 — pipe-arch arc radius uses the sagitta relation R = B^2/(8H) + H/2, and the
        // geometry no longer throws for standard AISI/NCSPA span/rise ratios.
        [Fact]
        public void ArchRadius_UsesSagittaRelation_ForStandard43x27()
        {
            // 43 in x 27 in pipe arch: span = 3.5833 ft, rise = 2.25 ft.
            double r = ArchConduit.ArcRadiusFt(3.5833, 2.25);
            Assert.Equal(1.839, r, 2);           // B^2/(8H)+H/2 = 0.713 + 1.125
            Assert.True(r >= 3.5833 / 2.0);       // spring line lies on the intrados
        }

        [Theory]
        [InlineData(3.5833, 2.25)]  // 43x27, span/rise ~1.59
        [InlineData(6.0, 4.0)]      // span/rise 1.5
        [InlineData(5.0, 3.0)]      // span/rise ~1.67
        public void ArchGeometry_DoesNotThrow_ForStandardProportions(double span, double rise)
        {
            double depth = ArchConduit.SpringLineDepthFt(span, rise);
            Assert.True(depth > 0.0);
        }

        // F5 — CurveNumberFromRunoffC is monotonically increasing in the runoff coefficient
        // (higher C -> higher CN), the physical direction; the old code inverted it.
        [Fact]
        public void CurveNumberFromRunoffC_IncreasesWithRunoffCoefficient()
        {
            double low = ScsRunoff.CurveNumberFromRunoffC(0.1);
            double mid = ScsRunoff.CurveNumberFromRunoffC(0.5);
            double high = ScsRunoff.CurveNumberFromRunoffC(0.9);

            Assert.True(low < mid, $"CN(0.1)={low} should be < CN(0.5)={mid}");
            Assert.True(mid < high, $"CN(0.5)={mid} should be < CN(0.9)={high}");
            Assert.True(high > 80.0, $"impervious C=0.9 should yield a high CN, got {high}");
            Assert.True(ScsRunoff.CurveNumberFromRunoffC(1.0) <= 98.0);
        }

        // F7/F8 — Clark UH is nonzero when Tc <= timestep, and its ordinates are cfs whose
        // volume equals one inch of runoff over the drainage area (1 acre-in = 3630 ft^3).
        [Fact]
        public void ClarkUnitHydrograph_IsNonZero_WhenTcBelowTimestep()
        {
            var uh = ClarkUnitHydrograph.Generate(areaAcres: 50.0, tcMinutes: 10.0, timestepMinutes: 15.0);
            Assert.True(uh.PeakFlowCfs > 0.0);
        }

        [Fact]
        public void ClarkUnitHydrograph_EnclosesOneInchOfRunoff()
        {
            double areaAcres = 100.0;
            double dtMinutes = 15.0;
            var uh = ClarkUnitHydrograph.Generate(areaAcres, tcMinutes: 30.0, timestepMinutes: dtMinutes);

            double volumeCf = uh.Ordinates.Sum(o => o.FlowCfs) * (dtMinutes * 60.0);
            double expectedCf = areaAcres * 3630.0;   // 1 in over the area
            Assert.InRange(volumeCf, expectedCf * 0.95, expectedCf * 1.05);
        }

        // F11 — full/surcharged box hydraulic radius uses wetted perimeter 2(w+h).
        [Fact]
        public void FullBarrelBox_HydraulicRadius_UsesFullPerimeter()
        {
            var pipe = new PipeSegment
            {
                Shape = PipeShape.Box,
                WidthFt = 4.0,
                HeightFt = 4.0,
                ManningN = 0.013,
                Slope = 0.01,
            };
            var reach = ReachFactory.FromFullBarrel(pipe, designFlowCfs: 50.0, lengthFt: 100.0, name: "R1");
            // A/P = 16 / (2*(4+4)) = 1.0 ft  (old bug used w+2h = 12 -> 1.333)
            Assert.Equal(1.0, reach.HydRadiusFt, 3);
        }

        // F9 — Muskingum-Cunge output is stamped on the input hydrograph's time axis
        // (data step), not the internally-clamped stability step.
        [Fact]
        public void MuskingumCunge_OutputIsOnDataTimeAxis()
        {
            double dtHours = 0.5;
            var inflow = new[] { 0.0, 10.0, 30.0, 50.0, 30.0, 10.0, 0.0 };
            var result = MuskingumCungeRouting.Route(
                inflow, new MuskingumCungeRouting.ReachParameters(), dtHours);

            for (int i = 0; i < inflow.Length; i++)
                Assert.Equal(i * dtHours, result.Points[i].TimeHours, 6);

            Assert.Equal(1.0, result.Parameters.Sum, 3);           // C1+C2+C3 = 1
            Assert.True(result.PeakOutflowCfs < result.PeakInflowCfs); // still attenuates
        }

        // F10 — Snyder triangular UH encloses one inch of direct runoff (the old base time
        // overstated the enclosed volume by ~49%).
        [Fact]
        public void SnyderUnitHydrograph_EnclosesOneInchOfRunoff()
        {
            double areaAcres = 100.0;
            var uh = SnyderUnitHydrograph.Generate(areaAcres, channelLengthMi: 1.2, centroidDistanceMi: 0.6);
            double volumeCf = uh.Ordinates.Sum(o => o.FlowCfs) * uh.TimeStepHours * 3600.0;
            double expectedCf = areaAcres * 3630.0;  // 1 in over the area
            Assert.InRange(volumeCf, expectedCf * 0.85, expectedCf * 1.10);
        }

        // F29 — Atlas 14 duration parsing keeps empty cells positionally, so a requested ARI
        // column returns its own intensity rather than a left-shifted neighbor's.
        [Fact]
        public void Atlas14DurationRow_KeepsColumnAlignment_WithEmptyCells()
        {
            bool ok = Atlas14Fetcher.TryParseDurationRow("5-min: 1.0,,3.0,4.0", 2, out _, out double intensity);
            Assert.True(ok);
            Assert.Equal(3.0, intensity, 3);   // old left-collapse would have returned 4.0

            // A blank cell at the requested column is "no data", not the next column's value.
            Assert.False(Atlas14Fetcher.TryParseDurationRow("5-min: 1.0,,3.0,4.0", 1, out _, out _));
        }

        // F4 — low-flow inlet-control headwater is no longer floored at ~one diameter
        // (Hc/D was pinned at 1.0 instead of the critical-head ratio).
        [Fact]
        public void CulvertInletHeadwater_NotFlooredAtOneDiameter_AtLowFlow()
        {
            var culvert = new CulvertHydraulics.CulvertParameters
            {
                DiameterIn = 24,
                LengthFt = 100,
                SlopeFtPerFt = 0.01,
                ManningN = 0.013,
                EntranceLossKe = 0.5,
            };
            var hw = CulvertHydraulics.Headwater(5.0, culvert);
            Assert.True(hw.HeadwaterInletFt < 2.0);   // below D = 24 in / 12 = 2.0 ft
        }

        // F3 — outlet-control friction uses the US-customary HDS-5 constant Ku = 29. A long
        // culvert is outlet-controlled with a substantially higher headwater than the SI
        // constant 19.63 would produce (~17.9 ft vs ~11.9 ft here).
        [Fact]
        public void CulvertOutletControl_UsesUsCustomaryFrictionConstant()
        {
            var longCulvert = new CulvertHydraulics.CulvertParameters
            {
                DiameterIn = 24,
                LengthFt = 600,
                SlopeFtPerFt = 0.01,
                ManningN = 0.013,
                EntranceLossKe = 0.5,
            };
            var hw = CulvertHydraulics.Headwater(40.0, longCulvert);
            Assert.Equal(CulvertHydraulics.ControlType.Outlet, hw.Control);
            Assert.True(hw.HeadwaterFt > 15.0);   // SI 19.63 constant would give ~11.9 ft
        }

        // F4 (cap) — at over-capacity flow the inlet headwater stays governed by the submerged
        // form; the critical-head ratio is capped at 1.0 so it cannot blow up and beat it.
        [Fact]
        public void CulvertInletHeadwater_HighFlow_StaysBounded()
        {
            var culvert = new CulvertHydraulics.CulvertParameters
            {
                DiameterIn = 24,
                LengthFt = 100,
                SlopeFtPerFt = 0.01,
                ManningN = 0.013,
                EntranceLossKe = 0.5,
            };
            var hw = CulvertHydraulics.Headwater(50.0, culvert);
            Assert.InRange(hw.HeadwaterFt, 10.0, 13.0);   // ~11.4 ft; an uncapped Hc/D gives ~18
        }
    }
}
