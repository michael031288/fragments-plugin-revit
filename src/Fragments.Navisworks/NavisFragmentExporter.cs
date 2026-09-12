using System.IO;
using Autodesk.Navisworks.Api;
using Fragments.Core;

namespace Fragments.Navisworks;

public sealed class NavisExportSummary
{
    public int ElementCount { get; set; }
    public int TriangleCount { get; set; }
    public long FileBytes { get; set; }
}

public static class NavisFragmentExporter
{
    public static NavisExportSummary Export(Document document, IEnumerable<ModelItem> items, string filePath, bool compress = true)
    {
        var toMeters = UnitConversion.ScaleFactor(document.Units, Units.Meters);
        var builder = new FragmentsModelBuilder
        {
            ModelGuid = Guid.NewGuid().ToString(),
            Metadata = "{\"schema\":\"IFC4\",\"origin\":\"Navisworks\",\"application\":\"Fragments.Navisworks\",\"units\":\"meters\",\"upAxis\":\"Y\",\"source\":\"world\"}"
        };

        var projectId = builder.AddItem("IFCPROJECT", attributes: new[]
        {
            new FragmentAttribute("Name", document.Title ?? "Navisworks Model", "IFCLABEL")
        });

        var geometryCache = new Dictionary<string, (int Shell, int Material)>(StringComparer.Ordinal);
        var exported = new List<uint>();
        var triangleCount = 0;
        var itemLocalIds = new Dictionary<ModelItem, uint>();

        foreach (var item in items)
        {
            if (item == null || !item.HasGeometry)
            {
                continue;
            }

            var extracted = NavisGeometryExtractor.Extract(item, toMeters);
            if (extracted == null)
            {
                continue;
            }

            var (points, indices, color, transform) = extracted.Value;
            if (indices.Count < 3)
            {
                continue;
            }

            var key = NavisGeometryExtractor.FragmentKey(item);
            if (!geometryCache.TryGetValue(key, out var reused))
            {
                var shell = builder.AddTriangleShell(points, indices);
                var material = builder.AddMaterial(color[0], color[1], color[2], color[3]);
                reused = (shell, material);
                geometryCache[key] = reused;
            }

            var localId = builder.AddItem(
                NavisPropertyCollector.CategoryOf(item),
                NavisPropertyCollector.FindGuid(item),
                NavisPropertyCollector.Collect(item));
            builder.AddInstance(localId, reused.Shell, reused.Material, transform);
            itemLocalIds[item] = localId;
            exported.Add(localId);
            triangleCount += indices.Count / 3;
        }

        builder.AddRelation(projectId, "ContainsElements", exported);
        builder.SpatialStructure = BuildSpatial(projectId, exported);
        builder.RecenterFarFromOrigin();
        var bytes = builder.Build(compress);
        File.WriteAllBytes(filePath, bytes);

        return new NavisExportSummary
        {
            ElementCount = exported.Count,
            TriangleCount = triangleCount,
            FileBytes = bytes.LongLength
        };
    }

    private static FragmentSpatialNode BuildSpatial(uint projectId, List<uint> exported)
    {
        var root = FragmentSpatialNode.CategoryGroup("IFCPROJECT");
        var project = FragmentSpatialNode.Item(projectId);
        foreach (var id in exported)
        {
            project.Children.Add(FragmentSpatialNode.Item(id));
        }

        root.Children.Add(project);
        return root;
    }
}
