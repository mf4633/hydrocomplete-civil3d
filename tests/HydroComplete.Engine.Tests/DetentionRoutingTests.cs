using System;
using System.Collections.Generic;
using System.Linq;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class DetentionRoutingTests
    {
        private static List<DetentionRouting.HydrographPoint> TriangularInflow(
            double peakCfs,
            double durationHours,
            double dtHours = 0.1)
        {
            var hydro = new List<DetentionRouting.HydrographPoint>();
            for (double t = 0.0; t <= durationHours; t += dtHours)
            {
                double flow = t <= durationHours / 2.0
                    ? peakCfs * (t / (durationHours / 2.0))
                    : peakCfs * (1.0 - (t - durationHours / 2.0) / (durationHours / 2.0));
                hydro.Add(new DetentionRouting.HydrographPoint
                {
                    TimeHours = Math.Round(t, 2),
                    FlowCfs = Math.Max(0.0, flow),
                });
            }

            return hydro;
        }

        [Fact]
        public void OrificeDischarge_MatchesHandCalculation()
        {
            // Cd=0.6, D=12 in (1 ft), h=4 ft
            // A = pi/4 ft², Q = 0.6 * pi/4 * sqrt(2*32.2*4) ≈ 7.56 cfs
            double q = OutletStructures.OrificeDischargeCfs(0.6, 12.0, 4.0);
            Assert.Equal(7.56, q, 1);
        }

        [Fact]
        public void OrificeDischarge_ZeroHead_ReturnsZero()
        {
            Assert.Equal(0.0, OutletStructures.OrificeDischargeCfs(0.6, 12.0, 0.0));
            Assert.Equal(0.0, OutletStructures.OrificeDischargeCfs(0.6, 12.0, -1.0));
        }

        [Fact]
        public void SharpCrestedWeir_MatchesHandCalculation()
        {
            // Cw=3.0, L=8 ft, h=2 ft -> Q = 3*8*2^1.5 = 67.88 cfs
            double q = OutletStructures.SharpCrestedWeirDischargeCfs(3.0, 8.0, 2.0);
            Assert.Equal(67.88, q, 1);
        }

        [Fact]
        public void RiserDischarge_UsesLesserOfWeirAndOrifice()
        {
            // Governing discharge is always min(weir around perimeter, orifice through barrel)
            double lowHead = OutletStructures.RiserDischargeCfs(0.6, 2.75, 12.0, 0.5);
            double qWeirLow = OutletStructures.SharpCrestedWeirDischargeCfs(2.75, Math.PI, 0.5);
            double qOrificeLow = OutletStructures.OrificeDischargeCfs(0.6, 12.0, 0.5);
            Assert.Equal(Math.Min(qWeirLow, qOrificeLow), lowHead, 2);

            double highHead = OutletStructures.RiserDischargeCfs(0.6, 2.75, 12.0, 10.0);
            double qWeirHigh = OutletStructures.SharpCrestedWeirDischargeCfs(2.75, Math.PI, 10.0);
            double qOrificeHigh = OutletStructures.OrificeDischargeCfs(0.6, 12.0, 10.0);
            Assert.Equal(Math.Min(qWeirHigh, qOrificeHigh), highHead, 2);
            Assert.Equal(qOrificeHigh, highHead, 2);
        }

        [Fact]
        public void StageStorage_AverageEndArea_MatchesHandCalculation()
        {
            var table = new List<StageStorage.ElevationAreaPoint>
            {
                new StageStorage.ElevationAreaPoint { ElevationFt = 100.0, AreaFt2 = 0.0 },
                new StageStorage.ElevationAreaPoint { ElevationFt = 101.0, AreaFt2 = 1000.0 },
                new StageStorage.ElevationAreaPoint { ElevationFt = 102.0, AreaFt2 = 3000.0 },
            };

            var result = StageStorage.BuildFromElevationArea(table);

            Assert.Equal(500.0, result.Points[1].StorageFt3, 0);
            Assert.Equal(2500.0, result.Points[2].StorageFt3, 0);
            Assert.Equal(2500.0, result.TotalStorageFt3, 0);
            Assert.NotEmpty(result.Steps);
        }

        [Fact]
        public void StageStorage_InterpolateStorage_LinearBetweenPoints()
        {
            var table = StageStorage.BuildFromElevationArea(new List<StageStorage.ElevationAreaPoint>
            {
                new StageStorage.ElevationAreaPoint { ElevationFt = 0.0, AreaFt2 = 1000.0 },
                new StageStorage.ElevationAreaPoint { ElevationFt = 2.0, AreaFt2 = 3000.0 },
            }).Points;

            // Between elev 0 (S=0) and elev 2 (S=4000), storage is linear in elevation
            double storage = StageStorage.InterpolateStorage(1.0, table);
            Assert.Equal(2000.0, storage, 0);
        }

        [Fact]
        public void MultiOutletRating_SumsIndividualOutlets()
        {
            var outlets = new List<OutletStructures.OutletDefinition>
            {
                new OutletStructures.OrificeOutlet
                {
                    Name = "orifice",
                    DiameterInches = 12.0,
                    Cd = 0.6,
                    InvertElevFt = 0.0,
                },
                new OutletStructures.WeirOutlet
                {
                    Name = "spillway",
                    LengthFt = 8.0,
                    Cw = 3.0,
                    CrestElevFt = 4.0,
                },
            };

            var rating = OutletStructures.BuildRatingCurve(outlets, 0.0, 6.0, 1.0);
            var at6 = rating.Points.Last();

            double expectedOrifice = OutletStructures.OrificeDischargeCfs(0.6, 12.0, 6.0);
            double expectedWeir = OutletStructures.SharpCrestedWeirDischargeCfs(3.0, 8.0, 2.0);

            Assert.Equal(expectedOrifice, at6.OutletFlowsCfs["orifice"], 2);
            Assert.Equal(expectedWeir, at6.OutletFlowsCfs["spillway"], 2);
            Assert.Equal(expectedOrifice + expectedWeir, at6.TotalOutflowCfs, 2);
        }

        [Fact]
        public void Route_AttenuatesPeakOutflow()
        {
            var inflow = TriangularInflow(50.0, 3.0);
            var curve = DetentionRouting.BuildPrismaticStorageIndicationCurve(
                50000.0,
                new List<OutletStructures.OutletDefinition>
                {
                    new OutletStructures.OrificeOutlet { Name = "orifice", DiameterInches = 6.0 },
                    new OutletStructures.WeirOutlet
                    {
                        Name = "spillway",
                        LengthFt = 8.0,
                        CrestElevFt = 7.2,
                    },
                },
                avgDepthFt: 8.0);

            var result = DetentionRouting.Route(inflow, curve, 0.1);

            Assert.True(result.PeakOutflowCfs < result.PeakInflowCfs);
            Assert.True(result.ReductionPercent > 0.0);
            Assert.NotEmpty(result.Ordinates);
            Assert.NotEmpty(result.Steps);
        }

        [Fact]
        public void Route_MassBalanceWithinTolerance()
        {
            var inflow = TriangularInflow(50.0, 3.0);
            var curve = DetentionRouting.BuildPrismaticStorageIndicationCurve(
                50000.0,
                new List<OutletStructures.OutletDefinition>
                {
                    new OutletStructures.OrificeOutlet { Name = "orifice", DiameterInches = 6.0 },
                },
                avgDepthFt: 8.0);

            var result = DetentionRouting.Route(inflow, curve, 0.1);
            double err = DetentionRouting.ContinuityErrorPercent(result);

            Assert.True(err < 2.0, $"Continuity error {err:F2}% exceeds 2%");
        }

        [Fact]
        public void Route_WithScsUnitHydrographInflow_AttenuatesPeak()
        {
            var uh = ScsUnitHydrograph.Generate(50.0, 30.0);
            var inflow = DetentionRouting.InflowFromUnitHydrograph(uh, runoffDepthInches: 2.0);

            var curve = DetentionRouting.BuildPrismaticStorageIndicationCurve(
                80000.0,
                new List<OutletStructures.OutletDefinition>
                {
                    new OutletStructures.OrificeOutlet { Name = "primary", DiameterInches = 8.0 },
                },
                avgDepthFt: 10.0);

            var result = DetentionRouting.Route(inflow, curve, 0.1);

            Assert.True(result.PeakOutflowCfs < result.PeakInflowCfs);
            Assert.True(result.PeakStorageFt3 > 0.0);
        }

        [Fact]
        public void Route_MultiOutlet_SplitVolumesMatchTotalOutflow()
        {
            var inflow = TriangularInflow(40.0, 2.5);
            var curve = DetentionRouting.BuildPrismaticStorageIndicationCurve(
                30000.0,
                new List<OutletStructures.OutletDefinition>
                {
                    new OutletStructures.OrificeOutlet
                    {
                        Name = "lowOrifice",
                        DiameterInches = 6.0,
                        InvertElevFt = 0.0,
                    },
                    new OutletStructures.WeirOutlet
                    {
                        Name = "emergency",
                        LengthFt = 12.0,
                        Cw = 3.0,
                        CrestElevFt = 5.4,
                    },
                },
                avgDepthFt: 6.0);

            var result = DetentionRouting.Route(inflow, curve, 0.1);

            Assert.Equal(2, result.OutletHydrographs.Count);
            Assert.Contains("lowOrifice", result.OutletHydrographs.Keys);
            Assert.Contains("emergency", result.OutletHydrographs.Keys);

            double totalOutVol = 0.0;
            for (int i = 1; i < result.Ordinates.Count; i++)
            {
                double dt = (result.Ordinates[i].TimeHours - result.Ordinates[i - 1].TimeHours) * 3600.0;
                totalOutVol += (result.Ordinates[i].OutflowCfs + result.Ordinates[i - 1].OutflowCfs) / 2.0 * dt;
            }

            double sumOutletVols = 0.0;
            foreach (var hydro in result.OutletHydrographs.Values)
            {
                for (int i = 1; i < hydro.Count; i++)
                {
                    double dt = (hydro[i].TimeHours - hydro[i - 1].TimeHours) * 3600.0;
                    sumOutletVols += (hydro[i].FlowCfs + hydro[i - 1].FlowCfs) / 2.0 * dt;
                }
            }

            double splitErr = Math.Abs(totalOutVol - sumOutletVols) / totalOutVol * 100.0;
            Assert.True(splitErr < 1.0, $"Outlet split error {splitErr:F2}%");
        }

        [Fact]
        public void Route_EmptyInflow_ReturnsZeroPeaks()
        {
            var curve = DetentionRouting.BuildPrismaticStorageIndicationCurve(
                10000.0,
                new List<OutletStructures.OutletDefinition>
                {
                    new OutletStructures.OrificeOutlet { DiameterInches = 6.0 },
                });

            var result = DetentionRouting.Route(
                Array.Empty<DetentionRouting.HydrographPoint>(),
                curve);

            Assert.Equal(0.0, result.PeakInflowCfs);
            Assert.Equal(0.0, result.PeakOutflowCfs);
            Assert.Empty(result.Ordinates);
        }

        [Fact]
        public void BuildFromElevationArea_TooFewPoints_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                StageStorage.BuildFromElevationArea(new List<StageStorage.ElevationAreaPoint>
                {
                    new StageStorage.ElevationAreaPoint { ElevationFt = 0, AreaFt2 = 100 },
                }));
        }

        [Fact]
        public void StageStorage_InterpolateElevation_InverseOfStorage()
        {
            var built = StageStorage.BuildFromElevationArea(new List<StageStorage.ElevationAreaPoint>
            {
                new StageStorage.ElevationAreaPoint { ElevationFt = 10.0, AreaFt2 = 500.0 },
                new StageStorage.ElevationAreaPoint { ElevationFt = 12.0, AreaFt2 = 1500.0 },
            });

            double elev = StageStorage.InterpolateElevation(1000.0, built.Points);
            double storage = StageStorage.InterpolateStorage(elev, built.Points);
            Assert.Equal(1000.0, storage, 0);
        }
    }
}