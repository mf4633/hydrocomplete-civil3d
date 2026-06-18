using System;
using System.Collections.Generic;
using System.Linq;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Stage-storage relationship from elevation-area survey data using the
    /// average end area method: V = Σ [(A_i + A_{i+1}) / 2] × Δh.
    /// </summary>
    public static class StageStorage
    {
        public sealed class ElevationAreaPoint
        {
            /// <summary>Pond water-surface elevation, ft.</summary>
            public double ElevationFt { get; set; }

            /// <summary>Surface area at this elevation, ft².</summary>
            public double AreaFt2 { get; set; }
        }

        public sealed class StageStoragePoint
        {
            public double ElevationFt { get; set; }
            public double AreaFt2 { get; set; }

            /// <summary>Cumulative storage volume below this elevation, ft³.</summary>
            public double StorageFt3 { get; set; }
        }

        public sealed class StageStorageResult : TracedResult
        {
            public List<StageStoragePoint> Points { get; } = new List<StageStoragePoint>();

            /// <summary>Total storage at the top surveyed elevation, ft³.</summary>
            public double TotalStorageFt3 { get; set; }
        }

        /// <summary>
        /// Build a stage-storage table from an elevation-area contour table
        /// (sorted ascending by elevation).
        /// </summary>
        public static StageStorageResult BuildFromElevationArea(
            IReadOnlyList<ElevationAreaPoint> elevAreaTable)
        {
            if (elevAreaTable == null) throw new ArgumentNullException(nameof(elevAreaTable));
            if (elevAreaTable.Count < 2)
                throw new ArgumentException("At least two elevation-area points are required.", nameof(elevAreaTable));

            var sorted = elevAreaTable
                .OrderBy(p => p.ElevationFt)
                .ToList();

            var result = new StageStorageResult();
            double cumStorage = 0.0;

            result.Points.Add(new StageStoragePoint
            {
                ElevationFt = sorted[0].ElevationFt,
                AreaFt2 = sorted[0].AreaFt2,
                StorageFt3 = 0.0,
            });

            for (int i = 1; i < sorted.Count; i++)
            {
                double dh = sorted[i].ElevationFt - sorted[i - 1].ElevationFt;
                if (dh < 0)
                    throw new ArgumentException("Elevation-area table must be sorted ascending by elevation.");

                double avgArea = (sorted[i].AreaFt2 + sorted[i - 1].AreaFt2) / 2.0;
                double increment = AverageEndAreaVolume(sorted[i - 1].AreaFt2, sorted[i].AreaFt2, dh);
                cumStorage += increment;

                result.Points.Add(new StageStoragePoint
                {
                    ElevationFt = sorted[i].ElevationFt,
                    AreaFt2 = sorted[i].AreaFt2,
                    StorageFt3 = cumStorage,
                });

                result.Steps.Add(new CalcStep(
                    $"dV@{sorted[i].ElevationFt:0.##}",
                    increment,
                    "ft³",
                    $"({sorted[i - 1].AreaFt2:0.##}+{sorted[i].AreaFt2:0.##})/2×{dh:0.##}"));
            }

            result.TotalStorageFt3 = cumStorage;
            result.Steps.Add(new CalcStep("V_total", cumStorage, "ft³", "Σ average end area increments"));
            return result;
        }

        /// <summary>Average end area volume between two contours: V = (A1 + A2) / 2 × h.</summary>
        public static double AverageEndAreaVolume(double area1Ft2, double area2Ft2, double depthFt)
        {
            if (depthFt < 0) throw new ArgumentOutOfRangeException(nameof(depthFt));
            return ((area1Ft2 + area2Ft2) / 2.0) * depthFt;
        }

        /// <summary>Interpolate cumulative storage at an arbitrary elevation (ft).</summary>
        public static double InterpolateStorage(double elevationFt, IReadOnlyList<StageStoragePoint> table)
        {
            if (table == null || table.Count == 0) throw new ArgumentException("Stage-storage table is empty.");
            if (table.Count == 1) return elevationFt <= table[0].ElevationFt ? 0.0 : table[0].StorageFt3;

            if (elevationFt <= table[0].ElevationFt) return 0.0;

            var last = table[table.Count - 1];
            if (elevationFt >= last.ElevationFt)
            {
                // Linear extrapolation using top area
                return last.StorageFt3 + last.AreaFt2 * (elevationFt - last.ElevationFt);
            }

            for (int i = 1; i < table.Count; i++)
            {
                if (table[i].ElevationFt >= elevationFt)
                {
                    double e0 = table[i - 1].ElevationFt;
                    double e1 = table[i].ElevationFt;
                    double s0 = table[i - 1].StorageFt3;
                    double s1 = table[i].StorageFt3;
                    if (Math.Abs(e1 - e0) < 1e-12) return s1;
                    double f = (elevationFt - e0) / (e1 - e0);
                    return s0 + f * (s1 - s0);
                }
            }

            return last.StorageFt3;
        }

        /// <summary>Interpolate surface area at an arbitrary elevation (ft).</summary>
        public static double InterpolateArea(double elevationFt, IReadOnlyList<StageStoragePoint> table)
        {
            if (table == null || table.Count == 0) throw new ArgumentException("Stage-storage table is empty.");
            if (table.Count == 1) return table[0].AreaFt2;

            if (elevationFt <= table[0].ElevationFt) return table[0].AreaFt2;

            var last = table[table.Count - 1];
            if (elevationFt >= last.ElevationFt) return last.AreaFt2;

            for (int i = 1; i < table.Count; i++)
            {
                if (table[i].ElevationFt >= elevationFt)
                {
                    double e0 = table[i - 1].ElevationFt;
                    double e1 = table[i].ElevationFt;
                    double a0 = table[i - 1].AreaFt2;
                    double a1 = table[i].AreaFt2;
                    if (Math.Abs(e1 - e0) < 1e-12) return a1;
                    double f = (elevationFt - e0) / (e1 - e0);
                    return a0 + f * (a1 - a0);
                }
            }

            return last.AreaFt2;
        }

        /// <summary>Interpolate elevation for a target storage volume (ft³).</summary>
        public static double InterpolateElevation(double storageFt3, IReadOnlyList<StageStoragePoint> table)
        {
            if (table == null || table.Count == 0) throw new ArgumentException("Stage-storage table is empty.");
            if (storageFt3 <= 0) return table[0].ElevationFt;

            var last = table[table.Count - 1];
            if (storageFt3 >= last.StorageFt3)
            {
                if (last.AreaFt2 <= 0) return last.ElevationFt;
                return last.ElevationFt + (storageFt3 - last.StorageFt3) / last.AreaFt2;
            }

            for (int i = 1; i < table.Count; i++)
            {
                if (table[i].StorageFt3 >= storageFt3)
                {
                    double s0 = table[i - 1].StorageFt3;
                    double s1 = table[i].StorageFt3;
                    double e0 = table[i - 1].ElevationFt;
                    double e1 = table[i].ElevationFt;
                    if (Math.Abs(s1 - s0) < 1e-12) return e1;
                    double f = (storageFt3 - s0) / (s1 - s0);
                    return e0 + f * (e1 - e0);
                }
            }

            return last.ElevationFt;
        }
    }
}