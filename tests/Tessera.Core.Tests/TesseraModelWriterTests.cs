using System.Drawing;
using Rhino.FileIO;
using Rhino.Geometry;
using Xunit;

namespace Tessera.Core.Tests;

public class TesseraModelWriterTests
{
    [Fact]
    public void ClassAndStorey_RoundTripsMeshLayerColorAndUserText()
    {
        var model = SampleModel(TesseraLayerMode.ClassAndStorey);
        var path = Path.Combine(Path.GetTempPath(), "tessera-" + Guid.NewGuid().ToString("N") + ".3dm");
        try
        {
            var result = TesseraModelWriter.Write(model, path);

            Assert.Equal(2, result.ElementCount);
            Assert.Equal(2, result.ObjectCount);
            Assert.Equal(2, result.TriangleCount);
            Assert.Single(result.Files);

            using var file = File3dm.Read(path);
            Assert.NotNull(file);
            Assert.Equal(TesseraKeys.ApplicationName, file.ApplicationName);
            Assert.Equal(Rhino.UnitSystem.Meters, file.Settings.ModelUnitSystem);
            Assert.Equal(TesseraKeys.SchemaVersion, file.Strings.GetValue(TesseraKeys.Schema));
            Assert.Equal(TesseraKeys.UnitMeters, file.Strings.GetValue(TesseraKeys.Units));
            Assert.Equal("Z", file.Strings.GetValue(TesseraKeys.UpAxis));
            Assert.Equal("ClassAndStorey", file.Strings.GetValue(TesseraKeys.LayerMode));
            Assert.Equal("Tower", file.Strings.GetValue(TesseraKeys.ProjectName));

            var objects = ReadObjects(file);
            Assert.Equal(2, objects.Count);

            var wall = Assert.Single(objects, o => o.Name == "Wall 1");
            Assert.Equal("IfcWall", wall.IfcClass);
            Assert.Equal("Level 1", wall.Storey);
            Assert.Equal("Basic Wall", wall.TypeName);
            Assert.Equal("abc-123", wall.UniqueId);
            Assert.Equal("Concrete", wall.Material);
            Assert.Equal(Color.FromArgb(255, 10, 20, 30), wall.Color);

            var wallLayer = file.AllLayers.FindIndex(wall.LayerIndex);
            Assert.Equal("Level 1::IfcWall", wallLayer.FullPath.Replace("\\", "::"));
            var wallColor = TesseraClassPalette.ForClass("IfcWall");
            Assert.Equal(wallColor.R, wallLayer.Color.R);
            Assert.Equal(wallColor.G, wallLayer.Color.G);
            Assert.Equal(wallColor.B, wallLayer.Color.B);
            Assert.True(wall.DoublePrecision);
            Assert.Equal(123456.789, wall.X, 9);
            Assert.Equal(10, wall.Y, 9);
            Assert.Equal(3, wall.Z, 9);

            var windowLayer = file.AllLayers.FindIndex(objects.Single(o => o.Name == "Window 1").LayerIndex);
            Assert.Equal("Level 2::IfcWindow", windowLayer.FullPath.Replace("\\", "::"));
            Assert.Equal(TesseraClassPalette.ForClass("IfcWindow").R, windowLayer.Color.R);
            Assert.Equal(80, File3dm.ReadArchiveVersion(path));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void StoreyMode_UsesOneLayerPerStorey()
    {
        var model = SampleModel(TesseraLayerMode.Storey);
        var path = TempPath();
        try
        {
            TesseraModelWriter.Write(model, path);
            using var file = File3dm.Read(path);
            var names = ReadObjects(file).Select(o => file.AllLayers.FindIndex(o.LayerIndex).FullPath).OrderBy(n => n).ToArray();
            Assert.Equal(new[] { "Level 1", "Level 2" }, names);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SplitByClass_WritesOneFilePerClass()
    {
        var model = SampleModel(TesseraLayerMode.Class);
        var path = TempPath();
        try
        {
            var result = TesseraModelWriter.Write(model, path, TesseraSplitMode.ByClass);
            Assert.Equal(2, result.Files.Count);
            Assert.Contains(result.Files, file => file.EndsWith("IfcWall.3dm", StringComparison.Ordinal));
            Assert.Contains(result.Files, file => file.EndsWith("IfcWindow.3dm", StringComparison.Ordinal));
            Assert.False(File.Exists(path));

            using var wall = File3dm.Read(result.Files.Single(file => file.Contains("IfcWall")));
            var wallObjects = ReadObjects(wall);
            Assert.Single(wallObjects);
            Assert.Equal("Wall 1", wallObjects[0].Name);
        }
        finally
        {
            foreach (var file in Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "*"))
            {
                File.Delete(file);
            }
        }
    }

    [Fact]
    public void EmptyAndDegenerateMeshes_AreSkipped()
    {
        var model = new TesseraModel();
        model.Elements.Add(new TesseraElement
        {
            Name = "Empty",
            IfcClass = "IfcWall",
            Meshes = { new TesseraMesh() }
        });
        var degenerate = new TesseraMesh();
        degenerate.TriangleIndices.AddRange(new[] { 0, 0, 0 });
        degenerate.Positions.AddRange(new double[] { 0, 0, 0 });
        model.Elements.Add(new TesseraElement
        {
            Name = "Degenerate",
            IfcClass = "IfcWall",
            Meshes = { degenerate }
        });

        var path = TempPath();
        try
        {
            var result = TesseraModelWriter.Write(model, path);
            Assert.Equal(0, result.ElementCount);
            using var file = File3dm.Read(path);
            Assert.Empty(file.Objects);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("IfcWall", 230, 126, 34)]
    [InlineData("IfcWindow", 231, 76, 60)]
    [InlineData("IfcRoof", 192, 57, 163)]
    [InlineData("IfcStair", 39, 174, 96)]
    public void ClassColors_MatchTesseraPalette(string ifcClass, byte r, byte g, byte b)
    {
        var color = TesseraClassPalette.ForClass(ifcClass);
        Assert.Equal(r, color.R);
        Assert.Equal(g, color.G);
        Assert.Equal(b, color.B);
    }

    private static TesseraModel SampleModel(TesseraLayerMode mode)
    {
        var model = new TesseraModel { LayerMode = mode };
        model.DocumentStrings[TesseraKeys.ProjectName] = "Tower";
        model.Elements.Add(Element("Wall 1", "IfcWall", "Level 1", 123456.789, 10, 3, "Basic Wall", "abc-123", "Concrete", 10, 20, 30));
        model.Elements.Add(Element("Window 1", "IfcWindow", "Level 2", 1, 2, 3, "Fixed", "def-456", "Glass", 200, 220, 240));
        return model;
    }

    private static TesseraElement Element(
        string name,
        string ifcClass,
        string storey,
        double x,
        double y,
        double z,
        string typeName,
        string uniqueId,
        string material,
        byte r,
        byte g,
        byte b)
    {
        var mesh = new TesseraMesh { R = r, G = g, B = b, MaterialName = material };
        mesh.AddTriangle(x, y, z, x + 1, y, z, x, y + 1, z);
        var element = new TesseraElement
        {
            Name = name,
            IfcClass = ifcClass,
            Storey = storey
        };
        element.UserStrings[TesseraKeys.TypeName] = typeName;
        element.UserStrings[TesseraKeys.RevitUniqueId] = uniqueId;
        element.Meshes.Add(mesh);
        return element;
    }

    private static List<ObjectSnapshot> ReadObjects(File3dm file)
    {
        // File3dmObjectTable's LINQ cast yields nulls. Copy fields while foreach is live.
        var objects = new List<ObjectSnapshot>();
        foreach (var obj in file.Objects)
        {
            var mesh = Assert.IsType<Mesh>(obj.Geometry);
            var point = mesh.Vertices.Point3dAt(0);
            objects.Add(new ObjectSnapshot(
                obj.Name,
                obj.Attributes.GetUserString(TesseraKeys.IfcClass),
                obj.Attributes.GetUserString(TesseraKeys.IfcStorey),
                obj.Attributes.GetUserString(TesseraKeys.TypeName),
                obj.Attributes.GetUserString(TesseraKeys.RevitUniqueId),
                obj.Attributes.GetUserString(TesseraKeys.Material),
                obj.Attributes.ObjectColor,
                obj.Attributes.LayerIndex,
                point.X,
                point.Y,
                point.Z,
                mesh.Vertices.UseDoublePrecisionVertices));
        }

        return objects;
    }

    private sealed record ObjectSnapshot(
        string? Name,
        string? IfcClass,
        string? Storey,
        string? TypeName,
        string? UniqueId,
        string? Material,
        Color Color,
        int LayerIndex,
        double X,
        double Y,
        double Z,
        bool DoublePrecision);

    private static string TempPath()
    {
        return Path.Combine(Path.GetTempPath(), "tessera-" + Guid.NewGuid().ToString("N") + ".3dm");
    }
}
