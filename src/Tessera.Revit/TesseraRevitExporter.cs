using System.Globalization;
using Autodesk.Revit.DB;
using Tessera.Core;

namespace Tessera.Revit;

internal sealed class TesseraExportSettings
{
    public TesseraLayerMode LayerMode { get; init; } = TesseraLayerMode.ClassAndStorey;
    public TesseraSplitMode Split { get; init; }
    public int LevelOfDetail { get; init; } = 8;
    public bool UseSharedCoordinates { get; init; }
}

internal sealed class TesseraExportSummary
{
    public int ElementCount { get; init; }
    public int ObjectCount { get; init; }
    public int TriangleCount { get; init; }
    public long FileBytes { get; init; }
    public IReadOnlyList<string> Files { get; init; } = Array.Empty<string>();
}

internal static class TesseraRevitExporter
{
    public static double ToMeters(double feet)
    {
        return UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Meters);
    }

    public static TesseraExportSummary Export(Document document, View3D view, string filePath, TesseraExportSettings settings)
    {
        RhinoNative.EnsureLoaded();
        var shared = InternalToShared(document);
        var model = new TesseraModel { LayerMode = settings.LayerMode };
        model.DocumentStrings[TesseraKeys.ProjectName] = document.Title ?? string.Empty;
        model.DocumentStrings[TesseraKeys.ViewName] = view.Name ?? string.Empty;
        model.DocumentStrings[TesseraKeys.Coordinates] = settings.UseSharedCoordinates ? "shared" : "project";
        AddSurveyStrings(document, model.DocumentStrings);

        var context = new TesseraExportContext(
            document,
            model,
            settings.UseSharedCoordinates ? shared : Transform.Identity,
            settings.LevelOfDetail);
        using var exporter = new CustomExporter(document, context)
        {
            IncludeGeometricObjects = true,
            ShouldStopOnError = false
        };
        exporter.Export(view);

        var written = TesseraModelWriter.Write(model, filePath, settings.Split);
        long bytes = 0;
        foreach (var file in written.Files)
        {
            bytes += new FileInfo(file).Length;
        }

        return new TesseraExportSummary
        {
            ElementCount = written.ElementCount,
            ObjectCount = written.ObjectCount,
            TriangleCount = context.TriangleCount,
            FileBytes = bytes,
            Files = written.Files
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

    private static void AddSurveyStrings(Document document, IDictionary<string, string> strings)
    {
        try
        {
            var position = document.ActiveProjectLocation?.GetProjectPosition(XYZ.Zero);
            if (position != null)
            {
                strings["Tessera.Eastings"] = ToMeters(position.EastWest).ToString("G17", CultureInfo.InvariantCulture);
                strings["Tessera.Northings"] = ToMeters(position.NorthSouth).ToString("G17", CultureInfo.InvariantCulture);
                strings["Tessera.OrthogonalHeight"] = ToMeters(position.Elevation).ToString("G17", CultureInfo.InvariantCulture);
                strings["Tessera.TrueNorthRadians"] = position.Angle.ToString("G17", CultureInfo.InvariantCulture);
            }
        }
        catch
        {
            // Survey data is optional.
        }
    }
}
