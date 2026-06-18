using System;

namespace HydroComplete.Engine
{
    /// <summary>
    /// HEC-22 grate-on-grade inlet interception capacity (simplified composite gutter).
    /// Public-domain; FHWA HEC-22 storm drain inlet design.
    /// </summary>
    public static class InletCapacity
    {
        /// <summary>
        /// HEC-22 composite gutter coefficient for depressed grate (US customary).
        /// </summary>
        public const double CompositeGutterCw = 3.0;

        /// <summary>Check whether an inlet can capture the approach design flow.</summary>
        public sealed class InletCheck : TracedResult
        {
            public double DesignQCfs { get; set; }
            public double CapacityCfs { get; set; }
            public bool Ok { get; set; }
        }

        /// <summary>
        /// Grate-on-grade interception capacity (cfs).
        /// Q = Cw * L * d^1.5 * sqrt(S) with Cw ≈ 3.0.
        /// </summary>
        public static double GrateCapacityCfs(double grateLengthFt, double flowDepthFt, double gutterSlope)
        {
            if (grateLengthFt <= 0.0 || flowDepthFt <= 0.0 || gutterSlope <= 0.0)
                return 0.0;

            return CompositeGutterCw
                * grateLengthFt
                * Math.Pow(flowDepthFt, 1.5)
                * Math.Sqrt(gutterSlope);
        }

        /// <summary>
        /// Compare design approach flow to grate capacity and return a traced result.
        /// </summary>
        public static InletCheck CheckInlet(
            double designQCfs,
            double grateLengthFt,
            double flowDepthFt,
            double gutterSlope)
        {
            if (designQCfs < 0) throw new ArgumentOutOfRangeException(nameof(designQCfs));

            double cap = GrateCapacityCfs(grateLengthFt, flowDepthFt, gutterSlope);
            bool ok = cap >= designQCfs;

            var result = new InletCheck
            {
                DesignQCfs = designQCfs,
                CapacityCfs = cap,
                Ok = ok,
            };

            result.Steps.Add(new CalcStep("L", grateLengthFt, "ft", "grate length"));
            result.Steps.Add(new CalcStep("d", flowDepthFt, "ft", "gutter flow depth"));
            result.Steps.Add(new CalcStep("S", gutterSlope, "ft/ft", "gutter slope"));
            result.Steps.Add(new CalcStep("Cw", CompositeGutterCw, "-", "HEC-22 composite gutter"));
            result.Steps.Add(new CalcStep("Q_cap", cap, "cfs", "Cw*L*d^1.5*sqrt(S)"));
            result.Steps.Add(new CalcStep("Q_design", designQCfs, "cfs", "approach design flow"));
            result.Steps.Add(new CalcStep("ok", ok ? 1.0 : 0.0, "-", ok ? "Q_cap >= Q_design" : "Q_cap < Q_design"));
            return result;
        }
    }
}