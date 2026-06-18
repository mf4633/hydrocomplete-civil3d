using System;

namespace HydroComplete.Engine
{
    /// <summary>
    /// HEC-22 simplified momentum-equation junction loss at a manhole (straight-through).
    /// Public-domain; FHWA HEC-22 (2009) storm drain design.
    ///
    /// Momentum balance for one inflow and one outflow (no lateral):
    ///
    ///     h_J = [Q_u·V_u - Q_d·V_d] / (g·A_d)
    ///
    /// Returns the additional HGL drop at the structure. Negative values (velocity
    /// increase at expansion) are clamped to zero in this stub.
    /// </summary>
    public static class MomentumJunction
    {
        /// <summary>Pipe leg geometry and hydraulics at the manhole face.</summary>
        public sealed class PipeLeg
        {
            /// <summary>Inside diameter, ft.</summary>
            public double DiameterFt { get; set; }

            /// <summary>Flow depth at the junction face, ft (clamped to full barrel when &gt;= D).</summary>
            public double DepthFt { get; set; }

            /// <summary>Discharge, cfs.</summary>
            public double FlowCfs { get; set; }
        }

        public sealed class JunctionLossResult : TracedResult
        {
            /// <summary>Additional HGL drop at the manhole, ft.</summary>
            public double HglDropFt { get; set; }

            public double UpstreamAreaFt2 { get; set; }
            public double DownstreamAreaFt2 { get; set; }
            public double UpstreamVelocityFps { get; set; }
            public double DownstreamVelocityFps { get; set; }
        }

        /// <summary>
        /// Straight-through manhole loss from upstream and downstream pipe legs.
        /// </summary>
        public static JunctionLossResult StraightThroughLoss(PipeLeg upstream, PipeLeg downstream)
        {
            if (upstream == null) throw new ArgumentNullException(nameof(upstream));
            if (downstream == null) throw new ArgumentNullException(nameof(downstream));

            double qUp = upstream.FlowCfs;
            double qDown = downstream.FlowCfs;
            if (qUp < 0) throw new ArgumentOutOfRangeException(nameof(upstream), "Upstream flow must be >= 0.");
            if (qDown < 0) throw new ArgumentOutOfRangeException(nameof(downstream), "Downstream flow must be >= 0.");

            double aUp = FlowAreaFt2(upstream.DiameterFt, upstream.DepthFt);
            double aDown = FlowAreaFt2(downstream.DiameterFt, downstream.DepthFt);

            double vUp = qUp > 0 && aUp > 0 ? qUp / aUp : 0.0;
            double vDown = qDown > 0 && aDown > 0 ? qDown / aDown : 0.0;

            double momentumNumerator = qUp * vUp - qDown * vDown;
            double hRaw = aDown > 0 ? momentumNumerator / (Hec22.G_Fps2 * aDown) : 0.0;
            double hJ = Math.Max(0.0, hRaw);

            var result = new JunctionLossResult
            {
                HglDropFt = hJ,
                UpstreamAreaFt2 = aUp,
                DownstreamAreaFt2 = aDown,
                UpstreamVelocityFps = vUp,
                DownstreamVelocityFps = vDown,
            };

            result.Steps.Add(new CalcStep("A_u", aUp, "ft^2", "partial-flow area at upstream face"));
            result.Steps.Add(new CalcStep("A_d", aDown, "ft^2", "partial-flow area at downstream face"));
            result.Steps.Add(new CalcStep("V_u", vUp, "ft/s", "Q_u/A_u"));
            result.Steps.Add(new CalcStep("V_d", vDown, "ft/s", "Q_d/A_d"));
            result.Steps.Add(new CalcStep("h_J_raw", hRaw, "ft", "[Q_u·V_u - Q_d·V_d]/(g·A_d)"));
            result.Steps.Add(new CalcStep("h_J", hJ, "ft", "max(0, h_J_raw) HGL drop"));
            return result;
        }

        /// <summary>Flow area from circular depth; full barrel when depth &gt;= diameter.</summary>
        public static double FlowAreaFt2(double diameterFt, double depthFt)
        {
            if (diameterFt <= 0) throw new ArgumentOutOfRangeException(nameof(diameterFt), "Diameter must be > 0.");
            if (depthFt < 0) throw new ArgumentOutOfRangeException(nameof(depthFt), "Depth must be >= 0.");
            if (depthFt <= 0) return 0.0;
            if (depthFt >= diameterFt) return Math.PI * diameterFt * diameterFt / 4.0;

            var (area, _) = Manning.PartialFlowGeometry(diameterFt, depthFt);
            return area;
        }
    }
}