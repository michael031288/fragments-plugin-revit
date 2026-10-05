namespace Tessera.Core;

/// <summary>
/// Document and object user-text keys written into every Tessera .3dm.
/// Rhino shows these in Object Properties and Document User Text.
/// </summary>
public static class TesseraKeys
{
    public const string Schema = "Tessera.Schema";
    public const string Units = "Tessera.Units";
    public const string UpAxis = "Tessera.UpAxis";
    public const string LayerMode = "Tessera.LayerMode";
    public const string Source = "Tessera.Source";
    public const string Coordinates = "Tessera.Coordinates";
    public const string ProjectName = "Tessera.ProjectName";
    public const string ViewName = "Tessera.ViewName";

    public const string IfcClass = "IfcClass";
    public const string IfcStorey = "IfcStorey";
    public const string StoreyElevation = "StoreyElevation";
    public const string RevitUniqueId = "RevitUniqueId";
    public const string RevitElementId = "RevitElementId";
    public const string RevitCategory = "RevitCategory";
    public const string TypeName = "TypeName";
    public const string Material = "Material";

    public const string SchemaVersion = "1";
    public const string UnitMeters = "meters";
    public const string UpAxisZ = "Z";
    public const string ApplicationName = "Tessera.Revit";
}
