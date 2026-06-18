using System;

namespace HydroComplete.Engine
{
    /// <summary>
    /// Plan-view pipe direction and deflection helpers (no CAD types).
    /// Used by <c>NetworkTopology</c> when mapping Civil 3D pipe endpoints.
    /// </summary>
    public static class PipePlanGeometry
    {
        /// <summary>
        /// Unit flow direction from upstream XY to downstream XY.
        /// Returns (1, 0) when length is negligible.
        /// </summary>
        public static (double X, double Y) FlowDirection(
            double upstreamX, double upstreamY,
            double downstreamX, double downstreamY)
        {
            double dx = downstreamX - upstreamX;
            double dy = downstreamY - upstreamY;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 1e-9)
                return (1.0, 0.0);

            return (dx / length, dy / length);
        }

        /// <summary>
        /// Plan-view deflection between consecutive pipe directions (0° = straight-through).
        /// </summary>
        public static double DeflectionDegrees(
            double inDirX, double inDirY,
            double outDirX, double outDirY)
        {
            double dot = inDirX * outDirX + inDirY * outDirY;
            dot = Math.Max(-1.0, Math.Min(1.0, dot));
            return Math.Acos(dot) * 180.0 / Math.PI;
        }
    }
}