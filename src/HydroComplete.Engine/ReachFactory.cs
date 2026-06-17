using System;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Builds <see cref="NetworkReach"/> geometry from circular pipe segments for
    /// steady HGL stepping (full-barrel or Manning normal depth).
    /// </summary>
    public static class ReachFactory
    {
        /// <summary>
        /// Full-barrel circular area (pi*D²/4) and hydraulic radius (D/4).
        /// </summary>
        public static NetworkReach FromFullBarrel(
            PipeSegment pipe, double designFlowCfs, double lengthFt = 0.0, string name = "")
        {
            if (pipe == null) throw new ArgumentNullException(nameof(pipe));

            double d = pipe.DiameterFt;
            double areaFull = Math.PI * d * d / 4.0;
            double rFull = d / 4.0;

            return new NetworkReach
            {
                Name = string.IsNullOrEmpty(name) ? pipe.Name : name,
                LengthFt = lengthFt,
                ManningN = pipe.ManningN,
                DiameterFt = d,
                AreaFt2 = areaFull,
                HydRadiusFt = rFull,
                FlowCfs = designFlowCfs,
                RelativeDepth = 1.0,
                FlowSurcharged = false,
            };
        }

        /// <summary>
        /// Manning normal depth at <paramref name="designFlowCfs"/> with partial-flow
        /// area and R (full-barrel when flow-surcharged).
        /// </summary>
        public static NetworkReach FromNormalDepth(
            PipeSegment pipe, double designFlowCfs, double lengthFt = 0.0, string name = "")
        {
            if (pipe == null) throw new ArgumentNullException(nameof(pipe));

            double d = pipe.DiameterFt;
            var nd = Manning.NormalDepth(pipe, designFlowCfs);

            double areaFt2;
            double hydRadiusFt;
            if (nd.Surcharged)
            {
                areaFt2 = Math.PI * d * d / 4.0;
                hydRadiusFt = d / 4.0;
            }
            else
            {
                (areaFt2, hydRadiusFt) = Manning.PartialFlowGeometry(d, nd.DepthFt);
            }

            double vh = Hec22.VelocityHeadFt(nd.VelocityFps);

            return new NetworkReach
            {
                Name = string.IsNullOrEmpty(name) ? pipe.Name : name,
                LengthFt = lengthFt,
                ManningN = pipe.ManningN,
                DiameterFt = d,
                AreaFt2 = areaFt2,
                HydRadiusFt = hydRadiusFt,
                FlowCfs = designFlowCfs,
                RelativeDepth = nd.RelativeDepth,
                FlowSurcharged = nd.Surcharged,
                VelHeadUpFt = vh,
                VelHeadDownFt = vh,
            };
        }
    }
}