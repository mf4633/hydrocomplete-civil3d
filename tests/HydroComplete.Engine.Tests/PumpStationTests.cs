using System.Collections.Generic;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    public class PumpStationTests
    {
        [Fact]
        public void InterpolatePumpHead_LinearBetweenPoints()
        {
            var curve = new List<PumpStation.CurvePoint>
            {
                new PumpStation.CurvePoint { FlowCfs = 0, HeadFt = 60 },
                new PumpStation.CurvePoint { FlowCfs = 50, HeadFt = 40 },
            };

            double head = PumpStation.InterpolatePumpHead(curve, 25);
            Assert.Equal(50, head, 1);
        }

        [Fact]
        public void CheckDuty_PassesWhenPumpHeadExceedsSystemHead()
        {
            PumpStation.DutyResult result = PumpStation.CheckDuty(
                designFlowCfs: 30,
                suctionInvertFt: 900,
                dischargeInvertFt: 940,
                forceMainLengthFt: 50,
                forceMainDiameterFt: 1.5,
                manningN: 0.013,
                PumpStation.DefaultCurve());

            Assert.True(result.Ok);
            Assert.True(result.PumpHeadFt >= result.SystemHeadFt);
            Assert.Equal(40, result.StaticHeadFt, 1);
            Assert.True(result.HeadMarginFt > 0);
        }

        [Fact]
        public void CheckDuty_FailsWhenFlowExceedsCurveAtLowHead()
        {
            var flatCurve = new[]
            {
                new PumpStation.CurvePoint { FlowCfs = 0, HeadFt = 10 },
                new PumpStation.CurvePoint { FlowCfs = 100, HeadFt = 5 },
            };

            PumpStation.DutyResult result = PumpStation.CheckDuty(
                designFlowCfs: 80,
                suctionInvertFt: 900,
                dischargeInvertFt: 950,
                forceMainLengthFt: 500,
                forceMainDiameterFt: 1.0,
                manningN: 0.013,
                flatCurve);

            Assert.False(result.Ok);
        }
    }
}