using System.Collections.Generic;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class TimeOfConcentrationTr55Tests
    {
        [Fact]
        public void SheetFlow_CapsLengthAt100Ft()
        {
            var at100 = TimeOfConcentration.SheetFlow(0.40, 100.0, 3.0, 0.05);
            var cappedPath = TimeOfConcentration.SheetFlow(0.40, 200.0, 3.0, 0.05);
            Assert.Equal(at100.TcMinutes, cappedPath.TcMinutes, 3);
        }

        [Fact]
        public void ShallowConcentrated_Paved_IsFasterThanUnpaved()
        {
            var unpaved = TimeOfConcentration.ShallowConcentrated(500.0, 0.05);
            var paved = TimeOfConcentration.ShallowConcentrated(
                500.0, 0.05, TimeOfConcentration.ShallowSurfaceType.Paved);
            Assert.True(paved.TcMinutes < unpaved.TcMinutes);
        }

        [Fact]
        public void FromTr55Segments_SumsSheetAndShallow()
        {
            var segments = new List<TimeOfConcentration.TcSegment>
            {
                new TimeOfConcentration.TcSegment
                {
                    Name = "Sheet",
                    Type = "sheet",
                    LengthFt = 100.0,
                    ManningN = 0.40,
                    Rainfall2YearIn = 3.0,
                    Slope = 0.05,
                },
                new TimeOfConcentration.TcSegment
                {
                    Name = "Shallow",
                    Type = "shallow",
                    LengthFt = 500.0,
                    Slope = 0.05,
                    SurfaceType = TimeOfConcentration.ShallowSurfaceType.Unpaved,
                },
            };

            var composite = TimeOfConcentration.FromTr55Segments(segments);
            double expected = TimeOfConcentration.SheetFlow(0.40, 100.0, 3.0, 0.05).TcMinutes
                + TimeOfConcentration.ShallowConcentrated(500.0, 0.05).TcMinutes;

            Assert.Equal(expected, composite.TcMinutes, 2);
            Assert.NotEmpty(composite.Steps);
        }

        [Fact]
        public void FromTr55Segments_IncludesChannelSegment()
        {
            var segments = new List<TimeOfConcentration.TcSegment>
            {
                new TimeOfConcentration.TcSegment
                {
                    Name = "Channel",
                    Type = "channel",
                    LengthFt = 200.0,
                    Slope = 0.005,
                    BottomWidthFt = 2.0,
                    SideSlopeZ = 1.0,
                    DepthFt = 1.0,
                    ManningN = 0.013,
                },
            };

            var composite = TimeOfConcentration.FromTr55Segments(segments);
            var flow = ChannelHydraulics.FlowAtDepth(2.0, 1.0, 1.0, 0.013, 0.005);
            double expectedMin = 200.0 / flow.VelocityFps / 60.0;

            Assert.Equal(expectedMin, composite.TcMinutes, 2);
            Assert.Contains(composite.Steps, s => s.Label == "Tt[Channel]");
        }
    }
}