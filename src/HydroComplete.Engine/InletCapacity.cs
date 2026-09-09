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
        /// HEC-22 orifice coefficient for a grate in a sag once it drowns; Eq. 4-27.
        /// </summary>
        public const double SagGrateOrificeCo = 0.67;

        /// <summary>
        /// Clear opening area as a fraction of the grate's plan area, used when the
        /// caller does not give one. Half is deliberately pessimistic: real grates
        /// run 0.5 to 0.8 depending on bar pattern, and guessing high on a safety
        /// check is the wrong way to be wrong.
        /// </summary>
        public const double DefaultClearOpeningRatio = 0.5;

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
        /// Weir-flow interception for a grate in a sag (cfs), HEC-22 Eq. 4-26:
        /// Q = Cw * P * d^1.5, with no slope term in a sump.
        ///
        /// P is the PERIMETER of the grate, not its length. For a grate against a
        /// curb the side along the curb does not take flow, so P = 2L + W; for a
        /// grate standing clear of the curb, P = 2(L + W). Passing a bare length
        /// understates a 4 ft by 4 ft grate by a factor of three.
        ///
        /// This is only the weir branch. A grate drowns as the water deepens and
        /// then behaves as an orifice, which governs at depth; use
        /// <see cref="SagGrateCapacityCfs"/> to get both and the lesser of them.
        /// </summary>
        public static double SagCapacityCfs(double perimeterFt, double flowDepthFt)
        {
            if (perimeterFt <= 0.0 || flowDepthFt <= 0.0)
                return 0.0;

            return SagGrateCw * perimeterFt * Math.Pow(flowDepthFt, 1.5);
        }

        /// <summary>Perimeter of a sag grate that takes flow, ft (HEC-22).</summary>
        /// <param name="againstCurb">
        /// True when one long side sits against the curb face and so intercepts
        /// nothing.
        /// </param>
        public static double SagGratePerimeterFt(
            double lengthFt, double widthFt, bool againstCurb = true)
        {
            if (lengthFt <= 0.0 || widthFt <= 0.0)
                return 0.0;

            return againstCurb
                ? (2.0 * lengthFt) + widthFt
                : 2.0 * (lengthFt + widthFt);
        }

        /// <summary>
        /// Orifice interception for a drowned grate in a sag (cfs), HEC-22 Eq. 4-27:
        /// Q = Co * Ag * sqrt(2 g d), with Ag the clear opening area.
        /// </summary>
        public static double SagOrificeCapacityCfs(double clearOpeningAreaFt2, double flowDepthFt)
        {
            if (clearOpeningAreaFt2 <= 0.0 || flowDepthFt <= 0.0)
                return 0.0;

            return SagGrateOrificeCo * clearOpeningAreaFt2 * Math.Sqrt(2.0 * 32.174 * flowDepthFt);
        }

        /// <summary>
        /// Interception for a grate in a sag (cfs), taking the lesser of the weir
        /// and orifice forms.
        ///
        /// HEC-22 puts weir flow below about 0.4 ft of ponding and orifice flow
        /// above about 1.4 ft, and calls the band between them indeterminate.
        /// Taking the smaller of the two throughout is the usual conservative
        /// reading and is continuous, so a design does not jump at the boundary.
        /// </summary>
        /// <param name="clearOpeningRatio">
        /// Clear opening area as a fraction of plan area; see
        /// <see cref="DefaultClearOpeningRatio"/>.
        /// </param>
        public static double SagGrateCapacityCfs(
            double lengthFt,
            double widthFt,
            double flowDepthFt,
            double clearOpeningRatio = DefaultClearOpeningRatio,
            bool againstCurb = true)
        {
            if (lengthFt <= 0.0 || widthFt <= 0.0 || flowDepthFt <= 0.0)
                return 0.0;

            if (clearOpeningRatio <= 0.0 || clearOpeningRatio > 1.0)
                throw new ArgumentOutOfRangeException(
                    nameof(clearOpeningRatio), "Clear opening ratio must be in (0, 1].");

            double weirCfs = SagCapacityCfs(
                SagGratePerimeterFt(lengthFt, widthFt, againstCurb), flowDepthFt);

            double orificeCfs = SagOrificeCapacityCfs(
                lengthFt * widthFt * clearOpeningRatio, flowDepthFt);

            return Math.Min(weirCfs, orificeCfs);
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
        /// <param name="grateWidthFt">
        /// Sag grates only. Given a width the check uses the real HEC-22 grate
        /// perimeter and the orifice limit. Left at zero it falls back to the
        /// weir form on the bare length, which understates a square grate about
        /// threefold: safe, but it will have you adding inlets you do not need.
        /// </param>
        public static double CapacityCfs(
            InletType inletType,
            double lengthFt,
            double flowDepthFt,
            double gutterSlope,
            double curbOpeningHeightFt = 0.0,
            double grateWidthFt = 0.0)
        {
            switch (inletType)
            {
                case InletType.GrateOnGrade:
                    return GrateCapacityCfs(lengthFt, flowDepthFt, gutterSlope);
                case InletType.Sag:
                    return grateWidthFt > 0.0
                        ? SagGrateCapacityCfs(lengthFt, grateWidthFt, flowDepthFt)
                        : SagCapacityCfs(lengthFt, flowDepthFt);
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
            double curbOpeningHeightFt = 0.0,
            double grateWidthFt = 0.0)
        {
            if (designQCfs < 0) throw new ArgumentOutOfRangeException(nameof(designQCfs));

            double cap = CapacityCfs(
                inletType, lengthFt, flowDepthFt, gutterSlope, curbOpeningHeightFt, grateWidthFt);
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
                    if (grateWidthFt > 0.0)
                    {
                        double perimeterFt = SagGratePerimeterFt(lengthFt, grateWidthFt);
                        result.Steps.Add(new CalcStep("W", grateWidthFt, "ft", "grate width"));
                        result.Steps.Add(new CalcStep(
                            "P", perimeterFt, "ft", "grate perimeter 2L+W, curb side excluded"));
                        result.Steps.Add(new CalcStep(
                            "Q_weir",
                            SagCapacityCfs(perimeterFt, flowDepthFt),
                            "cfs",
                            "Cw*P*d^1.5 (Eq. 4-26)"));
                        result.Steps.Add(new CalcStep(
                            "Q_orifice",
                            SagOrificeCapacityCfs(
                                lengthFt * grateWidthFt * DefaultClearOpeningRatio, flowDepthFt),
                            "cfs",
                            "Co*Ag*sqrt(2gd) (Eq. 4-27)"));
                        result.Steps.Add(new CalcStep("Q_cap", cap, "cfs", "lesser of weir and orifice"));
                    }
                    else
                    {
                        result.Steps.Add(new CalcStep(
                            "Q_cap", cap, "cfs",
                            "Cw*L*d^1.5 on the bare length: no grate width given, " +
                            "so this understates a square grate about threefold"));
                    }
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