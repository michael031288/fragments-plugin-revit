using System.Collections.Generic;
using System.Globalization;
using Autodesk.Revit.DB;
using Fragments.Core;

namespace Fragments.Revit;

/// <summary>
/// Maps Revit internal feet (Z-up, project north) into the shared / survey
/// frame (True North, real-world eastings) that ZonePlanner treats as IFC
/// project coordinates.
/// </summary>
internal static class RevitSharedCoordinates
{
    public static Transform InternalToShared(Document document)
    {
        try
        {
            var location = document.ActiveProjectLocation;
            if (location == null)
            {
                return Transform.Identity;
            }

            return location.GetTotalTransform().Inverse;
        }
        catch
        {
            return Transform.Identity;
        }
    }

    public static FragmentTransform ToViewerWorld(Transform internalTransform, Transform internalToShared)
    {
        var shared = internalToShared.Multiply(internalTransform);
        return CoordinateConversion.ToViewer(ToMeters(shared));
    }

    public static Vec3 ToViewerPoint(XYZ point)
    {
        return CoordinateConversion.ToViewer(new Vec3(
            (float)RevitFragmentExporter.ToMeters(point.X),
            (float)RevitFragmentExporter.ToMeters(point.Y),
            (float)RevitFragmentExporter.ToMeters(point.Z)));
    }

    public static double ElevationToSharedMeters(double internalElevation, Transform internalToShared)
    {
        var shared = internalToShared.OfPoint(new XYZ(0, 0, internalElevation));
        return RevitFragmentExporter.ToMeters(shared.Z);
    }

    public static string Metadata(Document document, Transform internalToShared)
    {
        var eastings = 0.0;
        var northings = 0.0;
        var elevation = 0.0;
        var angle = 0.0;
        try
        {
            var position = document.ActiveProjectLocation?.GetProjectPosition(XYZ.Zero);
            if (position != null)
            {
                eastings = RevitFragmentExporter.ToMeters(position.EastWest);
                northings = RevitFragmentExporter.ToMeters(position.NorthSouth);
                elevation = RevitFragmentExporter.ToMeters(position.Elevation);
                angle = position.Angle;
            }
        }
        catch
        {
            // Keep zeros when the project has no location.
        }

        var origin = internalToShared.OfPoint(XYZ.Zero);
        return string.Format(
            CultureInfo.InvariantCulture,
            "{{\"schema\":\"IFC4\",\"origin\":\"Revit\",\"application\":\"Fragments.Revit\",\"units\":\"meters\",\"upAxis\":\"Y\",\"source\":\"shared\",\"trueNorthRadians\":{0},\"eastings\":{1},\"northings\":{2},\"orthogonalHeight\":{3},\"internalOriginShared\":[{4},{5},{6}]}}",
            angle,
            eastings,
            northings,
            elevation,
            RevitFragmentExporter.ToMeters(origin.X),
            RevitFragmentExporter.ToMeters(origin.Y),
            RevitFragmentExporter.ToMeters(origin.Z));
    }

    public static IEnumerable<FragmentAttribute> SiteAttributes(Document document, Transform internalToShared)
    {
        var attributes = new List<FragmentAttribute>
        {
            new FragmentAttribute("Name", "Default Site", "IFCLABEL"),
            new FragmentAttribute("RefElevation", RevitFragmentExporter.ToMeters(internalToShared.OfPoint(XYZ.Zero).Z), "IFCREAL")
        };

        try
        {
            var position = document.ActiveProjectLocation?.GetProjectPosition(XYZ.Zero);
            if (position != null)
            {
                attributes.Add(new FragmentAttribute("Eastings", RevitFragmentExporter.ToMeters(position.EastWest), "IFCREAL"));
                attributes.Add(new FragmentAttribute("Northings", RevitFragmentExporter.ToMeters(position.NorthSouth), "IFCREAL"));
                attributes.Add(new FragmentAttribute("OrthogonalHeight", RevitFragmentExporter.ToMeters(position.Elevation), "IFCREAL"));
                attributes.Add(new FragmentAttribute("TrueNorth", position.Angle, "IFCREAL"));
            }
        }
        catch
        {
            // Optional georef attributes.
        }

        try
        {
            var site = document.SiteLocation;
            if (site != null)
            {
                attributes.Add(new FragmentAttribute("RefLatitude", site.Latitude * 180.0 / Math.PI, "IFCREAL"));
                attributes.Add(new FragmentAttribute("RefLongitude", site.Longitude * 180.0 / Math.PI, "IFCREAL"));
                if (!string.IsNullOrWhiteSpace(site.PlaceName))
                {
                    attributes.Add(new FragmentAttribute("PlaceName", site.PlaceName, "IFCLABEL"));
                }
            }
        }
        catch
        {
            // Optional geographic attributes.
        }

        return attributes;
    }

    private static FragmentTransform ToMeters(Transform transform)
    {
        return new FragmentTransform
        {
            Px = RevitFragmentExporter.ToMeters(transform.Origin.X),
            Py = RevitFragmentExporter.ToMeters(transform.Origin.Y),
            Pz = RevitFragmentExporter.ToMeters(transform.Origin.Z),
            Xx = (float)transform.BasisX.X,
            Xy = (float)transform.BasisX.Y,
            Xz = (float)transform.BasisX.Z,
            Yx = (float)transform.BasisY.X,
            Yy = (float)transform.BasisY.Y,
            Yz = (float)transform.BasisY.Z
        };
    }
}
