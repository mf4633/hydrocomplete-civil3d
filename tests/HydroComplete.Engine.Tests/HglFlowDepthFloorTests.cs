using System;
using System.Collections.Generic;
using HydroComplete.Engine;
using Xunit;

namespace HydroComplete.Engine.Tests
{
    /// <summary>
    /// The backwater pass must not report a grade line below the water already
    /// standing in the pipe.
    ///
    /// Stepping up from a low tailwater, a reach can arrive at a structure with
    /// a grade line beneath the crown of the surcharged pipe above it. That pipe
    /// is running full, so its water surface is at least its crown, and the
    /// grade line has to be held up to meet it. Skipping that step reports a
    /// grade line that is too LOW at that structure and at every structure above
    /// it, which under-reports flooding: the unconservative direction.
    ///
    /// The geometry here is Run 2 of the Hydraflow comparison, where the fault
    /// showed up as a flat 0.36 ft error all the way up the run. See
    /// VALIDATION-HYDRAFLOW.md.
    /// </summary>
    public class HglFlowDepthFloorTests
    {
        private const double TailwaterFt = 757.62;

        /// <summary>Surcharged 18-inch reach. Crown at its downstream end is 758.52.</summary>
        private static NetworkReach SurchargedUpstreamReach() => new NetworkReach
        {
            Name = "surcharged 18 in",
            LengthFt = 1.0,
            ManningN = 0.012,
            DiameterFt = 1.5,
            AreaFt2 = Math.PI * 1.5 * 1.5 / 4.0,
            HydRadiusFt = 1.5 / 4.0,
            FlowCfs = 9.20,
            FlowSurcharged = true,
            FlowDepthFt = 1.5,
            InvertUpFt = 757.89,
            InvertDnFt = 757.02,
        };

        /// <summary>Partly-full 24-inch outfall reach, 1.22 ft deep.</summary>
        private static NetworkReach PartlyFullOutfallReach() => new NetworkReach
        {
            Name = "partly full 24 in",
            LengthFt = 1.0,
            ManningN = 0.012,
            DiameterFt = 2.0,
            AreaFt2 = Math.PI * 2.0 * 2.0 / 4.0,
            HydRadiusFt = 2.0 / 4.0,
            FlowCfs = 11.90,
            FlowSurcharged = false,
            FlowDepthFt = 1.22,
            InvertUpFt = 756.92,
            InvertDnFt = 756.00,
        };

        private static List<NetworkReach> Network() =>
            new List<NetworkReach> { SurchargedUpstreamReach(), PartlyFullOutfallReach() };

        [Fact]
        public void Grade_line_is_held_up_to_the_crown_of_a_surcharged_reach()
        {
            var profile = Hgl.SteadyBackwaterFromOutfall(
                Network(), TailwaterFt, new HglProfileOptions { EnforceFlowDepthFloor = true });

            // 757.02 invert + 1.5 ft of pipe. The reach is full, so its water
            // surface cannot be lower than this.
            Assert.Equal(758.52, profile[0].HglFt, 2);
        }

        [Fact]
        public void Without_the_floor_the_grade_line_comes_out_low()
        {
            // The defect this guards against, kept as an explicit measurement
            // rather than a description.
            var withFloor = Hgl.SteadyBackwaterFromOutfall(
                Network(), TailwaterFt, new HglProfileOptions { EnforceFlowDepthFloor = true });

            var without = Hgl.SteadyBackwaterFromOutfall(
                Network(), TailwaterFt, new HglProfileOptions { EnforceFlowDepthFloor = false });

            double errorFt = withFloor[0].HglFt - without[0].HglFt;

            Assert.True(
                errorFt > 0.30,
                "turning the floor off should drop the grade line, dropped " + errorFt.ToString("0.###"));

            // And it is low, never high. Low is the direction that hides flooding.
            Assert.True(without[0].HglFt < withFloor[0].HglFt);
        }

        [Fact]
        public void The_outfall_keeps_its_tailwater_even_when_that_is_below_the_crown()
        {
            // Run 1's outfall pipe is surcharged with a crown at 757.50 while the
            // tailwater is 757.37. Hydraflow reports 757.37: a specified
            // tailwater is a boundary condition, not a number to override.
            var outfall = new NetworkReach
            {
                Name = "surcharged outfall",
                LengthFt = 186.07,
                ManningN = 0.012,
                DiameterFt = 1.5,
                AreaFt2 = Math.PI * 1.5 * 1.5 / 4.0,
                HydRadiusFt = 1.5 / 4.0,
                FlowCfs = 10.23,
                FlowSurcharged = true,
                FlowDepthFt = 1.5,
                InvertUpFt = 756.93,
                InvertDnFt = 756.00,
            };

            var profile = Hgl.SteadyBackwaterFromOutfall(
                new List<NetworkReach> { outfall },
                757.365,
                new HglProfileOptions { EnforceFlowDepthFloor = true });

            Assert.Equal(757.365, profile[0].HglFt, 3);
        }

        [Fact]
        public void A_reach_with_no_inverts_is_left_exactly_as_it_was()
        {
            // The floor needs an invert to work from. Callers that supply none
            // must see byte-identical behaviour, which is what lets this ship
            // switched on by default.
            var bare = Network();
            foreach (NetworkReach reach in bare)
            {
                reach.InvertUpFt = null;
                reach.InvertDnFt = null;
            }

            var on = Hgl.SteadyBackwaterFromOutfall(
                bare, TailwaterFt, new HglProfileOptions { EnforceFlowDepthFloor = true });

            var bareAgain = Network();
            foreach (NetworkReach reach in bareAgain)
            {
                reach.InvertUpFt = null;
                reach.InvertDnFt = null;
            }

            var off = Hgl.SteadyBackwaterFromOutfall(
                bareAgain, TailwaterFt, new HglProfileOptions { EnforceFlowDepthFloor = false });

            Assert.Equal(on.Count, off.Count);
            for (int i = 0; i < on.Count; i++)
            {
                Assert.Equal(off[i].HglFt, on[i].HglFt, 9);
                Assert.Equal(off[i].HglUpstreamFt, on[i].HglUpstreamFt, 9);
            }
        }

        [Fact]
        public void The_floor_is_on_by_default()
        {
            // A defect that only goes away when someone remembers to opt in is
            // still shipping.
            Assert.True(new HglProfileOptions().EnforceFlowDepthFloor);
        }

        [Fact]
        public void Raising_the_grade_line_is_recorded_in_the_trace()
        {
            // The report has to be able to say why the grade line jumped, or a
            // reviewer sees an unexplained step in the profile.
            var profile = Hgl.SteadyBackwaterFromOutfall(
                Network(), TailwaterFt, new HglProfileOptions { EnforceFlowDepthFloor = true });

            Assert.Contains(profile[0].Steps, s => s.Label == "floor_ds");
        }
    }
}
