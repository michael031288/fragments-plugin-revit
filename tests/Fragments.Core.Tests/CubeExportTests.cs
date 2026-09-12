using Fragments.Schema;
using Xunit;

namespace Fragments.Core.Tests;

public class CubeExportTests
{
    [Fact]
    public void UnitCube_RoundTripsUncompressed()
    {
        var bytes = CubeGeometry.CreateUnitCube().Build(compress: false);

        Assert.False(ZlibCodec.IsZlib(bytes));
        Assert.True(bytes.Length > 8);

        var model = FragmentsWriter.Read(bytes);
        AssertCube(model);
    }

    [Fact]
    public void UnitCube_RoundTripsCompressed()
    {
        var raw = CubeGeometry.CreateUnitCube().Build(compress: false);
        var compressed = CubeGeometry.CreateUnitCube().Build(compress: true);

        Assert.True(ZlibCodec.IsZlib(compressed));
        Assert.True(compressed.Length < raw.Length);

        var inflated = ZlibCodec.Decompress(compressed);
        CollectionAssertEqual(raw, inflated);

        var model = FragmentsWriter.Read(compressed);
        AssertCube(model);
    }

    [Fact]
    public void IdenticalMeshes_AreInstanced()
    {
        var builder = new FragmentsModelBuilder { ModelGuid = Guid.NewGuid().ToString() };
        var a = builder.AddItem("IFCWALL");
        var b = builder.AddItem("IFCWALL");
        var points = new[]
        {
            new Vec3(0, 0, 0),
            new Vec3(1, 0, 0),
            new Vec3(1, 1, 0)
        };
        var indices = new[] { 0, 1, 2 };
        var shellA = builder.AddTriangleShell(points, indices);
        var shellB = builder.AddTriangleShell(points, indices);
        Assert.Equal(shellA, shellB);

        var material = builder.AddMaterial(255, 0, 0);
        builder.AddInstance(a, shellA, material, new FragmentTransform { Px = 0 });
        builder.AddInstance(b, shellB, material, new FragmentTransform { Px = 4 });

        var model = FragmentsWriter.Read(builder.Build(false));
        Assert.Equal(1, model.Meshes!.Value.ShellsLength);
        Assert.Equal(2, model.Meshes!.Value.SamplesLength);
        Assert.Equal(2, model.Meshes!.Value.GlobalTransformsLength);
    }

    [Fact]
    public void Zlib_RejectsRawDeflateConfusion()
    {
        var raw = CubeGeometry.CreateUnitCube().Build(false);
        var compressed = ZlibCodec.Compress(raw);
        Assert.Equal(0x78, compressed[0]);
        Assert.Equal(0x9C, compressed[1]);

        var trailer = (uint)((compressed[compressed.Length - 4] << 24)
                             | (compressed[compressed.Length - 3] << 16)
                             | (compressed[compressed.Length - 2] << 8)
                             | compressed[compressed.Length - 1]);
        Assert.Equal(ZlibCodec.Adler32(raw), trailer);
    }

    private static void AssertCube(Model model)
    {
        Assert.Equal(5, model.LocalIdsLength);
        Assert.Equal("IFCPROJECT", model.Categories(0));
        Assert.Equal("IFCBUILDINGELEMENTPROXY", model.Categories(4));
        Assert.Equal(1, model.GuidsLength);
        Assert.Equal("cube-guid-0001", model.Guids(0));
        Assert.Equal((uint)5, model.GuidsItems(0));

        var meshes = model.Meshes!.Value;
        Assert.Equal(1, meshes.ShellsLength);
        Assert.Equal(1, meshes.SamplesLength);
        Assert.Equal(1, meshes.GlobalTransformsLength);
        Assert.Equal(8, meshes.Shells(0)!.Value.PointsLength);
        Assert.Equal(12, meshes.Shells(0)!.Value.ProfilesLength);
        Assert.Equal(12, meshes.Shells(0)!.Value.ProfilesFaceIdsLength);
        Assert.Equal(RepresentationClass.SHELL, meshes.Representations(0)!.Value.RepresentationClass);

        var attr = model.Attributes(4)!.Value.Data(0);
        Assert.Contains("Unit Cube", attr, StringComparison.Ordinal);

        Assert.NotNull(model.SpatialStructure);
        Assert.Equal("IFCPROJECT", model.SpatialStructure!.Value.Category);
    }

    private static void CollectionAssertEqual(byte[] expected, byte[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        Assert.Equal(expected, actual);
    }
}
