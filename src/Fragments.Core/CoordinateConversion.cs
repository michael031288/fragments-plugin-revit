namespace Fragments.Core;

/// <summary>
/// Converts authoring-space (Revit / Navisworks / IFC) Z-up metres into the
/// That Open / Three.js viewer frame used by ZonePlanner:
/// <c>viewer = (srcX, srcZ, -srcY)</c>.
/// </summary>
/// <remarks>
/// Official IfcImporter files already arrive in this frame because web-ifc
/// bakes the same basis change into every vertex and placement. Native
/// exporters must do it themselves: Fragments renders stored geometry as-is
/// and only uses <see cref="FragmentsModelBuilder.Coordinates"/> for
/// COORDINATE_TO_ORIGIN alignment, not for up-axis.
/// </remarks>
public static class CoordinateConversion
{
    /// <summary>
    /// Per-axis distance (metres) beyond which geometry is recentered so
    /// float32 vertices and the Three.js scene stay near the origin.
    /// Matches ZonePlanner's <c>FAR_ORIGIN_THRESHOLD</c>.
    /// </summary>
    public const double FarOriginThreshold = 1000;

    public static Vec3 ToViewer(Vec3 point)
    {
        return new Vec3(point.X, point.Z, -point.Y);
    }

    public static Vec3 ToSource(Vec3 point)
    {
        return new Vec3(point.X, -point.Z, point.Y);
    }

    /// <summary>
    /// Maps a Z-up transform T to viewer-space T' = C T C⁻¹ so that
    /// <c>T' * ToViewer(p) = ToViewer(T * p)</c>.
    /// </summary>
    public static FragmentTransform ToViewer(FragmentTransform source)
    {
        source ??= FragmentTransform.Identity;

        var zx = (source.Xy * source.Yz) - (source.Xz * source.Yy);
        var zy = (source.Xz * source.Yx) - (source.Xx * source.Yz);
        var zz = (source.Xx * source.Yy) - (source.Xy * source.Yx);

        return new FragmentTransform
        {
            Px = source.Px,
            Py = source.Pz,
            Pz = -source.Py,
            Xx = source.Xx,
            Xy = source.Xz,
            Xz = -source.Xy,
            Yx = zx,
            Yy = zz,
            Yz = -zy
        };
    }

    public static OriginShift? ComputeOriginShift(double centerX, double centerY, double centerZ, double threshold = FarOriginThreshold)
    {
        var x = Math.Abs(centerX) > threshold ? Math.Round(centerX) : 0;
        var y = Math.Abs(centerY) > threshold ? Math.Round(centerY) : 0;
        var z = Math.Abs(centerZ) > threshold ? Math.Round(centerZ) : 0;
        if (x == 0 && y == 0 && z == 0)
        {
            return null;
        }

        return new OriginShift(x, y, z);
    }

    /// <summary>
    /// Translation that was applied to bring world geometry near the origin.
    /// ZonePlanner inverts <c>meshes.coordinates</c> to restore real-world
    /// eastings: <c>viewer = matrix * yUp(ifc)</c>.
    /// </summary>
    public static FragmentTransform AppliedTranslation(OriginShift shift)
    {
        return new FragmentTransform
        {
            Px = -shift.X,
            Py = -shift.Y,
            Pz = -shift.Z
        };
    }
}

public readonly struct OriginShift
{
    public OriginShift(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public double X { get; }
    public double Y { get; }
    public double Z { get; }
}
