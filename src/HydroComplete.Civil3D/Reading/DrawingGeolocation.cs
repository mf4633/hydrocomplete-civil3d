using System;
using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace HydroComplete.Civil3D.Reading
{
    /// <summary>
    /// Reads latitude/longitude from the active drawing's geo-reference data when set.
    /// Requires GEOGRAPHICLOCATION (or equivalent) in the DWG; returns null when absent.
    /// </summary>
    public static class DrawingGeolocation
    {
        public sealed class Result
        {
            public Result(double lat, double lon, string source)
            {
                Lat = lat;
                Lon = lon;
                Source = source;
            }

            public double Lat { get; }
            public double Lon { get; }
            /// <summary>GeoLocationData property used (ReferencePoint, GeoPosition, etc.).</summary>
            public string Source { get; }
        }

        public static Result? TryRead(Database db)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));

            ObjectId geoId;
            try
            {
                geoId = db.GeoDataObject;
            }
            catch
            {
                return null;
            }

            if (geoId.IsNull) return null;

            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                if (!(tr.GetObject(geoId, OpenMode.ForRead) is GeoLocationData geo))
                    return null;

                Result? fromRef = TryFromReferencePoint(geo);
                if (fromRef != null)
                {
                    tr.Commit();
                    return fromRef;
                }

                foreach (string markerProp in MarkerPropertyNames)
                {
                    Result? fromMarker = TryFromOptionalPoint(geo, markerProp, transformIfNeeded: true);
                    if (fromMarker != null)
                    {
                        tr.Commit();
                        return fromMarker;
                    }
                }

                Result? fromDesign = TryFromDesignPoint(geo);
                if (fromDesign != null)
                {
                    tr.Commit();
                    return fromDesign;
                }

                tr.Commit();
            }

            return null;
        }

        private static Result? TryFromReferencePoint(GeoLocationData geo)
        {
            try
            {
                // AutoCAD stores lon in X, lat in Y for geographic points.
                return TryLonLatPoint(geo.ReferencePoint, "ReferencePoint");
            }
            catch
            {
                return null;
            }
        }

        // GeoMarkerPosition is not on GeoLocationData in AcDbMgd 2026; probe via reflection for other releases.
        private static readonly string[] MarkerPropertyNames =
        {
            "GeoMarkerPosition",
            "GeoPosition",
        };

        private static Result? TryFromOptionalPoint(
            GeoLocationData geo, string propertyName, bool transformIfNeeded)
        {
            try
            {
                PropertyInfo? prop = typeof(GeoLocationData).GetProperty(
                    propertyName, BindingFlags.Instance | BindingFlags.Public);
                if (prop == null || prop.PropertyType != typeof(Point3d)) return null;

                var raw = (Point3d)prop.GetValue(geo)!;
                Result? asLonLat = TryLonLatPoint(raw, propertyName);
                if (asLonLat != null) return asLonLat;

                if (!transformIfNeeded) return null;
                Point3d transformed = geo.TransformToLonLatAlt(raw);
                return TryLonLatPoint(transformed, propertyName);
            }
            catch
            {
                return null;
            }
        }

        private static Result? TryFromDesignPoint(GeoLocationData geo)
        {
            try
            {
                Point3d design = geo.DesignPoint;
                Point3d lonLat = geo.TransformToLonLatAlt(design);
                return TryLonLatPoint(lonLat, "DesignPoint");
            }
            catch
            {
                return null;
            }
        }

        private static Result? TryLonLatPoint(Point3d lonLat, string source)
        {
            double lon = lonLat.X;
            double lat = lonLat.Y;
            if (!IsValidLatLon(lat, lon)) return null;
            return new Result(lat, lon, source);
        }

        private static bool IsValidLatLon(double lat, double lon)
        {
            if (double.IsNaN(lat) || double.IsNaN(lon) || double.IsInfinity(lat) || double.IsInfinity(lon))
                return false;
            if (lat < -90.0 || lat > 90.0) return false;
            if (lon < -180.0 || lon > 180.0) return false;
            if (Math.Abs(lat) < 0.0001 && Math.Abs(lon) < 0.0001) return false;
            return true;
        }
    }
}