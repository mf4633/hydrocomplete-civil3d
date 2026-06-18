using System;
using System.Linq;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class CulvertHydraulicsTests
    {
        private static CulvertHydraulics.CulvertParameters DefaultCulvert()
            => new CulvertHydraulics.CulvertParameters
            {
                DiameterIn = 24,
                LengthFt = 100,
                SlopeFtPerFt = 0.01,
                ManningN = 0.013,
                EntranceLossKe = 0.5,
            };

        [Fact]
        public void OrificeFlow_UnitHead_24InPipe_MatchesEquation()
        {
            // A=pi*1^2, Q = 0.6*pi*sqrt(64.4) ≈ 15.1 cfs
            double q = CulvertHydraulics.OrificeFlowCfs(1.0, 2.0);
            Assert.Equal(15.1, q, 0);
        }

        [Fact]
        public void ManningFullFlow_24In_1Percent_MatchesManning()
        {
            double q = CulvertHydraulics.ManningFullFlowCfs(2.0, 0.01, 0.013);
            Assert.True(q > 10.0);
            Assert.True(q < 25.0);
        }

        [Fact]
        public void Headwater_ZeroFlow_IsZero()
        {
            var hw = CulvertHydraulics.Headwater(0.0, DefaultCulvert());
            Assert.Equal(0.0, hw.HeadwaterFt);
            Assert.Equal(CulvertHydraulics.ControlType.Inlet, hw.Control);
        }

        [Fact]
        public void Headwater_50Cfs_IsPositive()
        {
            var hw = CulvertHydraulics.Headwater(50.0, DefaultCulvert());
            Assert.True(hw.HeadwaterFt > 0);
            Assert.True(hw.HeadwaterInletFt > 0 || hw.HeadwaterOutletFt > 0);
            Assert.NotEmpty(hw.Steps);
        }

        [Fact]
        public void RatingCurve_IsMonotonicNonDecreasing()
        {
            var curve = CulvertHydraulics.RatingCurve(DefaultCulvert(), maxDischargeCfs: 100.0, pointCount: 21);
            for (int i = 1; i < curve.Count; i++)
                Assert.True(curve[i].HeadwaterFt >= curve[i - 1].HeadwaterFt - 0.01);
        }

        [Fact]
        public void DischargeFromHeadwater_ZeroHead_IsZero()
            => Assert.Equal(0.0, CulvertHydraulics.DischargeFromHeadwaterFt(0.0, DefaultCulvert()));
    }
}