using System.Globalization;
using System.Text;

namespace Fragments.Core;

internal static class MeshHasher
{
    public static string HashTriangleMesh(IReadOnlyList<Vec3> points, IReadOnlyList<int> indices)
    {
        var sb = new StringBuilder(points.Count * 12 + indices.Count * 4);
        sb.Append(points.Count).Append('|').Append(indices.Count).Append('|');
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            sb.Append(Quantize(p.X)).Append(',')
                .Append(Quantize(p.Y)).Append(',')
                .Append(Quantize(p.Z)).Append(';');
        }

        sb.Append('#');
        for (var i = 0; i < indices.Count; i++)
        {
            sb.Append(indices[i]).Append(',');
        }

        return sb.ToString();
    }

    public static string HashMaterial(byte r, byte g, byte b, byte a, bool doubleSided)
    {
        return r + "," + g + "," + b + "," + a + "," + (doubleSided ? "2" : "1");
    }

    private static string Quantize(float value)
    {
        return Math.Round(value, 5).ToString("0.#####", CultureInfo.InvariantCulture);
    }
}
