using System.Globalization;
using Autodesk.Revit.DB;
using Tessera.Core;

namespace Tessera.Revit;

internal sealed class TesseraExportSettings
{
    public int LevelOfDetail { get; init; } = 8;
    public bool UseSharedCoordinates { get; init; }
}

internal sealed class TesseraExportSummary
{
    public int ElementCount { get; init; }
    public int TriangleCount { get; init; }
    public long FileBytes { get; init; }
    public string File { get; init; } = "";
}

internal static class TesseraRevitExporter
{
    public static double ToMeters(double feet)
    {
        return UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Meters);
    }

    public static TesseraExportSummary Export(Document document, View3D view, string filePath, TesseraExportSettings settings)
    {
        var shared = InternalToShared(document);
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
            settings.UseSharedCoordinates ? shared : Transform.Identity,
            settings.LevelOfDetail);
        using var exporter = new CustomExporter(document, context)
        {
            IncludeGeometricObjects = true,
            ShouldStopOnError = false
        };
        exporter.Export(view);

        model.AddTextProperty(context.Project, "Revit", TesseraKeys.ProjectName, document.Title);
        model.AddTextProperty(context.Project, "Revit", TesseraKeys.ViewName, view.Name);
        model.AddTextProperty(context.Project, "Revit", TesseraKeys.Coordinates, settings.UseSharedCoordinates ? "shared" : "project");
        model.AddTextProperty(context.Project, "Revit", TesseraKeys.Units, TesseraKeys.UnitMeters);
        model.AddTextProperty(context.Project, "Revit", TesseraKeys.UpAxis, TesseraKeys.UpAxisZ);
        AddSurvey(document, model, context.Project);
        model.Write(filePath);

        return new TesseraExportSummary
        {
            ElementCount = context.ElementCount,
            TriangleCount = model.TriangleCount,
            FileBytes = new FileInfo(filePath).Length,
            File = filePath
        };
    }

    private static Transform InternalToShared(Document document)
    {
        try
        {
            return document.ActiveProjectLocation?.GetTotalTransform().Inverse ?? Transform.Identity;
        }
        catch
        {
            return Transform.Identity;
        }
    }

    private static void AddSurvey(Document document, TsraModel model, uint project)
    {
        try
        {
            var position = document.ActiveProjectLocation?.GetProjectPosition(XYZ.Zero);
            if (position == null)
            {
                return;
            }

            model.AddRealProperty(project, "Survey", "Eastings", ToMeters(position.EastWest), "IFCLENGTHMEASURE");
            model.AddRealProperty(project, "Survey", "Northings", ToMeters(position.NorthSouth), "IFCLENGTHMEASURE");
            model.AddRealProperty(project, "Survey", "OrthogonalHeight", ToMeters(position.Elevation), "IFCLENGTHMEASURE");
            model.AddTextProperty(project, "Survey", "TrueNorthRadians", position.Angle.ToString("G17", CultureInfo.InvariantCulture));
        }
        catch
        {
            // Survey data is optional.
        }
    }
}
