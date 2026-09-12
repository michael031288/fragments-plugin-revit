using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.ComApi;
using Autodesk.Navisworks.Api.Interop.ComApi;
using Fragments.Core;

namespace Fragments.Navisworks;

internal sealed class PrimitiveCallback : InwSimplePrimitivesCB
{
    public List<Vec3> Points { get; } = new List<Vec3>();
    public List<int> Indices { get; } = new List<int>();

    public void Triangle(InwSimpleVertex v1, InwSimpleVertex v2, InwSimpleVertex v3)
    {
        Add(v1);
        Add(v2);
        Add(v3);
    }

    public void Line(InwSimpleVertex v1, InwSimpleVertex v2)
    {
    }

    public void Point(InwSimpleVertex v1)
    {
    }

    public void SnapPoint(InwSimpleVertex v1)
    {
    }

    private void Add(InwSimpleVertex vertex)
    {
        var coords = (Array)vertex.coord;
        var x = Convert.ToSingle(coords.GetValue(1));
        var y = Convert.ToSingle(coords.GetValue(2));
        var z = Convert.ToSingle(coords.GetValue(3));
        var index = Points.Count;
        Points.Add(new Vec3(x, y, z));
        Indices.Add(index);
    }
}

internal static class NavisGeometryExtractor
{
    public static (List<Vec3> Points, List<int> Indices, byte[] Color, FragmentTransform Transform)? Extract(ModelItem item, double toMeters)
    {
        var oState = ComApiBridge.State;
        var selection = new ModelItemCollection { item };
        var comSelection = ComApiBridge.ToInwOpSelection(selection);

        PrimitiveCallback? first = null;
        FragmentTransform transform = FragmentTransform.Identity;
        byte[] color = { 180, 180, 180, 255 };

        foreach (InwOaPath path in comSelection.Paths())
        {
            foreach (InwOaFragment3 fragment in path.Fragments())
            {
                var listener = new PrimitiveCallback();
                fragment.GenerateSimplePrimitives(nwEVertexProperty.eNORMAL, listener);
                if (listener.Indices.Count < 3)
                {
                    continue;
                }

                first = listener;
                transform = ToTransform(fragment, toMeters);
                color = ReadColor(fragment);
                break;
            }

            if (first != null)
            {
                break;
            }
        }

        if (first == null)
        {
            return null;
        }

        var points = new List<Vec3>(first.Points.Count);
        foreach (var point in first.Points)
        {
            points.Add(new Vec3(
                (float)(point.X * toMeters),
                (float)(point.Y * toMeters),
                (float)(point.Z * toMeters)));
        }

        return (points, first.Indices, color, transform);
    }

    public static string FragmentKey(ModelItem item)
    {
        try
        {
            var selection = new ModelItemCollection { item };
            var comSelection = ComApiBridge.ToInwOpSelection(selection);
            foreach (InwOaPath path in comSelection.Paths())
            {
                foreach (InwOaFragment3 fragment in path.Fragments())
                {
                    var data = (Array)fragment.path.ArrayData;
                    var parts = new int[data.Length];
                    var i = 0;
                    foreach (var value in data)
                    {
                        parts[i++] = Convert.ToInt32(value);
                    }

                    return string.Join(".", parts);
                }
            }
        }
        catch
        {
            // Fall back to a unique key so export still works.
        }

        return "item:" + item.InstanceGuid;
    }

    private static FragmentTransform ToTransform(InwOaFragment3 fragment, double toMeters)
    {
        try
        {
            var matrix = (Array)fragment.GetLocalToWorldMatrix();
            var xx = Convert.ToSingle(matrix.GetValue(1));
            var xy = Convert.ToSingle(matrix.GetValue(2));
            var xz = Convert.ToSingle(matrix.GetValue(3));
            var yx = Convert.ToSingle(matrix.GetValue(5));
            var yy = Convert.ToSingle(matrix.GetValue(6));
            var yz = Convert.ToSingle(matrix.GetValue(7));
            var px = Convert.ToDouble(matrix.GetValue(13)) * toMeters;
            var py = Convert.ToDouble(matrix.GetValue(14)) * toMeters;
            var pz = Convert.ToDouble(matrix.GetValue(15)) * toMeters;
            return new FragmentTransform
            {
                Px = px,
                Py = py,
                Pz = pz,
                Xx = xx,
                Xy = xy,
                Xz = xz,
                Yx = yx,
                Yy = yy,
                Yz = yz
            };
        }
        catch
        {
            return FragmentTransform.Identity;
        }
    }

    private static byte[] ReadColor(InwOaFragment3 fragment)
    {
        try
        {
            var appearance = fragment.Appearance as Array;
            if (appearance == null || appearance.Length < 6)
            {
                return new byte[] { 180, 180, 180, 255 };
            }

            var r = (byte)Math.Max(0, Math.Min(255, Convert.ToDouble(appearance.GetValue(4)) * 255.0));
            var g = (byte)Math.Max(0, Math.Min(255, Convert.ToDouble(appearance.GetValue(5)) * 255.0));
            var b = (byte)Math.Max(0, Math.Min(255, Convert.ToDouble(appearance.GetValue(6)) * 255.0));
            return new[] { r, g, b, (byte)255 };
        }
        catch
        {
            return new byte[] { 180, 180, 180, 255 };
        }
    }
}
