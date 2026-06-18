using System;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class ArchConduitTests
    {
        private static PipeSegment MakeArch(
            double spanFt, double riseFt, double n = 0.013, double slope = 0.01)
            => new PipeSegment
            {
                Shape = PipeShape.Arch,
                SpanFt = spanFt,
                RiseFt = riseFt,
                ManningN = n,
                Slope = slope,
            };

        // B=2 ft, H=2 ft, n=0.013, S=0.01 -> Q_full ~ 25.5 cfs (hand-checked).
        [Fact]
        public void Capacity_FullBarrel_MatchesHandCalc()
        {
            var pipe = MakeArch(2.0, 2.0);
            var r = ArchConduit.Capacity(pipe);
            Assert.Equal(25.5, r.FullFlowCfs, 0);
            Assert.Equal(7.2, r.FullVelocityFps, 0);
            Assert.True(r.PeakFlowCfs >= r.FullFlowCfs * 0.95);
        }

        [Fact]
        public void ArcRadius_MatchesStandardFormula()
        {
            double span = 2.0, rise = 2.0;
            double expected = span * span / (16.0 * rise) + rise / 2.0;
            Assert.Equal(expected, ArchConduit.ArcRadiusFt(span, rise), 6);
            Assert.Equal(1.125, ArchConduit.ArcRadiusFt(2.0, 2.0), 3);
        }

        [Fact]
        public void FlowAtDepth_PartialDepth_MatchesManningFormula()
        {
            double span = 2.0, rise = 2.0, y = 0.4, n = 0.013, s = 0.01;
            var (area, hydR) = ArchConduit.PartialFlowGeometry(span, rise, y);
            Assert.True(area > 0);
            Assert.True(hydR > 0);

            double expected = (ArchConduit.Kn / n) * area * Math.Pow(hydR, 2.0 / 3.0) * Math.Sqrt(s);
            double actual = ArchConduit.FlowAtDepth(span, rise, y, n, s);
            Assert.Equal(expected, actual, 4);
        }

        [Fact]
        public void NormalDepth_RoundTrips_ThroughFlowAtDepth()
        {
            var pipe = MakeArch(2.0, 2.0);
            double target = 12.0;
            var nd = ArchConduit.NormalDepth(pipe, target);
            Assert.False(nd.Surcharged);
            Assert.InRange(nd.RelativeDepth, 0.0, 1.0);
            double backOut = ArchConduit.FlowAtDepth(
                pipe.SpanFt, pipe.RiseFt, nd.DepthFt, pipe.ManningN, pipe.Slope);
            Assert.Equal(target, backOut, 2);
        }

        [Fact]
        public void NormalDepth_FlowAbovePeak_FlagsSurcharge()
        {
            var pipe = MakeArch(2.0, 2.0);
            var cap = ArchConduit.Capacity(pipe);
            var nd = ArchConduit.NormalDepth(pipe, cap.PeakFlowCfs + 50.0);
            Assert.True(nd.Surcharged);
            Assert.Equal(pipe.RiseFt, nd.DepthFt, 5);
            Assert.Equal(1.0, nd.RelativeDepth, 4);
        }

        [Fact]
        public void FlowAtDepth_BelowInvert_IsZero()
        {
            Assert.Equal(0.0, ArchConduit.FlowAtDepth(2.0, 2.0, 0.0, 0.013, 0.01));
            var (area, r) = ArchConduit.PartialFlowGeometry(2.0, 2.0, 0.0);
            Assert.Equal(0.0, area);
            Assert.Equal(0.0, r);
        }

        [Fact]
        public void PartialFlowGeometry_AtFullDepth_MatchesFullBarrel()
        {
            double span = 2.0, rise = 2.0;
            var (area, hydR) = ArchConduit.PartialFlowGeometry(span, rise, rise);
            double spring = ArchConduit.SpringLineDepthFt(span, rise);
            double rArc = ArchConduit.ArcRadiusFt(span, rise);
            double theta = 2.0 * Math.Acos(1.0 - spring / rArc);
            double areaArc = rArc * rArc / 2.0 * (theta - Math.Sin(theta));
            double expectedArea = areaArc + span * (rise - spring);
            double expectedPerim = rArc * theta + 2.0 * (rise - spring) + span;
            Assert.Equal(expectedArea, area, 4);
            Assert.Equal(expectedArea / expectedPerim, hydR, 4);
        }

        [Fact]
        public void ManningCapacity_DispatchesArchShape()
        {
            var pipe = MakeArch(2.0, 2.0);
            var direct = ArchConduit.Capacity(pipe);
            var dispatched = Manning.Capacity(pipe);
            Assert.Equal(direct.FullFlowCfs, dispatched.FullFlowCfs, 4);
            Assert.Equal(direct.PeakFlowCfs, dispatched.PeakFlowCfs, 4);
        }

        [Fact]
        public void ReachFactory_FromNormalDepth_ArchPipe_UsesPartialGeometry()
        {
            var pipe = MakeArch(2.0, 2.0);
            var fullArea = ArchConduit.PartialFlowGeometry(2.0, 2.0, 2.0).AreaFt2;
            var reach = ReachFactory.FromNormalDepth(pipe, 12.0, lengthFt: 100.0, name: "AR1");
            Assert.False(reach.FlowSurcharged);
            Assert.True(reach.AreaFt2 < fullArea);
            Assert.Null(reach.DiameterFt);
        }

        [Fact]
        public void ResolveSpanRise_FallsBackToWidthHeight()
        {
            var pipe = new PipeSegment
            {
                Shape = PipeShape.Arch,
                WidthFt = 1.5,
                HeightFt = 1.5,
            };
            Assert.Equal(1.5, ArchConduit.ResolveSpan(pipe), 4);
            Assert.Equal(1.5, ArchConduit.ResolveRise(pipe), 4);
        }
    }
}