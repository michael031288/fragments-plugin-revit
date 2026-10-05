namespace Tessera.Core;

/// <summary>
/// How element meshes are grouped into Rhino layers.
/// Matches Tessera's class, storey, and combined organizations.
/// </summary>
public enum TesseraLayerMode
{
    Class,
    Storey,
    ClassAndStorey
}

/// <summary>
/// Writes one .3dm, or one file per storey or IFC class.
/// </summary>
public enum TesseraSplitMode
{
    None,
    ByStorey,
    ByClass
}
