using System.Collections.Generic;
using HydroComplete.Engine;

namespace HydroComplete.Civil3D.Writing
{
    /// <summary>
    /// Steady HGL profile inputs for one ordered pipe network in an HTML report.
    /// </summary>
    public sealed class HglNetworkReport
    {
        public string NetworkName { get; set; } = "";

        /// <summary>Starting HGL elevation at the headwater, ft.</summary>
        public double StartHglFt { get; set; }

        public List<HglPipeReportRow> Rows { get; } = new List<HglPipeReportRow>();
    }

    /// <summary>One pipe row in the steady HGL profile table.</summary>
    public sealed class HglPipeReportRow
    {
        public string PipeName { get; set; } = "";

        /// <summary>HGL at the upstream end of the reach, ft.</summary>
        public double HglUsFt { get; set; }

        /// <summary>HGL at the downstream end of the reach, ft.</summary>
        public double HglDsFt { get; set; }

        /// <summary>Engine profile point (friction, minor losses, calc steps).</summary>
        public HglProfilePoint Point { get; set; } = null!;

        /// <summary>True when HGL exceeds pipe crown at US or DS end.</summary>
        public bool IsSurcharged { get; set; }

        /// <summary>Normal-depth relative depth d/D at design Q.</summary>
        public double RelativeDepth { get; set; }

        /// <summary>True when design Q exceeds peak open-channel capacity.</summary>
        public bool FlowSurcharged { get; set; }
    }

    /// <summary>Design-flow and per-network HGL results for HTML export.</summary>
    public sealed class HglReportData
    {
        public double DesignFlowCfs { get; set; }

        public bool IncludeMinorLosses { get; set; }

        public List<HglNetworkReport> Networks { get; } = new List<HglNetworkReport>();
    }
}