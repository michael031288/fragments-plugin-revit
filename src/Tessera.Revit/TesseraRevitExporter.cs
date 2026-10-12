using System.Globalization;
using Autodesk.Revit.DB;
using Tessera.Core;

namespace Tessera.Revit;

/// <summary>Which Revit origin the exported geometry is measured from (Revit's IFC exporter calls this the coordinate base).</summary>
internal enum TesseraCoordinateBase
{
    /// <summary>Shared coordinates (the Survey Point): the real-world system every discipline's model shares.</summary>
    Shared,

    /// <summary>Project coordinates: measured from the Project Base Point, along project north.</summary>
    ProjectBasePoint,

    /// <summary>Revit's internal origin.</summary>
    Internal
}

internal sealed class TesseraExportSettings
{
    public int LevelOfDetail { get; init; } = 8;

    public TesseraCoordinateBase Coordinates { get; init; } = TesseraCoordinateBase.Shared;

    /// <summary>The map's coordinate reference system, e.g. "EPSG:27700". Empty: the model's own, if Revit has one.</summary>
    public string? Crs { get; init; }

    public static TesseraCoordinateBase ParseCoordinates(string? text)
    {
        return (text ?? "").Trim().ToLowerInvariant().Replace(" ", "").Replace("_", "") switch
        {
            "" or "shared" or "survey" or "surveypoint" or "sharedcoordinates" => TesseraCoordinateBase.Shared,
            "project" or "projectbasepoint" or "pbp" or "projectcoordinates" => TesseraCoordinateBase.ProjectBasePoint,
            "internal" or "internalorigin" or "internalcoordinates" => TesseraCoordinateBase.Internal,
            _ => throw new ArgumentException($"Unknown coordinates '{text}': use shared, project or internal.")
        };
    }

    public static string Describe(TesseraCoordinateBase coordinates) => coordinates switch
    {
        TesseraCoordinateBase.Shared => "shared coordinates",
        TesseraCoordinateBase.ProjectBasePoint => "project base point",
        _ => "internal origin"
    };
}

internal sealed class TesseraExportSummary
{
    public int ElementCount { get; init; }
    public int TriangleCount { get; init; }
    public long FileBytes { get; init; }
    public string File { get; init; } = "";

    /// <summary>Where the model was placed, in words, for logs and the result dialog.</summary>
    public string Placement { get; init; } = "";
}

internal static class TesseraRevitExporter
{
    public static double ToMeters(double feet)
    {
        return UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Meters);
    }

    public static TesseraExportSummary Export(Document document, View3D view, string filePath, TesseraExportSettings settings)
    {
        var shared = InternalToShared(document, out var sharedCheck);
        var toModel = InternalToModel(document, settings.Coordinates, shared);
        var model = new TsraModel
        {
            Generator = TesseraKeys.ApplicationName,
            Schema = "Revit",
            SourceName = document.Title ?? ""
        };
        var context = new TesseraExportContext(
            document,
            model,
            document.Title ?? "",
            toModel,
            settings.LevelOfDetail);
        using var exporter = new CustomExporter(document, context)
        {
            IncludeGeometricObjects = true,
            ShouldStopOnError = false
        };
        exporter.Export(view);

        model.AddTextProperty(context.Project, "Revit", TesseraKeys.ProjectName, document.Title);
        model.AddTextProperty(context.Project, "Revit", TesseraKeys.ViewName, view.Name);
        model.AddTextProperty(context.Project, "Revit", TesseraKeys.Coordinates, TesseraExportSettings.Describe(settings.Coordinates));
        model.AddTextProperty(context.Project, "Revit", TesseraKeys.Units, TesseraKeys.UnitMeters);
        model.AddTextProperty(context.Project, "Revit", TesseraKeys.UpAxis, TesseraKeys.UpAxisZ);
        var map = AddMapConversion(document, model, context.Site, shared, toModel, settings.Crs);
        model.Write(filePath);

        return new TesseraExportSummary
        {
            ElementCount = context.ElementCount,
            TriangleCount = model.TriangleCount,
            FileBytes = new FileInfo(filePath).Length,
            File = filePath,
            Placement = $"{TesseraExportSettings.Describe(settings.Coordinates)}; {map}; {sharedCheck}"
        };
    }

    /// <summary>
    /// Internal coordinates → shared coordinates. Revit's documentation does not say which way
    /// <see cref="ProjectLocation.GetTotalTransform"/> maps, so both directions are tried against the
    /// Project Base Point and Survey Point, whose shared positions Revit reports, and the one that
    /// puts them where Revit says they are is used.
    /// </summary>
    internal static Transform InternalToShared(Document document, out string check)
    {
        Transform total;
        try
        {
            total = document.ActiveProjectLocation?.GetTotalTransform() ?? Transform.Identity;
        }
        catch
        {
            check = "no project location: shared = internal";
            return Transform.Identity;
        }

        var known = new List<(XYZ Internal, XYZ Shared)>();
        foreach (var point in BasePoints(document))
        {
            try
            {
                known.Add((point.Position, point.SharedPosition));
            }
            catch
            {
                // A base point Revit cannot place.
            }
        }

        var inverse = total.Inverse;
        if (known.Count == 0)
        {
            check = "base points unavailable: assumed GetTotalTransform maps shared to internal";
            return inverse;
        }

        var inverseError = known.Sum(item => inverse.OfPoint(item.Internal).DistanceTo(item.Shared));
        var directError = known.Sum(item => total.OfPoint(item.Internal).DistanceTo(item.Shared));
        var chosen = inverseError <= directError ? inverse : total;
        var error = Math.Min(inverseError, directError);
        check = string.Format(
            CultureInfo.InvariantCulture,
            "shared transform checked on {0} base point(s), off by {1:0.###} mm",
            known.Count,
            ToMeters(error) * 1000);
        return chosen;
    }

    /// <summary>Internal coordinates → the coordinates the geometry is written in.</summary>
    internal static Transform InternalToModel(Document document, TesseraCoordinateBase coordinates, Transform internalToShared)
    {
        switch (coordinates)
        {
            case TesseraCoordinateBase.Shared:
                return internalToShared;
            case TesseraCoordinateBase.ProjectBasePoint:
                // Project coordinates: from the Project Base Point, axes along project north (the internal axes).
                var basePoint = BasePoint.GetProjectBasePoint(document);
                return Transform.CreateTranslation(basePoint == null ? XYZ.Zero : basePoint.Position.Negate());
            default:
                return Transform.Identity;
        }
    }

    private static IEnumerable<BasePoint> BasePoints(Document document)
    {
        BasePoint? project = null;
        BasePoint? survey = null;
        try
        {
            project = BasePoint.GetProjectBasePoint(document);
            survey = BasePoint.GetSurveyPoint(document);
        }
        catch
        {
            // Older or unusual documents.
        }

        if (project != null)
        {
            yield return project;
        }

        if (survey != null)
        {
            yield return survey;
        }
    }

    /// <summary>
    /// Records where the model sits in shared coordinates as IFC's map conversion, on the site, the way
    /// Tessera's compiler records an IfcMapConversion (<c>ePSet_MapConversion</c>, metres). Tessera's
    /// coordinate points, IFC export and mapping read it from there.
    /// E = x·a − y·b + Eastings, N = x·b + y·a + Northings, H = z + OrthogonalHeight.
    /// </summary>
    private static string AddMapConversion(Document document, TsraModel model, uint site, Transform internalToShared, Transform internalToModel, string? crs)
    {
        // Model → shared: undo the model's own placement, then go to shared.
        var modelToShared = internalToShared.Multiply(internalToModel.Inverse);
        var origin = modelToShared.Origin;
        var axis = modelToShared.BasisX;
        var length = Math.Sqrt(axis.X * axis.X + axis.Y * axis.Y);
        var a = length > 0 ? axis.X / length : 1;
        var b = length > 0 ? axis.Y / length : 0;
        var eastings = ToMeters(origin.X);
        var northings = ToMeters(origin.Y);
        var height = ToMeters(origin.Z);

        model.AddRealProperty(site, "ePSet_MapConversion", "Eastings", eastings, "IFCLENGTHMEASURE");
        model.AddRealProperty(site, "ePSet_MapConversion", "Northings", northings, "IFCLENGTHMEASURE");
        model.AddRealProperty(site, "ePSet_MapConversion", "OrthogonalHeight", height, "IFCLENGTHMEASURE");
        model.AddRealProperty(site, "ePSet_MapConversion", "XAxisAbscissa", a, "IFCREAL");
        model.AddRealProperty(site, "ePSet_MapConversion", "XAxisOrdinate", b, "IFCREAL");
        model.AddRealProperty(site, "ePSet_MapConversion", "Scale", 1.0, "IFCREAL");

        var crsName = string.IsNullOrWhiteSpace(crs) ? ModelCrs(document) : crs.Trim();
        if (!string.IsNullOrWhiteSpace(crsName))
        {
            model.AddTextProperty(site, "ePSet_ProjectedCRS", "Name", crsName);
        }

        var degrees = Math.Atan2(b, a) * 180 / Math.PI;
        return string.Format(
            CultureInfo.InvariantCulture,
            "model origin at E {0:0.000} N {1:0.000} H {2:0.000}, rotated {3:0.###}°{4}",
            eastings,
            northings,
            height,
            degrees,
            string.IsNullOrWhiteSpace(crsName) ? "" : $" ({crsName})");
    }

    /// <summary>The coordinate system Revit's location carries (set from Civil 3D or a geolocated link), if any.</summary>
    private static string? ModelCrs(Document document)
    {
        try
        {
            var id = document.SiteLocation?.GeoCoordinateSystemId;
            return string.IsNullOrWhiteSpace(id) ? null : id;
        }
        catch
        {
            return null;
        }
    }
}
