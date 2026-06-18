using System;
using System.Collections.Generic;
using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;

namespace HydroComplete.Civil3D.Reading
{
    /// <summary>How a catchment outlet structure was resolved.</summary>
    public enum CatchmentOutletSource
    {
        /// <summary>No outlet structure could be determined.</summary>
        None,

        /// <summary>Civil 3D catchment reference/outfall structure ObjectId.</summary>
        ReferenceStructureId,

        /// <summary>Nearest pipe-network structure by plan proximity (heuristic).</summary>
        NearestStructure,
    }

    /// <summary>Resolved catchment outlet structure for flow routing.</summary>
    public sealed class CatchmentOutletInfo
    {
        public CatchmentOutletSource Source { get; set; }

        /// <summary>Structure ObjectId handle string when available.</summary>
        public string? StructureId { get; set; }

        /// <summary>Structure name when resolved from the pipe network.</summary>
        public string? StructureName { get; set; }
    }

    /// <summary>
    /// Reads catchment outlet / reference structures from the Civil 3D API.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Civil 3D links catchments to pipe-network structures via a reference structure
    /// on the catchment object. Property names vary by release; this reader probes
    /// <c>ReferenceStructureId</c>, <c>OutfallStructureId</c>, and
    /// <c>ReferenceStructure</c> via reflection when direct members are absent.
    /// </para>
    /// <para>
    /// <b>Fallback:</b> When no API outlet is found, the catchment plan centroid is
    /// compared to structure insertion points from the active pipe network and the
    /// nearest structure is assigned. This is a heuristic — verify assignments on
    /// complex networks or assign outlets manually in Civil 3D.
    /// </para>
    /// </remarks>
    public static class CatchmentOutletReader
    {
        public static CatchmentOutletInfo TryReadOutlet(
            Autodesk.Civil.DatabaseServices.Catchment catchment,
            Transaction tr,
            IReadOnlyList<ReadPipe>? pipes = null,
            IReadOnlyDictionary<ObjectId, Point3d>? structurePositions = null)
        {
            if (catchment == null) throw new ArgumentNullException(nameof(catchment));
            if (tr == null) throw new ArgumentNullException(nameof(tr));

            var info = new CatchmentOutletInfo { Source = CatchmentOutletSource.None };

            if (TryReadReferenceStructureId(catchment, out ObjectId structId) && !structId.IsNull)
            {
                info.Source = CatchmentOutletSource.ReferenceStructureId;
                info.StructureId = structId.Handle.ToString();
                info.StructureName = ResolveStructureName(structId, tr, pipes);
                return info;
            }

            if (pipes != null && pipes.Count > 0)
            {
                CatchmentOutletInfo? nearest = TryNearestStructure(
                    catchment, tr, pipes, structurePositions);
                if (nearest != null)
                    return nearest;
            }

            return info;
        }

        private static bool TryReadReferenceStructureId(
            Autodesk.Civil.DatabaseServices.Catchment catchment,
            out ObjectId structureId)
        {
            structureId = ObjectId.Null;

            try
            {
                var prop = catchment.GetType().GetProperty(
                    "ReferenceStructureId",
                    BindingFlags.Instance | BindingFlags.Public);
                if (prop != null && prop.PropertyType == typeof(ObjectId))
                {
                    structureId = (ObjectId)prop.GetValue(catchment)!;
                    if (!structureId.IsNull) return true;
                }
            }
            catch
            {
                // API surface differs by Civil 3D version.
            }

            try
            {
                var prop = catchment.GetType().GetProperty(
                    "OutfallStructureId",
                    BindingFlags.Instance | BindingFlags.Public);
                if (prop != null && prop.PropertyType == typeof(ObjectId))
                {
                    structureId = (ObjectId)prop.GetValue(catchment)!;
                    if (!structureId.IsNull) return true;
                }
            }
            catch
            {
            }

            try
            {
                var prop = catchment.GetType().GetProperty(
                    "ReferenceStructure",
                    BindingFlags.Instance | BindingFlags.Public);
                if (prop != null && prop.PropertyType == typeof(ObjectId))
                {
                    structureId = (ObjectId)prop.GetValue(catchment)!;
                    if (!structureId.IsNull) return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static CatchmentOutletInfo? TryNearestStructure(
            Autodesk.Civil.DatabaseServices.Catchment catchment,
            Transaction tr,
            IReadOnlyList<ReadPipe> pipes,
            IReadOnlyDictionary<ObjectId, Point3d>? structurePositions)
        {
            if (!TryCatchmentCentroid(catchment, out Point3d centroid))
                return null;

            var positions = structurePositions ?? BuildStructurePositions(pipes, tr);
            if (positions.Count == 0) return null;

            ObjectId nearestId = ObjectId.Null;
            double bestDist = double.MaxValue;

            foreach (var pair in positions)
            {
                double dx = pair.Value.X - centroid.X;
                double dy = pair.Value.Y - centroid.Y;
                double dist = dx * dx + dy * dy;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    nearestId = pair.Key;
                }
            }

            if (nearestId.IsNull) return null;

            return new CatchmentOutletInfo
            {
                Source = CatchmentOutletSource.NearestStructure,
                StructureId = nearestId.Handle.ToString(),
                StructureName = ResolveStructureName(nearestId, tr, pipes),
            };
        }

        private static bool TryCatchmentCentroid(
            Autodesk.Civil.DatabaseServices.Catchment catchment,
            out Point3d centroid)
        {
            centroid = Point3d.Origin;

            try
            {
                var prop = catchment.GetType().GetProperty(
                    "Centroid",
                    BindingFlags.Instance | BindingFlags.Public);
                if (prop != null && prop.PropertyType == typeof(Point3d))
                {
                    centroid = (Point3d)prop.GetValue(catchment)!;
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                var method = catchment.GetType().GetMethod(
                    "GetCatchmentOutline",
                    BindingFlags.Instance | BindingFlags.Public);
                if (method != null)
                {
                    object? outline = method.Invoke(catchment, null);
                    if (outline is Point3dCollection pts && pts.Count > 0)
                    {
                        double sx = 0.0, sy = 0.0;
                        foreach (Point3d pt in pts)
                        {
                            sx += pt.X;
                            sy += pt.Y;
                        }
                        centroid = new Point3d(sx / pts.Count, sy / pts.Count, 0.0);
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static Dictionary<ObjectId, Point3d> BuildStructurePositions(
            IReadOnlyList<ReadPipe> pipes,
            Transaction tr)
        {
            var positions = new Dictionary<ObjectId, Point3d>();
            foreach (ReadPipe rp in pipes)
            {
                AddStructurePosition(positions, rp.UpstreamStructureId, tr);
                AddStructurePosition(positions, rp.DownstreamStructureId, tr);
            }

            return positions;
        }

        private static void AddStructurePosition(
            Dictionary<ObjectId, Point3d> positions,
            ObjectId structureId,
            Transaction tr)
        {
            if (structureId.IsNull || positions.ContainsKey(structureId)) return;
            if (!(tr.GetObject(structureId, OpenMode.ForRead) is Structure structure)) return;

            try
            {
                positions[structureId] = structure.Position;
            }
            catch
            {
                try
                {
                    positions[structureId] = structure.Location;
                }
                catch
                {
                }
            }
        }

        private static string ResolveStructureName(
            ObjectId structureId,
            Transaction tr,
            IReadOnlyList<ReadPipe>? pipes)
        {
            if (pipes != null)
            {
                foreach (ReadPipe rp in pipes)
                {
                    if (rp.UpstreamStructureId == structureId && !string.IsNullOrEmpty(rp.StartStructureName))
                        return rp.UpstreamStructureId == rp.StartStructureId
                            ? rp.StartStructureName
                            : rp.EndStructureName;
                    if (rp.DownstreamStructureId == structureId && !string.IsNullOrEmpty(rp.EndStructureName))
                        return rp.DownstreamStructureId == rp.EndStructureId
                            ? rp.EndStructureName
                            : rp.StartStructureName;
                }
            }

            if (structureId.IsNull) return "";
            if (tr.GetObject(structureId, OpenMode.ForRead) is Structure structure)
                return structure.Name ?? "";
            return "";
        }
    }
}