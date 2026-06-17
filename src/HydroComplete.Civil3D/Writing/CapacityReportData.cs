using System;
using System.Collections.Generic;
using HydroComplete.Civil3D.Reading;
using HydroComplete.Engine;

namespace HydroComplete.Civil3D.Writing
{
    /// <summary>Design-Q vs full-barrel capacity check for command-line and report output.</summary>
    public sealed class CapacityReportData
    {
        public double DesignFlowCfs { get; set; }
        public List<CapacityPipeRow> Rows { get; } = new List<CapacityPipeRow>();
    }

    public sealed class CapacityPipeRow
    {
        public ReadPipe Pipe { get; set; } = null!;
        public Manning.CapacityResult Capacity { get; set; } = null!;
        public Manning.NormalDepthResult NormalDepth { get; set; } = null!;

        public double QFullCfs => Capacity.FullFlowCfs;
        public double FlowRatio => QFullCfs > 0 ? DesignFlowCfs / QFullCfs : 0.0;
        public double DesignFlowCfs { get; set; }
        public bool Surcharged => NormalDepth.Surcharged;
        public double RelativeDepth => NormalDepth.RelativeDepth;
    }

    public static class CapacityReportBuilder
    {
        public static CapacityReportData Build(IReadOnlyList<ReadPipe> pipes, double designFlowCfs)
        {
            if (pipes == null) throw new ArgumentNullException(nameof(pipes));
            if (designFlowCfs <= 0) throw new ArgumentOutOfRangeException(nameof(designFlowCfs));

            var report = new CapacityReportData { DesignFlowCfs = designFlowCfs };
            foreach (ReadPipe rp in pipes)
            {
                try
                {
                    var cap = Manning.Capacity(rp.Segment);
                    var nd = Manning.NormalDepth(rp.Segment, designFlowCfs);
                    report.Rows.Add(new CapacityPipeRow
                    {
                        Pipe = rp,
                        Capacity = cap,
                        NormalDepth = nd,
                        DesignFlowCfs = designFlowCfs,
                    });
                }
                catch (System.Exception)
                {
                    // Caller reports per-pipe skips when needed.
                }
            }

            return report;
        }
    }
}