using Fragments.Schema;
using Xunit;

namespace Fragments.Core.Tests;

public class CoordinateConversionTests
{
    [Fact]
    public void ToViewer_MapsZUpPointOntoThreeJsYUp()
    {
        var viewer = CoordinateConversion.ToViewer(new Vec3(1, 2, 3));
        Assert.Equal(1f, viewer.X);
        Assert.Equal(3f, viewer.Y);
        Assert.Equal(-2f, viewer.Z);
        var source = CoordinateConversion.ToSource(viewer);
        Assert.Equal(1f, source.X);
        Assert.Equal(2f, source.Y);
        Assert.Equal(3f, source.Z);
    }

    [Fact]
    public void ToViewer_KeepsIdentityTransformIdentity()
    {
        var viewer = CoordinateConversion.ToViewer(FragmentTransform.Identity);
        Assert.Equal(0, viewer.Px);
        Assert.Equal(0, viewer.Py);
        Assert.Equal(0, viewer.Pz);
        Assert.Equal(1f, viewer.Xx);
        Assert.Equal(0f, viewer.Xy);
        Assert.Equal(0f, viewer.Xz);
        Assert.Equal(0f, viewer.Yx);
        Assert.Equal(1f, viewer.Yy);
        Assert.Equal(0f, viewer.Yz);
    }

    [Fact]
    public void ToViewer_SendsSourceUpToViewerUp()
    {
        var source = new FragmentTransform { Pz = 10 };
        var viewer = CoordinateConversion.ToViewer(source);
        Assert.Equal(0, viewer.Px);
        Assert.Equal(10, viewer.Py);
        Assert.Equal(0, viewer.Pz);
    }

    [Fact]
    public void ToViewer_PreservesTransformedPoints()
    {
        var source = new FragmentTransform
        {
            Px = 10,
            Py = 20,
            Pz = 30,
            Xx = 0,
            Xy = 1,
            Xz = 0,
            Yx = -1,
            Yy = 0,
            Yz = 0
        };
        var local = new Vec3(1, 0, 2);
        var worldSource = Apply(source, local);
        var worldViewer = Apply(CoordinateConversion.ToViewer(source), CoordinateConversion.ToViewer(local));
        var expected = CoordinateConversion.ToViewer(worldSource);
        Assert.Equal(expected.X, worldViewer.X, 3);
        Assert.Equal(expected.Y, worldViewer.Y, 3);
        Assert.Equal(expected.Z, worldViewer.Z, 3);
    }

    [Fact]
    public void RecenterFarFromOrigin_MovesWorldPlacementsAndRecordsAppliedTranslation()
    {
        var builder = new FragmentsModelBuilder { ModelGuid = Guid.NewGuid().ToString() };
        var item = builder.AddItem("IFCWALL");
        var shell = builder.AddTriangleShell(
            new[] { new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(0, 1, 0) },
            new[] { 0, 1, 2 });
        var material = builder.AddMaterial(255, 0, 0);
        builder.AddInstance(item, shell, material, new FragmentTransform { Px = 530_000, Py = 12, Pz = -180_000 });

        var shift = builder.RecenterFarFromOrigin();
        Assert.NotNull(shift);
        Assert.Equal(530000, shift!.Value.X);
        Assert.Equal(0, shift.Value.Y);
        Assert.Equal(-180000, shift.Value.Z);
        Assert.Equal(-530000, builder.Coordinates.Px);
        Assert.Equal(0, builder.Coordinates.Py);
        Assert.Equal(180000, builder.Coordinates.Pz);

        var model = FragmentsWriter.Read(builder.Build(false));
        var placed = model.Meshes!.Value.GlobalTransforms(0)!.Value.Position;
        Assert.Equal(0, placed.X);
        Assert.Equal(12, placed.Y);
        Assert.Equal(0, placed.Z);
    }

    [Fact]
    public void RecenterFarFromOrigin_MovesWorldSpaceVerticesWhenPlacementsSitAtOrigin()
    {
        var builder = new FragmentsModelBuilder { ModelGuid = Guid.NewGuid().ToString() };
        var item = builder.AddItem("IFCSLAB");
        var shell = builder.AddTriangleShell(
            new[] { new Vec3(4000, 5, -2000), new Vec3(4010, 5, -2000), new Vec3(4000, 5, -1990) },
            new[] { 0, 1, 2 });
        var material = builder.AddMaterial(0, 255, 0);
        builder.AddInstance(item, shell, material, FragmentTransform.Identity);

        var shift = builder.RecenterFarFromOrigin();
        Assert.NotNull(shift);
        Assert.Equal(4005, shift!.Value.X);
        Assert.Equal(0, shift.Value.Y);
        Assert.Equal(-1995, shift.Value.Z);
        Assert.Equal(-4005, builder.Coordinates.Px);
        Assert.Equal(1995, builder.Coordinates.Pz);

        var model = FragmentsWriter.Read(builder.Build(false));
        var point = model.Meshes!.Value.Shells(0)!.Value.Points(0)!.Value;
        Assert.Equal(-5f, point.X, 3);
        Assert.Equal(5f, point.Y, 3);
        Assert.Equal(-5f, point.Z, 3);
        Assert.Equal(0, model.Meshes.Value.GlobalTransforms(0)!.Value.Position.X);
    }

    [Fact]
    public void RecenterFarFromOrigin_RoundTripsThroughWriter()
    {
        var builder = new FragmentsModelBuilder { ModelGuid = Guid.NewGuid().ToString() };
        var item = builder.AddItem("IFCWALL");
        var shell = builder.AddTriangleShell(
            new[] { new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(0, 1, 0) },
            new[] { 0, 1, 2 });
        builder.AddInstance(item, shell, builder.AddMaterial(1, 2, 3), new FragmentTransform { Px = 2500, Py = 4, Pz = 0 });
        builder.RecenterFarFromOrigin();

        var model = FragmentsWriter.Read(builder.Build(false));
        var coordinates = model.Meshes!.Value.Coordinates!.Value;
        Assert.Equal(-2500, coordinates.Position.X);
        Assert.Equal(0, coordinates.Position.Y);
        Assert.Equal(0, coordinates.Position.Z);
        Assert.Equal(1f, coordinates.XDirection.X);
        Assert.Equal(1f, coordinates.YDirection.Y);
        Assert.Equal(0, model.Meshes.Value.GlobalTransforms(0)!.Value.Position.X);
        Assert.Equal(4, model.Meshes.Value.GlobalTransforms(0)!.Value.Position.Y);
    }

    private static Vec3 Apply(FragmentTransform transform, Vec3 point)
    {
        var zx = (transform.Xy * transform.Yz) - (transform.Xz * transform.Yy);
        var zy = (transform.Xz * transform.Yx) - (transform.Xx * transform.Yz);
        var zz = (transform.Xx * transform.Yy) - (transform.Xy * transform.Yx);
        return new Vec3(
            (float)(transform.Xx * point.X + transform.Yx * point.Y + zx * point.Z + transform.Px),
            (float)(transform.Xy * point.X + transform.Yy * point.Y + zy * point.Z + transform.Py),
            (float)(transform.Xz * point.X + transform.Yz * point.Y + zz * point.Z + transform.Pz));
    }
}
