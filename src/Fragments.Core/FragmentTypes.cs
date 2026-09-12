using System.Globalization;
using System.Text;

namespace Fragments.Core;

public readonly struct Vec3
{
    public Vec3(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public float X { get; }
    public float Y { get; }
    public float Z { get; }
}

public sealed class FragmentTransform
{
    public static FragmentTransform Identity { get; } = new FragmentTransform();

    public double Px { get; set; }
    public double Py { get; set; }
    public double Pz { get; set; }
    public float Xx { get; set; } = 1f;
    public float Xy { get; set; }
    public float Xz { get; set; }
    public float Yx { get; set; }
    public float Yy { get; set; } = 1f;
    public float Yz { get; set; }

    public FragmentTransform Clone()
    {
        return new FragmentTransform
        {
            Px = Px,
            Py = Py,
            Pz = Pz,
            Xx = Xx,
            Xy = Xy,
            Xz = Xz,
            Yx = Yx,
            Yy = Yy,
            Yz = Yz
        };
    }
}

public sealed class FragmentAttribute
{
    public FragmentAttribute(string name, object? value, string ifcType = "IFCLABEL")
    {
        Name = name;
        Value = value;
        IfcType = ifcType;
    }

    public string Name { get; }
    public object? Value { get; }
    public string IfcType { get; }
}

public sealed class FragmentRelation
{
    public FragmentRelation(string name, IReadOnlyList<uint> relatedLocalIds)
    {
        Name = name;
        RelatedLocalIds = relatedLocalIds;
    }

    public string Name { get; }
    public IReadOnlyList<uint> RelatedLocalIds { get; }
}

public sealed class FragmentSpatialNode
{
    public uint? LocalId { get; set; }
    public string? Category { get; set; }
    public List<FragmentSpatialNode> Children { get; } = new List<FragmentSpatialNode>();

    public static FragmentSpatialNode CategoryGroup(string category)
    {
        return new FragmentSpatialNode { Category = category };
    }

    public static FragmentSpatialNode Item(uint localId)
    {
        return new FragmentSpatialNode { LocalId = localId };
    }
}

public sealed class ExportProgress
{
    public ExportProgress(string stage, double fraction, string? detail = null)
    {
        Stage = stage;
        Fraction = fraction;
        Detail = detail;
    }

    public string Stage { get; }
    public double Fraction { get; }
    public string? Detail { get; }
}

internal static class JsonUtil
{
    public static string EncodeAttribute(string name, object? value, string ifcType)
    {
        return "[" + Quote(name) + "," + EncodeValue(value) + "," + Quote(ifcType) + "]";
    }

    public static string EncodeRelation(string name, IReadOnlyList<uint> ids)
    {
        var sb = new StringBuilder();
        sb.Append('[').Append(Quote(name));
        for (var i = 0; i < ids.Count; i++)
        {
            sb.Append(',').Append(ids[i].ToString(CultureInfo.InvariantCulture));
        }

        sb.Append(']');
        return sb.ToString();
    }

    private static string EncodeValue(object? value)
    {
        switch (value)
        {
            case null:
                return "null";
            case bool b:
                return b ? "true" : "false";
            case string s:
                return Quote(s);
            case byte or sbyte or short or ushort or int or uint or long or ulong:
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0";
            case float or double or decimal:
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0";
            default:
                return Quote(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
        }
    }

    private static string Quote(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    if (c < 32)
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }
}
