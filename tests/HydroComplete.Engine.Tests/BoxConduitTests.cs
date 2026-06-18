using System;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class BoxConduitTests
    {
        private static PipeSegment MakeBox(double widthFt, double heightFt, double n = 0.013, double slope = 0.01)
            => new PipeSegment
            {
                Shape = PipeShape.Box,
                WidthFt = widthFt,
                HeightFt = heightFt,
                ManningN = n,
                Slope = slope,
            };

        // B=4 ft, H=3 ft, n=0.013, S=0.01 -> Q_full ~ 154.9 cfs (hand-checked).
        [Fact]
        public void Capacity_FullBarrel_MatchesHandCalc()
        {
            var pipe = MakeBox(4.0, 3.0);
            var r = BoxConduit.Capacity(pipe);
            Assert.Equal(154.9, r.FullFlowCfs, 0);
            Assert.Equal(12.9, r.FullVelocityFps, 0);
            Assert.Equal(r.FullFlowCfs, r.PeakFlowCfs, 4);
        }

        [Fact]
        public void FlowAtDepth_PartialDepth_MatchesManningFormula()
        {
            double w = 4.0, h = 3.0, y = 1.5, n = 0.013, s = 0.01;
            var (area, hydR) = BoxConduit.PartialFlowGeometry(w, h, y);
            Assert.Equal(6.0, area, 4);
            Assert.Equal(6.0 / 7.0, hydR, 4);

            double expected = (BoxConduit.Kn / n) * area * Math.Pow(hydR, 2.0 / 3.0) * Math.Sqrt(s);
            double actual = BoxConduit.FlowAtDepth(w, h, y, n, s);
            Assert.Equal(expected, actual, 4);
        }

        [Fact]
        public void NormalDepth_RoundTrips_ThroughFlowAtDepth()
        {
            var pipe = MakeBox(4.0, 3.0);
            double target = 80.0;
            var nd = BoxConduit.NormalDepth(pipe, target);
            Assert.False(nd.Surcharged);
            Assert.InRange(nd.RelativeDepth, 0.0, 1.0);
            double backOut = BoxConduit.FlowAtDepth(
                pipe.WidthFt, pipe.HeightFt, nd.DepthFt, pipe.ManningN, pipe.Slope);
            Assert.Equal(target, backOut, 2);
        }

        [Fact]
        public void NormalDepth_FlowAboveFull_FlagsSurcharge()
        {
            var pipe = MakeBox(4.0, 3.0);
            var nd = BoxConduit.NormalDepth(pipe, 500.0);
            Assert.True(nd.Surcharged);
            Assert.Equal(pipe.HeightFt, nd.DepthFt, 5);
            Assert.Equal(1.0, nd.RelativeDepth, 4);
        }

        [Fact]
        public void FlowAtDepth_BelowInvert_IsZero()
        {
            Assert.Equal(0.0, BoxConduit.FlowAtDepth(4.0, 3.0, 0.0, 0.013, 0.01));
            var (area, r) = BoxConduit.PartialFlowGeometry(4.0, 3.0, 0.0);
            Assert.Equal(0.0, area);
            Assert.Equal(0.0, r);
        }

        [Fact]
        public void Capacity_WiderBox_CarriesMoreFlow()
        {
            var narrow = MakeBox(3.0, 3.0);
            var wide = MakeBox(5.0, 3.0);
            var qNarrow = BoxConduit.Capacity(narrow).FullFlowCfs;
            var qWide = BoxConduit.Capacity(wide).FullFlowCfs;
            Assert.True(qWide > qNarrow);
        }

        [Fact]
        public void PartialFlowGeometry_AtFullDepth_MatchesFullBarrel()
        {
            double w = 4.0, h = 3.0;
            var (area, r) = BoxConduit.PartialFlowGeometry(w, h, h);
            Assert.Equal(w * h, area, 4);
            Assert.Equal((w * h) / (w + 2.0 * h), r, 4);
        }

        [Fact]
        public void ReachFactory_FromNormalDepth_BoxPipe_UsesPartialGeometry()
        {
            var pipe = MakeBox(4.0, 3.0);
            var reach = ReachFactory.FromNormalDepth(pipe, 80.0, lengthFt: 100.0, name: "BX1");
            Assert.False(reach.FlowSurcharged);
            Assert.True(reach.AreaFt2 < pipe.WidthFt * pipe.HeightFt);
            Assert.Null(reach.DiameterFt);
        }
    }
}