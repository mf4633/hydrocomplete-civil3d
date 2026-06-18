using System;
using System.Collections.Generic;

namespace HydroComplete.Engine
{
    /// <summary>Severity of a design-criteria finding.</summary>
    public enum DesignFindingSeverity
    {
        Warning,
        Error,
    }

    /// <summary>A single design-criteria finding tied to a pipe or node id.</summary>
    public sealed class DesignFinding
    {
        public DesignFinding(DesignFindingSeverity severity, string id, string message)
        {
            Severity = severity;
            Id = id ?? "";
            Message = message ?? "";
        }

        public DesignFindingSeverity Severity { get; }
        public string Id { get; }
        public string Message { get; }

        internal static DesignFinding Warn(string id, string message) =>
            new DesignFinding(DesignFindingSeverity.Warning, id, message);

        internal static DesignFinding Error(string id, string message) =>
            new DesignFinding(DesignFindingSeverity.Error, id, message);
    }

    /// <summary>Agency-style review thresholds for storm-sewer design criteria.</summary>
    public sealed class ReviewCriteria
    {
        /// <summary>Minimum design velocity (ft/s) for self-cleansing.</summary>
        public double MinVelocityFps { get; set; } = 2.0;

        /// <summary>Maximum design velocity (ft/s) before scour/erosion concern.</summary>
        public double MaxVelocityFps { get; set; } = 10.0;

        /// <summary>Maximum design flow as a fraction of just-full capacity before warning.</summary>
        public double MaxPctFull { get; set; } = 0.85;

        /// <summary>Minimum cover (ft) from ground (rim) to pipe crown.</summary>
        public double MinCoverFt { get; set; } = 1.0;

        /// <summary>Slopes below this (ft/ft) are flagged as suspiciously flat.</summary>
        public double MinSlope { get; set; } = 0.0005;

        /// <summary>Warn when a pipe is smaller than an upstream pipe feeding the same node.</summary>
        public bool CheckSizeProgression { get; set; } = true;
    }

    /// <summary>Structure/node data for cover and HGL surface-flooding checks.</summary>
    public sealed class ReviewNodeInput
    {
        public string Id { get; set; } = "";

        /// <summary>Ground/rim elevation, ft. When null, cover checks are skipped.</summary>
        public double? RimFt { get; set; }

        /// <summary>Structure sump/invert elevation, ft. Optional; pipe-end invert is used when null.</summary>
        public double? InvertFt { get; set; }

        /// <summary>Hydraulic grade at the structure, ft. When null, HGL flooding checks are skipped.</summary>
        public double? HglFt { get; set; }
    }

    /// <summary>Analyzed pipe data for design-criteria review.</summary>
    public sealed class ReviewPipeInput
    {
        public string Id { get; set; } = "";
        public string UpstreamNodeId { get; set; } = "";
        public string DownstreamNodeId { get; set; } = "";
        public double DiameterFt { get; set; }

        /// <summary>Signed slope (ft/ft); negative when the pipe runs uphill.</summary>
        public double Slope { get; set; }

        public double DesignFlowCfs { get; set; }

        /// <summary>Just-full Manning capacity, cfs.</summary>
        public double FullCapacityCfs { get; set; }

        public double VelocityFps { get; set; }
        public bool Surcharged { get; set; }
        public double UpstreamInvertFt { get; set; }
        public double DownstreamInvertFt { get; set; }
    }

    /// <summary>
    /// Design-standard review of an analyzed storm-sewer network. Pure engine:
    /// flags velocity, capacity, slope, cover, size progression, and HGL
    /// surcharging — the engineering criteria behind SS_VALIDATE.
    /// </summary>
    public static class DesignReview
    {
        private const double SizeToleranceFt = 1e-9;

        /// <summary>
        /// Review pipes and nodes against design criteria. Returns findings
        /// (empty = passes all checked rules).
        /// </summary>
        public static List<DesignFinding> ReviewNetwork(
            IReadOnlyList<ReviewPipeInput> pipes,
            IReadOnlyDictionary<string, ReviewNodeInput>? nodes,
            ReviewCriteria? criteria = null)
        {
            if (pipes == null) throw new ArgumentNullException(nameof(pipes));

            var c = criteria ?? new ReviewCriteria();
            var nodeMap = nodes ?? new Dictionary<string, ReviewNodeInput>(StringComparer.OrdinalIgnoreCase);
            var findings = new List<DesignFinding>();

            foreach (ReviewPipeInput pipe in pipes)
            {
                string id = pipe.Id;

                if (pipe.Slope < 0.0)
                {
                    findings.Add(DesignFinding.Error(id,
                        string.Format(
                            "Pipe {0}: adverse slope {1:0.0000} ft/ft (runs uphill)",
                            id, pipe.Slope)));
                }
                else if (pipe.Slope < c.MinSlope)
                {
                    findings.Add(DesignFinding.Warn(id,
                        string.Format(
                            "Pipe {0}: very flat slope {1:0.0000} ft/ft (< {2:0.0000})",
                            id, pipe.Slope, c.MinSlope)));
                }

                if (pipe.Surcharged)
                {
                    findings.Add(DesignFinding.Error(id,
                        string.Format(
                            "Pipe {0}: surcharged — design Q {1:0.00} cfs exceeds capacity {2:0.00} cfs",
                            id, pipe.DesignFlowCfs, pipe.FullCapacityCfs)));
                }
                else if (pipe.FullCapacityCfs > 0.0
                         && pipe.DesignFlowCfs / pipe.FullCapacityCfs > c.MaxPctFull)
                {
                    findings.Add(DesignFinding.Warn(id,
                        string.Format(
                            "Pipe {0}: {1:0}% of full capacity (> {2:0}%)",
                            id,
                            100.0 * pipe.DesignFlowCfs / pipe.FullCapacityCfs,
                            100.0 * c.MaxPctFull)));
                }

                if (pipe.DesignFlowCfs > 0.0)
                {
                    if (pipe.VelocityFps < c.MinVelocityFps)
                    {
                        findings.Add(DesignFinding.Warn(id,
                            string.Format(
                                "Pipe {0}: velocity {1:0.0} ft/s below self-cleansing min {2:0.0} ft/s",
                                id, pipe.VelocityFps, c.MinVelocityFps)));
                    }
                    else if (pipe.VelocityFps > c.MaxVelocityFps)
                    {
                        findings.Add(DesignFinding.Warn(id,
                            string.Format(
                                "Pipe {0}: velocity {1:0.0} ft/s exceeds max {2:0.0} ft/s (scour)",
                                id, pipe.VelocityFps, c.MaxVelocityFps)));
                    }
                }

                CheckCoverAtEnd(findings, pipe, nodeMap, c, isUpstream: true);
                CheckCoverAtEnd(findings, pipe, nodeMap, c, isUpstream: false);
            }

            if (c.CheckSizeProgression)
                CheckSizeProgression(findings, pipes);

            foreach (KeyValuePair<string, ReviewNodeInput> pair in nodeMap)
            {
                ReviewNodeInput node = pair.Value;
                if (!node.HglFt.HasValue || !node.RimFt.HasValue)
                    continue;

                if (node.HglFt.Value > node.RimFt.Value)
                {
                    string nid = string.IsNullOrEmpty(node.Id) ? pair.Key : node.Id;
                    findings.Add(DesignFinding.Error(nid,
                        string.Format(
                            "Node {0}: HGL {1:0.00} ft surcharges above rim {2:0.00} ft (flooding)",
                            nid, node.HglFt.Value, node.RimFt.Value)));
                }
            }

            return findings;
        }

        private static void CheckCoverAtEnd(
            List<DesignFinding> findings,
            ReviewPipeInput pipe,
            IReadOnlyDictionary<string, ReviewNodeInput> nodes,
            ReviewCriteria c,
            bool isUpstream)
        {
            string nodeId = isUpstream ? pipe.UpstreamNodeId : pipe.DownstreamNodeId;
            if (string.IsNullOrEmpty(nodeId) || !nodes.TryGetValue(nodeId, out ReviewNodeInput? node))
                return;
            if (!node.RimFt.HasValue)
                return;

            double invert = node.InvertFt
                ?? (isUpstream ? pipe.UpstreamInvertFt : pipe.DownstreamInvertFt);
            double cover = node.RimFt.Value - (invert + pipe.DiameterFt);
            if (cover < c.MinCoverFt)
            {
                string end = isUpstream ? "upstream" : "downstream";
                findings.Add(DesignFinding.Warn(pipe.Id,
                    string.Format(
                        "Pipe {0}: {1:0.00} ft cover at {2} node {3} (< {4:0.00} ft min)",
                        pipe.Id, cover, end, nodeId, c.MinCoverFt)));
            }
        }

        private static void CheckSizeProgression(
            List<DesignFinding> findings,
            IReadOnlyList<ReviewPipeInput> pipes)
        {
            var outByNode = new Dictionary<string, List<ReviewPipeInput>>(StringComparer.OrdinalIgnoreCase);
            foreach (ReviewPipeInput pipe in pipes)
            {
                if (string.IsNullOrEmpty(pipe.UpstreamNodeId))
                    continue;
                if (!outByNode.TryGetValue(pipe.UpstreamNodeId, out List<ReviewPipeInput>? list))
                {
                    list = new List<ReviewPipeInput>();
                    outByNode[pipe.UpstreamNodeId] = list;
                }
                list.Add(pipe);
            }

            foreach (ReviewPipeInput upstream in pipes)
            {
                if (string.IsNullOrEmpty(upstream.DownstreamNodeId))
                    continue;
                if (!outByNode.TryGetValue(upstream.DownstreamNodeId, out List<ReviewPipeInput>? downs))
                    continue;

                foreach (ReviewPipeInput down in downs)
                {
                    if (down.DiameterFt + SizeToleranceFt < upstream.DiameterFt)
                    {
                        findings.Add(DesignFinding.Warn(down.Id,
                            string.Format(
                                "Pipe {0}: diameter {1:0.00} ft is smaller than upstream pipe {2} ({3:0.00} ft) at node {4}",
                                down.Id, down.DiameterFt, upstream.Id, upstream.DiameterFt,
                                upstream.DownstreamNodeId)));
                    }
                }
            }
        }
    }
}