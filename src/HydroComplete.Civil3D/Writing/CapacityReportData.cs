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

        /// <summary>True when design Q varies by pipe (routed catchment flows).</summary>
        public bool IsRouted { get; set; }

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
            => Build(pipes, designFlowCfs, pipeFlowCfs: null);

        public static CapacityReportData Build(
            IReadOnlyList<ReadPipe> pipes,
            double designFlowCfs,
            IReadOnlyDictionary<string, double>? pipeFlowCfs)
        {
            if (pipes == null) throw new ArgumentNullException(nameof(pipes));

            bool routed = pipeFlowCfs != null && pipeFlowCfs.Count > 0;
            if (!routed && designFlowCfs <= 0)
                throw new ArgumentOutOfRangeException(nameof(designFlowCfs));

            var report = new CapacityReportData
            {
                DesignFlowCfs = designFlowCfs,
                IsRouted = routed,
            };

            foreach (ReadPipe rp in pipes)
            {
                try
                {
                    double q = ResolveDesignFlow(rp, designFlowCfs, pipeFlowCfs);
                    if (q <= 0) continue;

                    var cap = Manning.Capacity(rp.Segment);
                    var nd = Manning.NormalDepth(rp.Segment, q);
                    report.Rows.Add(new CapacityPipeRow
                    {
                        Pipe = rp,
                        Capacity = cap,
                        NormalDepth = nd,
                        DesignFlowCfs = q,
                    });
                }
                catch (System.Exception)
                {
                    // Caller reports per-pipe skips when needed.
                }
            }

            return report;
        }

        private static double ResolveDesignFlow(
            ReadPipe rp,
            double uniformDesignFlowCfs,
            IReadOnlyDictionary<string, double>? pipeFlowCfs)
        {
            if (pipeFlowCfs == null)
                return uniformDesignFlowCfs;

            string key = rp.PipeId.Handle.ToString();
            if (pipeFlowCfs.TryGetValue(key, out double routed) && routed > 0)
                return routed;

            return uniformDesignFlowCfs;
        }
    }
}