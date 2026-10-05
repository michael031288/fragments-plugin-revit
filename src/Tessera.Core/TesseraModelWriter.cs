using System.Drawing;
using System.Globalization;
using Rhino;
using Rhino.DocObjects;
using Rhino.FileIO;
using Rhino.Geometry;

namespace Tessera.Core;

public sealed class TesseraWriteResult
{
    public int ElementCount { get; init; }
    public int ObjectCount { get; init; }
    public int TriangleCount { get; init; }
    public IReadOnlyList<string> Files { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Writes a Rhino 8 .3dm that opens with Tessera's layer colors, hierarchy, and user text.
/// </summary>
public static class TesseraModelWriter
{
    public const int FileVersion = 8;

    public static TesseraWriteResult Write(TesseraModel model, string filePath, TesseraSplitMode split = TesseraSplitMode.None)
    {
        if (model == null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("A .3dm path is required.", nameof(filePath));
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (string.IsNullOrEmpty(directory))
        {
            throw new ArgumentException("The .3dm path has no directory.", nameof(filePath));
        }

        Directory.CreateDirectory(directory);
        var groups = Group(model, split);
        var files = new List<string>(groups.Count);
        var objects = 0;
        var triangles = 0;
        var elements = 0;
        var baseName = Path.GetFileNameWithoutExtension(filePath);
        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var path = groups.Count == 1 && split == TesseraSplitMode.None
                ? Path.GetFullPath(filePath)
                : UniquePath(directory, baseName, TesseraText.FileToken(group.Key, "Part"), usedPaths);
            usedPaths.Add(path);

            var written = WriteOne(model, group.Value, path);
            files.Add(path);
            objects += written.ObjectCount;
            triangles += written.TriangleCount;
            elements += written.ElementCount;
        }

        return new TesseraWriteResult
        {
            ElementCount = elements,
            ObjectCount = objects,
            TriangleCount = triangles,
            Files = files
        };
    }

    private static List<KeyValuePair<string, List<TesseraElement>>> Group(TesseraModel model, TesseraSplitMode split)
    {
        if (split == TesseraSplitMode.None)
        {
            return new List<KeyValuePair<string, List<TesseraElement>>>
            {
                new KeyValuePair<string, List<TesseraElement>>("model", model.Elements)
            };
        }

        var groups = new Dictionary<string, List<TesseraElement>>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in model.Elements)
        {
            var key = split == TesseraSplitMode.ByClass
                ? TesseraText.LayerName(element.IfcClass, "IfcBuildingElementProxy")
                : TesseraText.LayerName(element.Storey, "Unassigned");
            if (!groups.TryGetValue(key, out var list))
            {
                list = new List<TesseraElement>();
                groups[key] = list;
            }

            list.Add(element);
        }

        if (groups.Count == 0)
        {
            groups["Empty"] = new List<TesseraElement>();
        }

        return groups.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string UniquePath(string directory, string baseName, string token, HashSet<string> usedPaths)
    {
        var path = Path.Combine(directory, baseName + " - " + token + ".3dm");
        var suffix = 2;
        while (usedPaths.Contains(path))
        {
            path = Path.Combine(directory, baseName + " - " + token + " " + suffix.ToString(CultureInfo.InvariantCulture) + ".3dm");
            suffix++;
        }

        return path;
    }

    private static TesseraWriteResult WriteOne(TesseraModel model, IReadOnlyList<TesseraElement> elements, string path)
    {
        using var file = new File3dm();
        file.ApplicationName = TesseraKeys.ApplicationName;
        file.ApplicationDetails = "Direct Revit 2025 export. Z-up metres. No IFC round-trip.";
        file.ApplicationUrl = "https://github.com/michael031288/fragments-plugin-revit";
        file.Settings.ModelUnitSystem = UnitSystem.Meters;
        file.Settings.ModelAbsoluteTolerance = 0.001;
        file.Notes = new File3dmNotes
        {
            Notes = string.Format(
                CultureInfo.InvariantCulture,
                "Tessera model{0}Units: meters{0}Up axis: Z{0}Layers: {1}",
                System.Environment.NewLine,
                model.LayerMode),
            IsVisible = false
        };

        SetDocumentString(file, TesseraKeys.Schema, TesseraKeys.SchemaVersion);
        SetDocumentString(file, TesseraKeys.Units, TesseraKeys.UnitMeters);
        SetDocumentString(file, TesseraKeys.UpAxis, TesseraKeys.UpAxisZ);
        SetDocumentString(file, TesseraKeys.LayerMode, model.LayerMode.ToString());
        SetDocumentString(file, TesseraKeys.Source, "Revit");
        foreach (var pair in model.DocumentStrings)
        {
            SetDocumentString(file, pair.Key, pair.Value);
        }

        var layers = new LayerTable(file, model.LayerMode);
        var objectCount = 0;
        var triangleCount = 0;
        var elementCount = 0;

        foreach (var element in elements)
        {
            var wroteElement = false;
            foreach (var source in element.Meshes)
            {
                var mesh = CreateMesh(source);
                if (mesh == null)
                {
                    continue;
                }

                var attributes = new ObjectAttributes
                {
                    Name = string.IsNullOrWhiteSpace(element.Name) ? element.IfcClass : element.Name,
                    LayerIndex = layers.IndexFor(element.Storey, element.IfcClass),
                    ColorSource = ObjectColorSource.ColorFromObject,
                    ObjectColor = Color.FromArgb(source.A, source.R, source.G, source.B)
                };

                SetUserString(attributes, TesseraKeys.IfcClass, element.IfcClass);
                SetUserString(attributes, TesseraKeys.IfcStorey, element.Storey);
                foreach (var pair in element.UserStrings)
                {
                    SetUserString(attributes, pair.Key, pair.Value);
                }

                SetUserString(attributes, TesseraKeys.Material, source.MaterialName);
                file.Objects.AddMesh(mesh, attributes);
                objectCount++;
                triangleCount += source.TriangleCount;
                wroteElement = true;
            }

            if (wroteElement)
            {
                elementCount++;
            }
        }

        var log = string.Empty;
        if (!file.WriteWithLog(path, FileVersion, out log))
        {
            throw new IOException("Rhino refused to write '" + path + "'. " + log);
        }

        return new TesseraWriteResult
        {
            ElementCount = elementCount,
            ObjectCount = objectCount,
            TriangleCount = triangleCount,
            Files = new[] { path }
        };
    }

    private static void SetDocumentString(File3dm file, string key, string? value)
    {
        var safeKey = TesseraText.UserKey(key);
        var safeValue = TesseraText.UserValue(value);
        if (safeKey == null || safeValue == null)
        {
            return;
        }

        file.Strings.SetString(safeKey, safeValue);
    }

    private static void SetUserString(ObjectAttributes attributes, string key, string? value)
    {
        var safeKey = TesseraText.UserKey(key);
        var safeValue = TesseraText.UserValue(value);
        if (safeKey == null || safeValue == null)
        {
            return;
        }

        attributes.SetUserString(safeKey, safeValue);
    }

    private static Mesh? CreateMesh(TesseraMesh source)
    {
        if (source.TriangleIndices.Count < 3 || source.Positions.Count < 9)
        {
            return null;
        }

        var mesh = new Mesh();
        mesh.Vertices.UseDoublePrecisionVertices = true;
        var vertexCount = source.Positions.Count / 3;
        for (var i = 0; i + 2 < source.TriangleIndices.Count; i += 3)
        {
            var a = source.TriangleIndices[i];
            var b = source.TriangleIndices[i + 1];
            var c = source.TriangleIndices[i + 2];
            if (a < 0 || b < 0 || c < 0 || a >= vertexCount || b >= vertexCount || c >= vertexCount)
            {
                continue;
            }

            if (a == b || b == c || a == c)
            {
                continue;
            }

            var start = mesh.Vertices.Count;
            AddVertex(mesh, source, a);
            AddVertex(mesh, source, b);
            AddVertex(mesh, source, c);
            mesh.Faces.AddFace(start, start + 1, start + 2);
        }

        if (mesh.Faces.Count == 0)
        {
            return null;
        }

        mesh.Normals.ComputeNormals();
        mesh.Compact();
        return mesh;
    }

    private static void AddVertex(Mesh mesh, TesseraMesh source, int index)
    {
        var offset = index * 3;
        mesh.Vertices.Add(source.Positions[offset], source.Positions[offset + 1], source.Positions[offset + 2]);
    }

    private sealed class LayerTable
    {
        private readonly File3dm _file;
        private readonly TesseraLayerMode _mode;
        private readonly Dictionary<string, int> _indexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public LayerTable(File3dm file, TesseraLayerMode mode)
        {
            _file = file;
            _mode = mode;
        }

        public int IndexFor(string? storey, string? ifcClass)
        {
            var storeyName = TesseraText.LayerName(storey, "Unassigned");
            var className = TesseraText.LayerName(ifcClass, "IfcBuildingElementProxy");
            var key = _mode switch
            {
                TesseraLayerMode.Class => "class:" + className,
                TesseraLayerMode.Storey => "storey:" + storeyName,
                _ => "both:" + storeyName + ":" + className
            };

            if (_indexes.TryGetValue(key, out var existing))
            {
                return existing;
            }

            int index;
            if (_mode == TesseraLayerMode.Class)
            {
                index = Add(className, TesseraClassPalette.ForClass(className), Guid.Empty);
            }
            else if (_mode == TesseraLayerMode.Storey)
            {
                index = Add(storeyName, TesseraClassPalette.Storey, Guid.Empty);
            }
            else
            {
                var parent = IndexForStorey(storeyName);
                var parentId = _file.AllLayers.FindIndex(parent).Id;
                index = Add(className, TesseraClassPalette.ForClass(className), parentId);
            }

            _indexes[key] = index;
            return index;
        }

        private int IndexForStorey(string storeyName)
        {
            var key = "storey:" + storeyName;
            if (_indexes.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var index = Add(storeyName, TesseraClassPalette.Storey, Guid.Empty);
            _indexes[key] = index;
            return index;
        }

        private int Add(string name, Color color, Guid parentId)
        {
            var index = parentId == Guid.Empty
                ? _file.AllLayers.AddLayer(name, color)
                : _file.AllLayers.AddLayer(name, color, parentId);
            if (index < 0)
            {
                throw new InvalidOperationException("Could not add Rhino layer '" + name + "'.");
            }

            return index;
        }
    }
}
