using System.Collections.Generic;

namespace HydroComplete.Engine
{
    /// <summary>
    /// One line of a calculation trace: the quantity, its value, units, and the
    /// expression that produced it. The plugin surfaces these so a reviewer can
    /// follow every number — the "formula transparency" promise.
    /// </summary>
    public sealed class CalcStep
    {
        public CalcStep(string label, double value, string units, string formula)
        {
            Label = label;
            Value = value;
            Units = units;
            Formula = formula;
        }

        public string Label { get; }
        public double Value { get; }
        public string Units { get; }
        public string Formula { get; }

        public override string ToString() => $"{Label} = {Value:0.####} {Units}   [{Formula}]";
    }

    /// <summary>Common result shape: a primary value plus its full calc trace.</summary>
    public class TracedResult
    {
        public TracedResult()
        {
            Steps = new List<CalcStep>();
        }

        public List<CalcStep> Steps { get; }
    }

    /// <summary>A single drainage area contributing runoff (Rational method input).</summary>
    public sealed class Catchment
    {
        /// <summary>Optional label (e.g. the Civil 3D catchment name).</summary>
        public string Name { get; set; } = "";

        /// <summary>Drainage area, acres.</summary>
        public double AreaAcres { get; set; }

        /// <summary>Runoff coefficient C (dimensionless, 0..1).</summary>
        public double RunoffC { get; set; }

        /// <summary>Time of concentration, minutes (drives the design intensity).</summary>
        public double TcMinutes { get; set; }
    }

    /// <summary>A circular gravity pipe segment for Manning capacity / normal-depth checks.</summary>
    public sealed class PipeSegment
    {
        public string Name { get; set; } = "";

        /// <summary>Inside diameter, feet.</summary>
        public double DiameterFt { get; set; }

        /// <summary>Slope, ft/ft (rise over run, positive downstream).</summary>
        public double Slope { get; set; }

        /// <summary>Manning's roughness n (dimensionless).</summary>
        public double ManningN { get; set; } = 0.013;

        /// <summary>Design (peak) flow the pipe must carry, cfs. Optional.</summary>
        public double DesignFlowCfs { get; set; }

        /// <summary>Pipe length, ft (plan or center-to-center per source drawing).</summary>
        public double LengthFt { get; set; }

        /// <summary>Invert elevation at the upstream end, ft.</summary>
        public double StartInvertFt { get; set; }

        /// <summary>Invert elevation at the downstream end, ft.</summary>
        public double EndInvertFt { get; set; }
    }

    /// <summary>A single reach in a steady HGL network profile.</summary>
    public sealed class NetworkReach
    {
        public string Name { get; set; } = "";

        /// <summary>Reach length, ft.</summary>
        public double LengthFt { get; set; }

        /// <summary>Manning's roughness n (dimensionless).</summary>
        public double ManningN { get; set; } = 0.013;

        /// <summary>Flow cross-sectional area, ft².</summary>
        public double AreaFt2 { get; set; }

        /// <summary>Hydraulic radius, ft.</summary>
        public double HydRadiusFt { get; set; }

        /// <summary>Discharge, cfs.</summary>
        public double FlowCfs { get; set; }

        /// <summary>Upstream velocity head, ft (V²/2g).</summary>
        public double VelHeadUpFt { get; set; }

        /// <summary>Downstream velocity head, ft (V²/2g).</summary>
        public double VelHeadDownFt { get; set; }

        /// <summary>
        /// Junction/minor loss K at the downstream structure (HEC-22 h_m = K*Vh).
        /// Zero skips minor loss at this node.
        /// </summary>
        public double JunctionLossK { get; set; }

        /// <summary>Entrance loss K at the upstream end of this reach (0 = none).</summary>
        public double EntranceLossK { get; set; }

        /// <summary>Exit loss K at the downstream end (1.0 = full velocity-head dissipation).</summary>
        public double ExitLossK { get; set; }
    }

    /// <summary>Options for steady HGL profile stepping.</summary>
    public sealed class HglProfileOptions
    {
        /// <summary>Apply HEC-22 junction losses at internal manholes.</summary>
        public bool IncludeJunctionLosses { get; set; }

        /// <summary>Apply entrance loss at the headwater pipe (K*Vh).</summary>
        public bool IncludeEntranceLoss { get; set; }

        /// <summary>Apply exit loss at the outfall pipe (typically K=1).</summary>
        public bool IncludeExitLoss { get; set; }

        public double ManholeLossK { get; set; } = Hec22.DefaultManholeK;
        public double EntranceLossK { get; set; } = Hec22.DefaultEntranceK;
        public double ExitLossK { get; set; } = Hec22.DefaultExitK;
    }

    /// <summary>One point on a steady network HGL/EGL profile (downstream end of a reach).</summary>
    public sealed class HglProfilePoint : TracedResult
    {
        public int ReachIndex { get; set; }

        /// <summary>Cumulative distance from profile start, ft.</summary>
        public double CumLengthFt { get; set; }

        /// <summary>Hydraulic grade line elevation, ft.</summary>
        public double HglFt { get; set; }

        /// <summary>Energy grade line elevation, ft.</summary>
        public double EglFt { get; set; }

        /// <summary>Friction head loss over this reach, ft.</summary>
        public double HfFt { get; set; }

        /// <summary>Energy grade line drop over this reach, ft.</summary>
        public double DeltaEglFt { get; set; }

        /// <summary>Sum of minor (junction/entrance/exit) losses over this reach, ft.</summary>
        public double HmFt { get; set; }

        /// <summary>Velocity head at the downstream end of this reach, ft.</summary>
        public double VelocityHeadFt { get; set; }
    }
}
