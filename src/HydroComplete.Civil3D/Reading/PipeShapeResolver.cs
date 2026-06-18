using Autodesk.Civil.DatabaseServices;
using HydroComplete.Engine;

namespace HydroComplete.Civil3D.Reading
{
    /// <summary>
    /// Maps Civil 3D pipe cross-section geometry onto <see cref="PipeSegment"/> shape fields.
    /// Uses <see cref="Pipe.CrossSectionalShape"/> (SweptShapeType) plus inner width/height.
    /// </summary>
    internal static class PipeShapeResolver
    {
        public static void ApplyToSegment(Pipe pipe, PipeSegment segment)
        {
            if (pipe == null || segment == null) return;

            SweptShapeType swept = pipe.CrossSectionalShape;
            segment.Shape = MapShape(swept);

            double width = pipe.InnerDiameterOrWidth;
            double height = pipe.InnerHeight;

            switch (segment.Shape)
            {
                case PipeShape.Circular:
                    segment.DiameterFt = width;
                    segment.WidthFt = 0;
                    segment.HeightFt = 0;
                    segment.SpanFt = 0;
                    segment.RiseFt = 0;
                    break;

                case PipeShape.Box:
                    segment.WidthFt = width;
                    segment.HeightFt = height > 0 ? height : width;
                    segment.DiameterFt = segment.WidthFt;
                    segment.SpanFt = segment.WidthFt;
                    segment.RiseFt = segment.HeightFt;
                    break;

                case PipeShape.Arch:
                    segment.SpanFt = width;
                    segment.RiseFt = height > 0 ? height : width;
                    segment.WidthFt = segment.SpanFt;
                    segment.HeightFt = segment.RiseFt;
                    segment.DiameterFt = segment.SpanFt;
                    break;
            }
        }

        internal static PipeShape MapShape(SweptShapeType swept)
        {
            switch (swept)
            {
                case SweptShapeType.Rectangular:
                    return PipeShape.Box;

                case SweptShapeType.Arched:
                case SweptShapeType.EggShaped:
                    return PipeShape.Arch;

                case SweptShapeType.Circular:
                case SweptShapeType.Elliptical:
                case SweptShapeType.HorizontalElliptical:
                    return PipeShape.Circular;

                default:
                    return PipeShape.Circular;
            }
        }

        internal static LandXmlPipeShape ToLandXmlShape(PipeShape shape)
        {
            switch (shape)
            {
                case PipeShape.Box:
                    return LandXmlPipeShape.Box;
                case PipeShape.Arch:
                    return LandXmlPipeShape.Arch;
                default:
                    return LandXmlPipeShape.Circular;
            }
        }
    }
}