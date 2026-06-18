using System;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Builds <see cref="NetworkReach"/> geometry from pipe segments for
    /// steady HGL stepping (full-barrel or Manning normal depth).
    /// </summary>
    public static class ReachFactory
    {
        /// <summary>
        /// Full-barrel area and hydraulic radius (circular: pi*D²/4, D/4; box: B*H, A/P).
        /// </summary>
        public static NetworkReach FromFullBarrel(
            PipeSegment pipe, double designFlowCfs, double lengthFt = 0.0, string name = "")
        {
            if (pipe == null) throw new ArgumentNullException(nameof(pipe));

            if (pipe.Shape == PipeShape.Box)
            {
                double w = pipe.WidthFt, h = pipe.HeightFt;
                double areaFull = w * h;
                double rFull = areaFull / (w + 2.0 * h);

                return new NetworkReach
                {
                    Name = string.IsNullOrEmpty(name) ? pipe.Name : name,
                    LengthFt = lengthFt,
                    ManningN = pipe.ManningN,
                    AreaFt2 = areaFull,
                    HydRadiusFt = rFull,
                    FlowCfs = designFlowCfs,
                    RelativeDepth = 1.0,
                    FlowSurcharged = false,
                    FlowDepthFt = h,
                };
            }

            if (pipe.Shape == PipeShape.Arch)
            {
                double span = ArchConduit.ResolveSpan(pipe);
                double rise = ArchConduit.ResolveRise(pipe);
                double areaFull = ArchConduit.PartialFlowGeometry(span, rise, rise).AreaFt2;
                double rFull = ArchConduit.PartialFlowGeometry(span, rise, rise).HydRadiusFt;

                return new NetworkReach
                {
                    Name = string.IsNullOrEmpty(name) ? pipe.Name : name,
                    LengthFt = lengthFt,
                    ManningN = pipe.ManningN,
                    AreaFt2 = areaFull,
                    HydRadiusFt = rFull,
                    FlowCfs = designFlowCfs,
                    RelativeDepth = 1.0,
                    FlowSurcharged = false,
                    FlowDepthFt = rise,
                };
            }

            double d = pipe.DiameterFt;
            double areaCircular = Math.PI * d * d / 4.0;
            double rCircular = d / 4.0;

            return new NetworkReach
            {
                Name = string.IsNullOrEmpty(name) ? pipe.Name : name,
                LengthFt = lengthFt,
                ManningN = pipe.ManningN,
                DiameterFt = d,
                AreaFt2 = areaCircular,
                HydRadiusFt = rCircular,
                FlowCfs = designFlowCfs,
                RelativeDepth = 1.0,
                FlowSurcharged = false,
                FlowDepthFt = d,
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

            if (pipe.Shape == PipeShape.Box)
                return FromBoxNormalDepth(pipe, designFlowCfs, lengthFt, name);

            if (pipe.Shape == PipeShape.Arch)
                return FromArchNormalDepth(pipe, designFlowCfs, lengthFt, name);

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
                FlowDepthFt = nd.Surcharged ? d : nd.DepthFt,
                VelHeadUpFt = vh,
                VelHeadDownFt = vh,
            };
        }

        private static NetworkReach FromBoxNormalDepth(
            PipeSegment pipe, double designFlowCfs, double lengthFt, string name)
        {
            double w = pipe.WidthFt, h = pipe.HeightFt;
            var nd = BoxConduit.NormalDepth(pipe, designFlowCfs);

            double areaFt2;
            double hydRadiusFt;
            if (nd.Surcharged)
            {
                areaFt2 = w * h;
                hydRadiusFt = areaFt2 / (w + 2.0 * h);
            }
            else
            {
                (areaFt2, hydRadiusFt) = BoxConduit.PartialFlowGeometry(w, h, nd.DepthFt);
            }

            double vh = Hec22.VelocityHeadFt(nd.VelocityFps);

            return new NetworkReach
            {
                Name = string.IsNullOrEmpty(name) ? pipe.Name : name,
                LengthFt = lengthFt,
                ManningN = pipe.ManningN,
                AreaFt2 = areaFt2,
                HydRadiusFt = hydRadiusFt,
                FlowCfs = designFlowCfs,
                RelativeDepth = nd.RelativeDepth,
                FlowSurcharged = nd.Surcharged,
                FlowDepthFt = nd.Surcharged ? h : nd.DepthFt,
                VelHeadUpFt = vh,
                VelHeadDownFt = vh,
            };
        }

        private static NetworkReach FromArchNormalDepth(
            PipeSegment pipe, double designFlowCfs, double lengthFt, string name)
        {
            double span = ArchConduit.ResolveSpan(pipe);
            double rise = ArchConduit.ResolveRise(pipe);
            var nd = ArchConduit.NormalDepth(pipe, designFlowCfs);

            double areaFt2;
            double hydRadiusFt;
            if (nd.Surcharged)
            {
                (areaFt2, hydRadiusFt) = ArchConduit.PartialFlowGeometry(span, rise, rise);
            }
            else
            {
                (areaFt2, hydRadiusFt) = ArchConduit.PartialFlowGeometry(span, rise, nd.DepthFt);
            }

            double vh = Hec22.VelocityHeadFt(nd.VelocityFps);

            return new NetworkReach
            {
                Name = string.IsNullOrEmpty(name) ? pipe.Name : name,
                LengthFt = lengthFt,
                ManningN = pipe.ManningN,
                AreaFt2 = areaFt2,
                HydRadiusFt = hydRadiusFt,
                FlowCfs = designFlowCfs,
                RelativeDepth = nd.RelativeDepth,
                FlowSurcharged = nd.Surcharged,
                FlowDepthFt = nd.Surcharged ? rise : nd.DepthFt,
                VelHeadUpFt = vh,
                VelHeadDownFt = vh,
            };
        }
    }
}