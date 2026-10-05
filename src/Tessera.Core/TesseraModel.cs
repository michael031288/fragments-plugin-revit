namespace Tessera.Core;

public sealed class TesseraMesh
{
    public byte R { get; set; } = 180;
    public byte G { get; set; } = 180;
    public byte B { get; set; } = 180;
    public byte A { get; set; } = 255;
    public string? MaterialName { get; set; }
    public List<double> Positions { get; } = new List<double>();
    public List<int> TriangleIndices { get; } = new List<int>();

    public int TriangleCount => TriangleIndices.Count / 3;

    public void AddTriangle(double x0, double y0, double z0, double x1, double y1, double z1, double x2, double y2, double z2)
    {
        var start = Positions.Count / 3;
        Positions.Add(x0);
        Positions.Add(y0);
        Positions.Add(z0);
        Positions.Add(x1);
        Positions.Add(y1);
        Positions.Add(z1);
        Positions.Add(x2);
        Positions.Add(y2);
        Positions.Add(z2);
        TriangleIndices.Add(start);
        TriangleIndices.Add(start + 1);
        TriangleIndices.Add(start + 2);
    }
}

public sealed class TesseraElement
{
    public string Name { get; set; } = "Element";
    public string IfcClass { get; set; } = "IfcBuildingElementProxy";
    public string Storey { get; set; } = "Unassigned";
    public Dictionary<string, string> UserStrings { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
    public List<TesseraMesh> Meshes { get; } = new List<TesseraMesh>();

    public int TriangleCount
    {
        get
        {
            var count = 0;
            foreach (var mesh in Meshes)
            {
                count += mesh.TriangleCount;
            }

            return count;
        }
    }
}

/// <summary>
/// In-memory Tessera model. Coordinates are Z-up metres, the same frame Rhino uses.
/// </summary>
public sealed class TesseraModel
{
    public TesseraLayerMode LayerMode { get; set; } = TesseraLayerMode.ClassAndStorey;
    public Dictionary<string, string> DocumentStrings { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
    public List<TesseraElement> Elements { get; } = new List<TesseraElement>();
}
