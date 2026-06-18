using System;

namespace HydroComplete.Engine
{
    /// <summary>
    /// HEC-22 inlet interception capacity (grate-on-grade, sag grate, curb opening).
    /// Public-domain; FHWA HEC-22 storm drain inlet design.
    /// </summary>
    public static class InletCapacity
    {
        /// <summary>Storm drain inlet geometry / placement category.</summary>
        public enum InletType
        {
            GrateOnGrade,
            Sag,
            CurbOpening,
        }

        /// <summary>
        /// HEC-22 composite gutter coefficient for grate-on-grade (US customary).
        /// </summary>
        public const double CompositeGutterCw = 3.0;

        /// <summary>
        /// HEC-22 weir coefficient for depressed grate in a sag (sump); Eq. 4-26, no slope term.
        /// </summary>
        public const double SagGrateCw = 3.27;

        /// <summary>
        /// HEC-22 curb-opening interception coefficient (US customary, simplified form).
        /// </summary>
        public const double CurbOpeningCw = 3.0;

        /// <summary>Check whether an inlet can capture the approach design flow.</summary>
        public sealed class InletCheck : TracedResult
        {
            public InletType InletType { get; set; }
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
        /// Sag (depressed) grate interception capacity (cfs).
        /// HEC-22 Eq. 4-26 weir form: Q = C * L * d^1.5 (no sqrt(S) in sump).
        /// </summary>
        public static double SagCapacityCfs(double grateLengthFt, double flowDepthFt)
        {
            if (grateLengthFt <= 0.0 || flowDepthFt <= 0.0)
                return 0.0;

            return SagGrateCw * grateLengthFt * Math.Pow(flowDepthFt, 1.5);
        }

        /// <summary>
        /// Curb-opening interception capacity (cfs).
        /// HEC-22 simplified: Q = C * a * L * d^1.5 * sqrt(S).
        /// </summary>
        public static double CurbOpeningCapacityCfs(
            double openingHeightFt,
            double lengthFt,
            double flowDepthFt,
            double gutterSlope)
        {
            if (openingHeightFt <= 0.0 || lengthFt <= 0.0 || flowDepthFt <= 0.0 || gutterSlope <= 0.0)
                return 0.0;

            return CurbOpeningCw
                * openingHeightFt
                * lengthFt
                * Math.Pow(flowDepthFt, 1.5)
                * Math.Sqrt(gutterSlope);
        }

        /// <summary>Capacity (cfs) for the given inlet type.</summary>
        public static double CapacityCfs(
            InletType inletType,
            double lengthFt,
            double flowDepthFt,
            double gutterSlope,
            double curbOpeningHeightFt = 0.0)
        {
            switch (inletType)
            {
                case InletType.GrateOnGrade:
                    return GrateCapacityCfs(lengthFt, flowDepthFt, gutterSlope);
                case InletType.Sag:
                    return SagCapacityCfs(lengthFt, flowDepthFt);
                case InletType.CurbOpening:
                    return CurbOpeningCapacityCfs(curbOpeningHeightFt, lengthFt, flowDepthFt, gutterSlope);
                default:
                    throw new ArgumentOutOfRangeException(nameof(inletType));
            }
        }

        /// <summary>
        /// Compare design approach flow to grate-on-grade capacity and return a traced result.
        /// </summary>
        public static InletCheck CheckInlet(
            double designQCfs,
            double grateLengthFt,
            double flowDepthFt,
            double gutterSlope)
        {
            return CheckInlet(designQCfs, InletType.GrateOnGrade, grateLengthFt, flowDepthFt, gutterSlope);
        }

        /// <summary>
        /// Compare design approach flow to inlet capacity and return a traced result.
        /// </summary>
        public static InletCheck CheckInlet(
            double designQCfs,
            InletType inletType,
            double lengthFt,
            double flowDepthFt,
            double gutterSlope,
            double curbOpeningHeightFt = 0.0)
        {
            if (designQCfs < 0) throw new ArgumentOutOfRangeException(nameof(designQCfs));

            double cap = CapacityCfs(inletType, lengthFt, flowDepthFt, gutterSlope, curbOpeningHeightFt);
            bool ok = cap >= designQCfs;

            var result = new InletCheck
            {
                InletType = inletType,
                DesignQCfs = designQCfs,
                CapacityCfs = cap,
                Ok = ok,
            };

            result.Steps.Add(new CalcStep("type", (double)inletType, "-", inletType.ToString()));
            result.Steps.Add(new CalcStep("L", lengthFt, "ft", "inlet length"));
            result.Steps.Add(new CalcStep("d", flowDepthFt, "ft", "gutter flow depth"));

            switch (inletType)
            {
                case InletType.GrateOnGrade:
                    result.Steps.Add(new CalcStep("S", gutterSlope, "ft/ft", "gutter slope"));
                    result.Steps.Add(new CalcStep("Cw", CompositeGutterCw, "-", "HEC-22 composite gutter"));
                    result.Steps.Add(new CalcStep("Q_cap", cap, "cfs", "Cw*L*d^1.5*sqrt(S)"));
                    break;
                case InletType.Sag:
                    result.Steps.Add(new CalcStep("Cw", SagGrateCw, "-", "HEC-22 sag grate weir"));
                    result.Steps.Add(new CalcStep("Q_cap", cap, "cfs", "Cw*L*d^1.5"));
                    break;
                case InletType.CurbOpening:
                    result.Steps.Add(new CalcStep("a", curbOpeningHeightFt, "ft", "curb opening height"));
                    result.Steps.Add(new CalcStep("S", gutterSlope, "ft/ft", "gutter slope"));
                    result.Steps.Add(new CalcStep("Cw", CurbOpeningCw, "-", "HEC-22 curb opening"));
                    result.Steps.Add(new CalcStep("Q_cap", cap, "cfs", "Cw*a*L*d^1.5*sqrt(S)"));
                    break;
            }

            result.Steps.Add(new CalcStep("Q_design", designQCfs, "cfs", "approach design flow"));
            result.Steps.Add(new CalcStep("ok", ok ? 1.0 : 0.0, "-", ok ? "Q_cap >= Q_design" : "Q_cap < Q_design"));
            return result;
        }
    }
}