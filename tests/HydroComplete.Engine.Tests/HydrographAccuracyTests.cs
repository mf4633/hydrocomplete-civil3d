using System;
using System.Collections.Generic;
using System.Linq;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    /// <summary>
    /// Absolute-accuracy regressions for hydrograph generation, superposition,
    /// and routing. Unlike the self-referential volume checks, these pin the
    /// results to independent physical truth: SCS runoff depth times area.
    /// </summary>
    public class HydrographAccuracyTests
    {
        private const double CfPerAcreInch = 3630.0;
        private const double SqFtPerAcre = 43560.0;

        private static double PhysicalVolumeAcreFt(double areaAcres, double cn, double stormIn)
        {
            double runoffIn = ScsRunoff.RunoffDepthInches(stormIn, cn);
            return runoffIn * areaAcres * CfPerAcreInch / SqFtPerAcre;
        }

        private static double SeriesVolumeAcreFt(
            IReadOnlyList<HydrographConvolution.HydrographOrdinate> ordinates)
        {
            double cf = 0.0;
            for (int i = 1; i < ordinates.Count; i++)
            {
                double dtSec = (ordinates[i].TimeHours - ordinates[i - 1].TimeHours) * 3600.0;
                cf += 0.5 * (ordinates[i].FlowCfs + ordinates[i - 1].FlowCfs) * dtSec;
            }
            return cf / SqFtPerAcre;
        }

        private static NetworkAnalysisPipe MakePipe(
            string key, string us, string ds,
            double lengthFt = 500.0, double diameterFt = 2.0, double slope = 0.01)
        {
            return new NetworkAnalysisPipe
            {
                PipeKey = key,
                NetworkName = "NET",
                PipeName = key,
                UpstreamNodeId = us,
                DownstreamNodeId = ds,
                LengthFt = lengthFt,
                Segment = new PipeSegment
                {
                    Name = key,
                    DiameterFt = diameterFt,
                    Slope = slope,
                    ManningN = 0.013,
                },
            };
        }

        private static Catchment MakeCatchment(
            string name, double areaAcres, string outfallId, double tc = 10.0, double cn = 75.0)
        {
            return new Catchment
            {
                Name = name,
                AreaAcres = areaAcres,
                CurveNumber = cn,
                TcMinutes = tc,
                OutfallStructureId = outfallId,
            };
        }

        // The regression that would have caught the 10x deficit: a UH sampled
        // onto a 15-minute grid for a Tc=10-min catchment must still enclose
        // exactly one inch of runoff.
        [Fact]
        public void BuildUnitHydrograph_CoarseGrid_EnclosesOneInch()
        {
            List<HydrographConvolution.UnitHydrographInput> uh =
                HydrographConvolution.BuildUnitHydrograph(
                    HydrographConvolution.UnitHydrographMethod.Scs,
                    areaAcres: 1.0, tcMinutes: 10.0, timestepHours: 0.25);

            double volumeCf = 0.0;
            for (int i = 1; i < uh.Count; i++)
            {
                double dtSec = (uh[i].TimeHours - uh[i - 1].TimeHours) * 3600.0;
                volumeCf += 0.5 * (uh[i].FlowCfsPerIn + uh[i - 1].FlowCfsPerIn) * dtSec;
            }

            Assert.Equal(CfPerAcreInch, volumeCf, CfPerAcreInch * 0.001);
        }

        [Theory]
        [InlineData(HydrographConvolution.UnitHydrographMethod.Scs)]
        [InlineData(HydrographConvolution.UnitHydrographMethod.Snyder)]
        [InlineData(HydrographConvolution.UnitHydrographMethod.Clark)]
        public void BuildUnitHydrograph_FineGrid_EnclosesOneInch_AllMethods(
            HydrographConvolution.UnitHydrographMethod method)
        {
            List<HydrographConvolution.UnitHydrographInput> uh =
                HydrographConvolution.BuildUnitHydrograph(
                    method, areaAcres: 2.5, tcMinutes: 20.0, timestepHours: 1.0 / 60.0);

            double volumeCf = 0.0;
            for (int i = 1; i < uh.Count; i++)
            {
                double dtSec = (uh[i].TimeHours - uh[i - 1].TimeHours) * 3600.0;
                volumeCf += 0.5 * (uh[i].FlowCfsPerIn + uh[i - 1].FlowCfsPerIn) * dtSec;
            }

            Assert.Equal(2.5 * CfPerAcreInch, volumeCf, 2.5 * CfPerAcreInch * 0.001);
        }

        // Tc below 9 minutes used to produce an identically-zero hydrograph
        // (2-ordinate UH plus the dropped last ordinate).
        [Fact]
        public void GenerateTr20_ShortTc_ProducesMassCorrectHydrograph()
        {
            var hydro = HydrographConvolution.GenerateTr20Hydrograph(
                areaAcres: 2.0, curveNumber: 80.0, tcMinutes: 5.0,
                totalRainfallIn: 3.0, timestepHours: 1.0 / 60.0);

            Assert.True(hydro.PeakFlowCfs > 0.0, "short-Tc hydrograph must not be zero");
            double expected = PhysicalVolumeAcreFt(2.0, 80.0, 3.0);
            Assert.Equal(expected, hydro.VolumeAcreFt, expected * 0.02);
        }

        // Convolved volume must equal SCS runoff depth x area even on the
        // coarse 15-minute grid (renormalized UH).
        [Fact]
        public void GenerateTr20_CoarseGrid_MassMatchesScsRunoff()
        {
            var hydro = HydrographConvolution.GenerateTr20Hydrograph(
                areaAcres: 4.05, curveNumber: 75.0, tcMinutes: 10.0,
                totalRainfallIn: 5.0, timestepHours: 0.25);

            double expected = PhysicalVolumeAcreFt(4.05, 75.0, 5.0);
            Assert.Equal(expected, hydro.VolumeAcreFt, expected * 0.02);
            Assert.Equal(hydro.VolumeAcreFt, SeriesVolumeAcreFt(hydro.Ordinates), expected * 0.01);
        }

        // Outfall volume must equal the sum of independent physical catchment
        // volumes - not merely match the engine's own catchment hydrographs.
        [Fact]
        public void Route_YJunction_OutfallMassMatchesPhysicalTruth()
        {
            var catchments = new List<Catchment>
            {
                MakeCatchment("A", 3.0, "S1", tc: 12.0, cn: 70.0),
                MakeCatchment("B", 2.0, "S2", tc: 18.0, cn: 85.0),
            };
            var pipes = new List<NetworkAnalysisPipe>
            {
                MakePipe("P1", "S1", "S3"),
                MakePipe("P2", "S2", "S3"),
                MakePipe("P3", "S3", "S4"),
            };
            var options = new HydrographRouter.HydrographRouterOptions
            {
                StormDepthIn = 5.0,
                TimestepHours = 0.25,
                ApplyMuskingumCunge = false,
            };

            var result = HydrographRouter.Route(catchments, pipes, options);

            double expected = PhysicalVolumeAcreFt(3.0, 70.0, 5.0) + PhysicalVolumeAcreFt(2.0, 85.0, 5.0);
            double outfall = result.PipeHydrographs["P3"].VolumeAcreFt;
            Assert.Equal(expected, outfall, expected * 0.02);
        }

        // The multi-headwater fallback must inject 100% of each unassigned
        // catchment's hydrograph (the old share summed to A_c/totalA).
        [Fact]
        public void Route_MultiHeadwaterFallback_ConservesMass()
        {
            var catchments = new List<Catchment>
            {
                MakeCatchment("A", 3.0, outfallId: "", tc: 12.0, cn: 70.0),
                MakeCatchment("B", 2.0, outfallId: "", tc: 18.0, cn: 85.0),
            };
            var pipes = new List<NetworkAnalysisPipe>
            {
                MakePipe("P1", "S1", "S3"),
                MakePipe("P2", "S2", "S3"),
                MakePipe("P3", "S3", "S4"),
            };
            var options = new HydrographRouter.HydrographRouterOptions
            {
                StormDepthIn = 5.0,
                TimestepHours = 0.25,
                ApplyMuskingumCunge = false,
            };

            var result = HydrographRouter.Route(catchments, pipes, options);

            Assert.Equal(CatchmentAssignmentMethod.AreaWeightedHeadwater, result.AssignmentMethod);
            double expected = PhysicalVolumeAcreFt(3.0, 70.0, 5.0) + PhysicalVolumeAcreFt(2.0, 85.0, 5.0);
            double outfall = result.PipeHydrographs["P3"].VolumeAcreFt;
            Assert.Equal(expected, outfall, expected * 0.02);
        }

        // Superposition volume additivity with unequal-length branches
        // (exercises the zero-pad path never previously tested).
        [Fact]
        public void CombineHydrographs_UnequalLengths_VolumeAdditive()
        {
            var a = new[] { 0.0, 4.0, 8.0, 4.0, 0.0 };
            var b = new[] { 0.0, 2.0, 6.0, 6.0, 2.0, 1.0, 0.5, 0.0 };
            double dt = 0.1;

            double[] combined = HydrographRouter.CombineHydrographs(a, b);

            double VolOf(IReadOnlyList<double> s) =>
                MuskingumRouting.HydrographVolumeAcreFt(s.ToList(), dt);
            Assert.Equal(combined.Length, b.Length);
            Assert.Equal(VolOf(a) + VolOf(b), VolOf(combined), 9);
        }

        // A long lag-routed pipe must actually delay the peak now that the
        // adaptive timestep resolves realistic travel times.
        [Fact]
        public void Route_LongLagPipe_DelaysDownstreamPeak()
        {
            var catchments = new List<Catchment>
            {
                MakeCatchment("A", 3.0, "S1", tc: 10.0, cn: 80.0),
            };
            // Two sequential pipes; flat slope keeps velocity low so travel
            // time spans several adaptive timesteps. MC disabled so the pure
            // lag branch is exercised.
            var pipes = new List<NetworkAnalysisPipe>
            {
                MakePipe("P1", "S1", "S2", lengthFt: 2000.0, slope: 0.001),
                MakePipe("P2", "S2", "S3", lengthFt: 2000.0, slope: 0.001),
            };
            var options = new HydrographRouter.HydrographRouterOptions
            {
                StormDepthIn = 5.0,
                TimestepHours = 0.25,
                ApplyMuskingumCunge = false,
            };

            var result = HydrographRouter.Route(catchments, pipes, options);

            double tPeak1 = result.PipeHydrographs["P1"].TimeToPeakMinutes;
            double tPeak2 = result.PipeHydrographs["P2"].TimeToPeakMinutes;
            Assert.True(tPeak2 > tPeak1,
                $"downstream peak ({tPeak2} min) must lag upstream peak ({tPeak1} min)");
        }

        // The exported ordinates must re-integrate to the reported volume -
        // the gap-filtered export made CSV re-integration diverge.
        [Fact]
        public void Route_ExportedOrdinates_ReintegrateToReportedVolume()
        {
            var catchments = new List<Catchment>
            {
                MakeCatchment("A", 3.0, "S1", tc: 12.0, cn: 75.0),
            };
            var pipes = new List<NetworkAnalysisPipe>
            {
                MakePipe("P1", "S1", "S2"),
            };
            var options = new HydrographRouter.HydrographRouterOptions
            {
                StormDepthIn = 5.0,
                TimestepHours = 0.25,
                ApplyMuskingumCunge = false,
            };

            var result = HydrographRouter.Route(catchments, pipes, options);
            HydrographRouter.PipeHydrographResult pipe = result.PipeHydrographs["P1"];

            double cf = 0.0;
            for (int i = 1; i < pipe.Ordinates.Count; i++)
            {
                double dtSec = (pipe.Ordinates[i].TimeMinutes - pipe.Ordinates[i - 1].TimeMinutes) * 60.0;
                cf += 0.5 * (pipe.Ordinates[i].FlowCfs + pipe.Ordinates[i - 1].FlowCfs) * dtSec;
            }
            double reintegrated = cf / SqFtPerAcre;

            Assert.Equal(pipe.VolumeAcreFt, reintegrated, pipe.VolumeAcreFt * 0.01);
        }
    }
}
