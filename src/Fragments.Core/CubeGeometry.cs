namespace Fragments.Core;

public static class CubeGeometry
{
    public static FragmentsModelBuilder CreateUnitCube(string? modelGuid = null)
    {
        var builder = new FragmentsModelBuilder
        {
            ModelGuid = modelGuid ?? "00000000-0000-0000-0000-000000000001",
            Metadata = "{\"schema\":\"IFC4\",\"origin\":\"Fragments.Core.CubeGeometry\"}"
        };

        var projectId = builder.AddItem("IFCPROJECT", attributes: new[]
        {
            new FragmentAttribute("Name", "Cube Project", "IFCLABEL")
        });
        var siteId = builder.AddItem("IFCSITE", attributes: new[]
        {
            new FragmentAttribute("Name", "Default Site", "IFCLABEL")
        });
        var buildingId = builder.AddItem("IFCBUILDING", attributes: new[]
        {
            new FragmentAttribute("Name", "Default Building", "IFCLABEL")
        });
        var storeyId = builder.AddItem("IFCBUILDINGSTOREY", attributes: new[]
        {
            new FragmentAttribute("Name", "Level 0", "IFCLABEL"),
            new FragmentAttribute("Elevation", 0.0, "IFCREAL")
        });

        var cubeId = builder.AddItem(
            "IFCBUILDINGELEMENTPROXY",
            "cube-guid-0001",
            new[]
            {
                new FragmentAttribute("Name", "Unit Cube", "IFCLABEL"),
                new FragmentAttribute("Description", "1m test cube", "IFCTEXT")
            });

        builder.AddRelation(projectId, "IsDecomposedBy", new[] { siteId });
        builder.AddRelation(siteId, "Decomposes", new[] { projectId });
        builder.AddRelation(siteId, "IsDecomposedBy", new[] { buildingId });
        builder.AddRelation(buildingId, "Decomposes", new[] { siteId });
        builder.AddRelation(buildingId, "IsDecomposedBy", new[] { storeyId });
        builder.AddRelation(storeyId, "Decomposes", new[] { buildingId });
        builder.AddRelation(storeyId, "ContainsElements", new[] { cubeId });
        builder.AddRelation(cubeId, "ContainedInStructure", new[] { storeyId });

        var points = new[]
        {
            new Vec3(0, 0, 0),
            new Vec3(1, 0, 0),
            new Vec3(1, 1, 0),
            new Vec3(0, 1, 0),
            new Vec3(0, 0, 1),
            new Vec3(1, 0, 1),
            new Vec3(1, 1, 1),
            new Vec3(0, 1, 1)
        };

        var indices = new[]
        {
            0, 2, 1, 0, 3, 2,
            4, 5, 6, 4, 6, 7,
            0, 1, 5, 0, 5, 4,
            1, 2, 6, 1, 6, 5,
            2, 3, 7, 2, 7, 6,
            3, 0, 4, 3, 4, 7
        };

        var faceIds = new ushort[] { 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5 };
        var shell = builder.AddTriangleShell(points, indices, faceIds);
        var material = builder.AddMaterial(180, 180, 190, 255);
        builder.AddInstance(cubeId, shell, material, FragmentTransform.Identity);

        var storeyNode = FragmentSpatialNode.CategoryGroup("IFCBUILDINGSTOREY");
        storeyNode.Children.Add(FragmentSpatialNode.Item(storeyId));
        storeyNode.Children[0].Children.Add(FragmentSpatialNode.Item(cubeId));

        var buildingNode = FragmentSpatialNode.CategoryGroup("IFCBUILDING");
        buildingNode.Children.Add(FragmentSpatialNode.Item(buildingId));
        buildingNode.Children[0].Children.Add(storeyNode);

        var siteNode = FragmentSpatialNode.CategoryGroup("IFCSITE");
        siteNode.Children.Add(FragmentSpatialNode.Item(siteId));
        siteNode.Children[0].Children.Add(buildingNode);

        var projectNode = FragmentSpatialNode.CategoryGroup("IFCPROJECT");
        projectNode.Children.Add(FragmentSpatialNode.Item(projectId));
        projectNode.Children[0].Children.Add(siteNode);
        builder.SpatialStructure = projectNode;

        return builder;
    }
}
