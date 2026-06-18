using System.Collections.Generic;
using HydroComplete.Engine;

namespace HydroComplete.Civil3D.Writing
{
    /// <summary>Detention routing summary for HTML export.</summary>
    public sealed class DetentionReportSection
    {
        public string StorageDescription { get; set; } = "";

        public List<string> OutletDescriptions { get; } = new List<string>();

        public double RunoffDepthIn { get; set; }

        public double DrainageAreaAcres { get; set; }

        public double SystemTcMinutes { get; set; }

        public DetentionRouting.RoutingResult Result { get; set; } = null!;
    }

    /// <summary>WQV-based BMP sizing summary for HTML export.</summary>
    public sealed class BmpSizingReportSection
    {
        public WaterQualityEngine.BmpSizingResult Result { get; set; } = null!;

        public double DesignStormInches { get; set; }

        public double DrainageAreaAcres { get; set; }

        public double ImperviousPercent { get; set; }
    }

    /// <summary>Treatment train pollutant removal summary for HTML export.</summary>
    public sealed class TreatmentTrainReportSection
    {
        public string LandUse { get; set; } = "";

        public double RunoffDepthIn { get; set; }

        public double DrainageAreaAcres { get; set; }

        public IReadOnlyList<string> BmpChain { get; set; } = System.Array.Empty<string>();

        public WaterQualityEngine.TreatmentTrainResult Result { get; set; } = null!;
    }

    /// <summary>Sediment basin design summary for HTML export.</summary>
    public sealed class SedimentBasinReportSection
    {
        public double DesignFlowCfs { get; set; }

        public double DrainageAreaAcres { get; set; }

        public double SedimentYieldTonsPerAcreYr { get; set; }

        public SedimentBasin.DesignResult Result { get; set; } = null!;
    }

    /// <summary>Optional stormwater sections appended to HTML reports.</summary>
    public sealed class StormwaterReportData
    {
        public DetentionReportSection? Detention { get; set; }

        public BmpSizingReportSection? BmpSizing { get; set; }

        public TreatmentTrainReportSection? TreatmentTrain { get; set; }

        public SedimentBasinReportSection? SedimentBasin { get; set; }

        public bool HasContent =>
            Detention != null || BmpSizing != null || TreatmentTrain != null || SedimentBasin != null;
    }
}