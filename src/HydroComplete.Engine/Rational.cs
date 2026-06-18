using System;
using System.Collections.Generic;

namespace HydroComplete.Engine
{
    /// <summary>
    /// The Rational method:  Q = C * i * A
    /// with Q in cfs, i in in/hr, A in acres. The unit conversion factor is
    /// 1.008 (acre-in/hr -> cfs) and is conventionally taken as 1.0.
    /// </summary>
    public static class Rational
    {
        public sealed class PeakFlowResult : TracedResult
        {
            public double PeakFlowCfs { get; set; }
            /// <summary>Area-weighted composite runoff coefficient actually used.</summary>
            public double CompositeC { get; set; }
            public double TotalAreaAcres { get; set; }
            /// <summary>Intensity used, in/hr.</summary>
            public double IntensityInHr { get; set; }
        }

        /// <summary>
        /// Peak flow for one catchment using its Tc on the supplied IDF curve.
        /// </summary>
        public static PeakFlowResult Peak(Catchment catchment, IdfCurve idf)
        {
            if (catchment == null) throw new ArgumentNullException(nameof(catchment));
            if (idf == null) throw new ArgumentNullException(nameof(idf));

            var intensity = idf.Intensity(catchment.TcMinutes);
            var peak = Peak(catchment.RunoffC, intensity.IntensityInHr, catchment.AreaAcres);
            if (!string.IsNullOrEmpty(catchment.Name))
                peak.Steps.Insert(0, new CalcStep("catchment", 0, "", catchment.Name));
            foreach (CalcStep step in intensity.Steps)
                peak.Steps.Insert(1, step);
            return peak;
        }

        /// <summary>Single-area peak flow, Q = C i A.</summary>
        public static PeakFlowResult Peak(double runoffC, double intensityInHr, double areaAcres)
        {
            if (runoffC < 0 || runoffC > 1) throw new ArgumentOutOfRangeException(nameof(runoffC), "C must be 0..1.");
            if (intensityInHr < 0) throw new ArgumentOutOfRangeException(nameof(intensityInHr));
            if (areaAcres < 0) throw new ArgumentOutOfRangeException(nameof(areaAcres));

            double q = runoffC * intensityInHr * areaAcres;
            var r = new PeakFlowResult
            {
                PeakFlowCfs = q,
                CompositeC = runoffC,
                TotalAreaAcres = areaAcres,
                IntensityInHr = intensityInHr,
            };
            r.Steps.Add(new CalcStep("Q", q, "cfs", $"C*i*A = {runoffC:0.###}*{intensityInHr:0.###}*{areaAcres:0.###}"));
            return r;
        }

        /// <summary>
        /// Composite peak flow over several subareas using an area-weighted C.
        /// The caller supplies the design intensity (typically from the IDF curve
        /// at the system time of concentration).
        /// </summary>
        public static PeakFlowResult Peak(IEnumerable<Catchment> catchments, double intensityInHr)
        {
            if (catchments == null) throw new ArgumentNullException(nameof(catchments));
            if (intensityInHr < 0) throw new ArgumentOutOfRangeException(nameof(intensityInHr));

            double sumCA = 0.0, sumA = 0.0;
            foreach (var c in catchments)
            {
                if (c.RunoffC < 0 || c.RunoffC > 1) throw new ArgumentOutOfRangeException(nameof(catchments), $"C out of range for '{c.Name}'.");
                if (c.AreaAcres < 0) throw new ArgumentOutOfRangeException(nameof(catchments), $"Area negative for '{c.Name}'.");
                sumCA += c.RunoffC * c.AreaAcres;
                sumA += c.AreaAcres;
            }

            double compositeC = sumA > 0 ? sumCA / sumA : 0.0;
            double q = compositeC * intensityInHr * sumA; // == sumCA * i

            var r = new PeakFlowResult
            {
                PeakFlowCfs = q,
                CompositeC = compositeC,
                TotalAreaAcres = sumA,
                IntensityInHr = intensityInHr,
            };
            r.Steps.Add(new CalcStep("Sum(C*A)", sumCA, "acres", "area-weighted"));
            r.Steps.Add(new CalcStep("A_total", sumA, "acres", "sum of subareas"));
            r.Steps.Add(new CalcStep("C_composite", compositeC, "-", "Sum(C*A)/A_total"));
            r.Steps.Add(new CalcStep("Q", q, "cfs", "C_composite*i*A_total"));
            return r;
        }
    }
}
