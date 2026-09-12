using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Fragments.Core;

namespace Fragments.Revit;

public sealed class ExportSummary
{
    public int ElementCount { get; set; }
    public int TriangleCount { get; set; }
    public long FileBytes { get; set; }
}

public static class RevitFragmentExporter
{
    public static ExportSummary Export(Document document, View3D view, string filePath, bool compress = true)
    {
        var builder = new FragmentsModelBuilder
        {
            ModelGuid = document.CreationGUID.ToString(),
            Metadata = "{\"schema\":\"IFC4\",\"origin\":\"Revit\",\"application\":\"Fragments.Revit\"}"
        };

        var projectId = builder.AddItem("IFCPROJECT", document.ProjectInformation?.UniqueId, new[]
        {
            new FragmentAttribute("Name", document.ProjectInformation?.Name ?? document.Title, "IFCLABEL"),
            new FragmentAttribute("Number", document.ProjectInformation?.Number ?? string.Empty, "IFCLABEL")
        });

        var siteId = builder.AddItem("IFCSITE", attributes: new[]
        {
            new FragmentAttribute("Name", "Default Site", "IFCLABEL")
        });

        var buildingId = builder.AddItem("IFCBUILDING", attributes: new[]
        {
            new FragmentAttribute("Name", document.ProjectInformation?.BuildingName ?? "Building", "IFCLABEL")
        });

        var levels = new FilteredElementCollector(document)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(l => l.Elevation)
            .ToList();

        var levelIds = new Dictionary<ElementId, uint>();
        foreach (var level in levels)
        {
            var localId = builder.AddItem("IFCBUILDINGSTOREY", level.UniqueId, new[]
            {
                new FragmentAttribute("Name", level.Name, "IFCLABEL"),
                new FragmentAttribute("Elevation", ToMeters(level.Elevation), "IFCREAL")
            });
            levelIds[level.Id] = localId;
        }

        var context = new RevitExportContext(document, builder, levelIds);
        var exporter = new CustomExporter(document, context)
        {
            IncludeGeometricObjects = true,
            ShouldStopOnError = false
        };
        exporter.Export(view);

        builder.AddRelation(projectId, "IsDecomposedBy", new[] { siteId });
        builder.AddRelation(siteId, "IsDecomposedBy", new[] { buildingId });
        if (levelIds.Count > 0)
        {
            builder.AddRelation(buildingId, "IsDecomposedBy", levelIds.Values.ToList());
        }

        builder.SpatialStructure = BuildSpatialTree(projectId, siteId, buildingId, levelIds, context.ElementLevels, context.ExportedLocalIds);

        var bytes = builder.Build(compress);
        File.WriteAllBytes(filePath, bytes);

        return new ExportSummary
        {
            ElementCount = context.ExportedLocalIds.Count,
            TriangleCount = context.TriangleCount,
            FileBytes = bytes.LongLength
        };
    }

    internal static double ToMeters(double feet)
    {
        return UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Meters);
    }

    private static FragmentSpatialNode BuildSpatialTree(
        uint projectId,
        uint siteId,
        uint buildingId,
        Dictionary<ElementId, uint> levelIds,
        Dictionary<uint, ElementId> elementLevels,
        IReadOnlyCollection<uint> exported)
    {
        var project = FragmentSpatialNode.CategoryGroup("IFCPROJECT");
        var projectItem = FragmentSpatialNode.Item(projectId);
        var siteGroup = FragmentSpatialNode.CategoryGroup("IFCSITE");
        var siteItem = FragmentSpatialNode.Item(siteId);
        var buildingGroup = FragmentSpatialNode.CategoryGroup("IFCBUILDING");
        var buildingItem = FragmentSpatialNode.Item(buildingId);

        var byLevel = new Dictionary<ElementId, List<uint>>();
        var unassigned = new List<uint>();
        foreach (var localId in exported)
        {
            if (elementLevels.TryGetValue(localId, out var levelId) && levelIds.ContainsKey(levelId))
            {
                if (!byLevel.TryGetValue(levelId, out var list))
                {
                    list = new List<uint>();
                    byLevel[levelId] = list;
                }

                list.Add(localId);
            }
            else
            {
                unassigned.Add(localId);
            }
        }

        foreach (var pair in levelIds)
        {
            var storeyGroup = FragmentSpatialNode.CategoryGroup("IFCBUILDINGSTOREY");
            var storeyItem = FragmentSpatialNode.Item(pair.Value);
            if (byLevel.TryGetValue(pair.Key, out var children))
            {
                foreach (var child in children)
                {
                    storeyItem.Children.Add(FragmentSpatialNode.Item(child));
                }
            }

            storeyGroup.Children.Add(storeyItem);
            buildingItem.Children.Add(storeyGroup);
        }

        foreach (var child in unassigned)
        {
            buildingItem.Children.Add(FragmentSpatialNode.Item(child));
        }

        buildingGroup.Children.Add(buildingItem);
        siteItem.Children.Add(buildingGroup);
        siteGroup.Children.Add(siteItem);
        projectItem.Children.Add(siteGroup);
        project.Children.Add(projectItem);
        return project;
    }
}
