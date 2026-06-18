using System.Collections.Generic;
using HydroComplete.Engine;

namespace HydroComplete.Civil3D.Writing
{
    /// <summary>Optional GVF profile section for HTML reports.</summary>
    public sealed class GvfReportData
    {
        public double FlowCfs { get; set; }
        public double BottomWidthFt { get; set; }
        public double SideSlopeZ { get; set; }
        public double ManningN { get; set; }
        public double BedSlopeFtPerFt { get; set; }
        public string BoundaryDescription { get; set; } = "";
        public GraduallyVariedFlow.ProfileResult Result { get; set; } = new GraduallyVariedFlow.ProfileResult();

        public bool HasContent =>
            Result.Profile != null && Result.Profile.Count > 0;
    }
}