using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Tessera.Core;

public sealed class TsraWriteOptions
{
    public int ZstdLevel { get; init; } = 19;

    public int ChunkTriangles { get; init; } = 32_768;

    public int CanonicalChunkBytes { get; init; } = 256 * 1024;

    public int ProxyBoxes { get; init; } = 4096;
}

/// <summary>
/// A Tessera compiled building. Geometry is metres, Z-up, in project or shared
/// coordinates. <see cref="ToBytes"/> writes format 0.6 (<c>.tsra</c>).
/// </summary>
public sealed class TsraModel
{
    public const int GridPerMetre = 10_000;
    public const uint NoEntity = uint.MaxValue;

    private readonly List<string> _strings = [""];
    private readonly Dictionary<string, uint> _stringIds = new(StringComparer.Ordinal) { [""] = 0 };
    private readonly List<string> _classNames = new();
    private readonly List<uint> _classStringIds = new();
    private readonly Dictionary<string, ushort> _classIndex = new(StringComparer.Ordinal);
    private readonly List<EntityRow> _entities = new();
    private readonly List<(uint Whole, uint Part)> _aggregates = new();
    private readonly List<(uint Container, uint Element)> _contained = new();
    private readonly List<(byte R, byte G, byte B, byte A)> _styles = new();
    private readonly List<MeshRow> _meshes = new();
    private readonly List<PropertySet> _propertySets = new();
    private readonly Dictionary<(uint Entity, string Set), int> _propertyIndex = new();

    public string Generator { get; set; } = "Tessera.Revit";

    public string Schema { get; set; } = "Revit";

    public string SourceName { get; set; } = "";

    public ulong SourceBytes { get; set; }

    public string SourceBlake3 { get; set; } = "";

    public int EntityCount => _entities.Count;

    public int MeshCount => _meshes.Count;

    public int TriangleCount { get; private set; }

    public static byte[] StableId(string text)
    {
        return TsraHash.Hash16(Encoding.UTF8.GetBytes(text ?? ""));
    }

    public uint Intern(string? text)
    {
        text ??= "";
        if (_stringIds.TryGetValue(text, out var id))
        {
            return id;
        }

        if (_strings.Count == int.MaxValue)
        {
            throw new InvalidOperationException("The Tessera string table is full.");
        }

        id = (uint)_strings.Count;
        _strings.Add(text);
        _stringIds[text] = id;
        return id;
    }

    public uint AddEntity(string ifcClass, string? name, ReadOnlySpan<byte> globalId, uint stepId, string? typeName, uint level, string? material)
    {
        if (string.IsNullOrWhiteSpace(ifcClass))
        {
            ifcClass = "IfcBuildingElementProxy";
        }

        if (!_classIndex.TryGetValue(ifcClass, out var classIndex))
        {
            if (_classNames.Count == ushort.MaxValue)
            {
                throw new InvalidOperationException("Too many IFC classes for one Tessera file.");
            }

            classIndex = (ushort)_classNames.Count;
            _classNames.Add(ifcClass);
            _classStringIds.Add(Intern(ifcClass));
            _classIndex[ifcClass] = classIndex;
        }

        var gid = new byte[16];
        if (!globalId.IsEmpty)
        {
            globalId.Slice(0, Math.Min(16, globalId.Length)).CopyTo(gid);
        }

        if (level != NoEntity && level >= (uint)_entities.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(level), "Level must be an entity already added, or NoEntity.");
        }

        _entities.Add(new EntityRow
        {
            Class = classIndex,
            GlobalId = gid,
            StepId = stepId,
            Name = Intern(name),
            Type = string.IsNullOrEmpty(typeName) ? NoEntity : Intern(typeName),
            Level = level,
            Material = string.IsNullOrEmpty(material) ? 0 : Intern(material)
        });
        return (uint)(_entities.Count - 1);
    }

    public void AddAggregate(uint whole, uint part) => AddPair(_aggregates, whole, part);

    public void AddContainedIn(uint container, uint element) => AddPair(_contained, container, element);

    public uint AddStyle(byte red, byte green, byte blue, byte alpha)
    {
        for (var i = 0; i < _styles.Count; i++)
        {
            var style = _styles[i];
            if (style.R == red && style.G == green && style.B == blue && style.A == alpha)
            {
                return (uint)(i + 1);
            }
        }

        _styles.Add((red, green, blue, alpha));
        return (uint)_styles.Count;
    }

    public void AddMesh(uint entity, uint style, IReadOnlyList<double> triangleXyz)
    {
        if (entity >= (uint)_entities.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(entity));
        }

        if (style > (uint)_styles.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(style));
        }

        if (triangleXyz.Count == 0)
        {
            return;
        }

        if (triangleXyz.Count % 9 != 0)
        {
            throw new ArgumentException("Triangle coordinates must be nine numbers per triangle.", nameof(triangleXyz));
        }

        var weld = new Dictionary<(long X, long Y, long Z), int>();
        var grid = new List<long>();
        var indices = new List<int>();
        long minX = long.MaxValue, minY = long.MaxValue, minZ = long.MaxValue;
        long maxX = long.MinValue, maxY = long.MinValue, maxZ = long.MinValue;
        for (var i = 0; i < triangleXyz.Count; i += 9)
        {
            var a = Vertex(triangleXyz, i);
            var b = Vertex(triangleXyz, i + 3);
            var c = Vertex(triangleXyz, i + 6);
            if (a == b || b == c || a == c)
            {
                continue;
            }

            indices.Add(a);
            indices.Add(b);
            indices.Add(c);
        }

        if (indices.Count == 0)
        {
            return;
        }

        for (var i = 0; i < grid.Count; i += 3)
        {
            minX = Math.Min(minX, grid[i]);
            minY = Math.Min(minY, grid[i + 1]);
            minZ = Math.Min(minZ, grid[i + 2]);
            maxX = Math.Max(maxX, grid[i]);
            maxY = Math.Max(maxY, grid[i + 1]);
            maxZ = Math.Max(maxZ, grid[i + 2]);
        }

        if (maxX - minX > int.MaxValue || maxY - minY > int.MaxValue || maxZ - minZ > int.MaxValue)
        {
            throw new InvalidOperationException("One element is wider than 214 km, which the Tessera prototype grid cannot store.");
        }

        TriangleCount += indices.Count / 3;
        _meshes.Add(new MeshRow
        {
            Entity = entity,
            Style = style,
            Grid = grid.ToArray(),
            Indices = indices.ToArray(),
            MinX = minX,
            MinY = minY,
            MinZ = minZ,
            MaxX = maxX,
            MaxY = maxY,
            MaxZ = maxZ
        });

        int Vertex(IReadOnlyList<double> source, int at)
        {
            var key = (TsraGeometry.ToGrid(source[at]), TsraGeometry.ToGrid(source[at + 1]), TsraGeometry.ToGrid(source[at + 2]));
            if (!weld.TryGetValue(key, out var index))
            {
                index = grid.Count / 3;
                weld[key] = index;
                grid.Add(key.Item1);
                grid.Add(key.Item2);
                grid.Add(key.Item3);
                minX = Math.Min(minX, key.Item1);
                minY = Math.Min(minY, key.Item2);
                minZ = Math.Min(minZ, key.Item3);
                maxX = Math.Max(maxX, key.Item1);
                maxY = Math.Max(maxY, key.Item2);
                maxZ = Math.Max(maxZ, key.Item3);
            }

            return index;
        }
    }

    public void AddTextProperty(uint entity, string setName, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var set = Properties(entity, setName);
        if (set.Exists(item => item.Name == name))
        {
            return;
        }

        set.Add(new PropertyValue { Name = name, Text = value });
    }

    public void AddRealProperty(uint entity, string setName, string name, double value, string? ifcType)
    {
        if (string.IsNullOrWhiteSpace(name) || !double.IsFinite(value))
        {
            return;
        }

        var set = Properties(entity, setName);
        if (set.Exists(item => item.Name == name))
        {
            return;
        }

        set.Add(new PropertyValue { Name = name, Real = value, IfcType = ifcType });
    }

    public void Write(string path, TsraWriteOptions? options = null)
    {
        File.WriteAllBytes(path, ToBytes(options));
    }

    public byte[] ToBytes(TsraWriteOptions? options = null)
    {
        options ??= new TsraWriteOptions();
        if (options.ZstdLevel is < 1 or > 22)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "zstd level must be from 1 to 22.");
        }

        PrepareStrings();

        var prototypes = new Prototype[_meshes.Count];
        var worldX = new long[_meshes.Count][];
        var worldY = new long[_meshes.Count][];
        var worldZ = new long[_meshes.Count][];
        long boundsMinX = long.MaxValue, boundsMinY = long.MaxValue, boundsMinZ = long.MaxValue;
        long boundsMaxX = long.MinValue, boundsMaxY = long.MinValue, boundsMaxZ = long.MinValue;
        var entityMinX = new long[_entities.Count];
        var entityMinY = new long[_entities.Count];
        var entityMinZ = new long[_entities.Count];
        var entityMaxX = new long[_entities.Count];
        var entityMaxY = new long[_entities.Count];
        var entityMaxZ = new long[_entities.Count];
        var entityHasBox = new bool[_entities.Count];
        Array.Fill(entityMinX, long.MaxValue);
        Array.Fill(entityMinY, long.MaxValue);
        Array.Fill(entityMinZ, long.MaxValue);
        Array.Fill(entityMaxX, long.MinValue);
        Array.Fill(entityMaxY, long.MinValue);
        Array.Fill(entityMaxZ, long.MinValue);

        for (var i = 0; i < _meshes.Count; i++)
        {
            var mesh = _meshes[i];
            var count = mesh.Grid.Length / 3;
            var local = new int[mesh.Grid.Length];
            var x = new long[count];
            var y = new long[count];
            var z = new long[count];
            for (var v = 0; v < count; v++)
            {
                x[v] = mesh.Grid[v * 3];
                y[v] = mesh.Grid[v * 3 + 1];
                z[v] = mesh.Grid[v * 3 + 2];
                local[v * 3] = (int)(x[v] - mesh.MinX);
                local[v * 3 + 1] = (int)(y[v] - mesh.MinY);
                local[v * 3 + 2] = (int)(z[v] - mesh.MinZ);
                boundsMinX = Math.Min(boundsMinX, x[v]);
                boundsMinY = Math.Min(boundsMinY, y[v]);
                boundsMinZ = Math.Min(boundsMinZ, z[v]);
                boundsMaxX = Math.Max(boundsMaxX, x[v]);
                boundsMaxY = Math.Max(boundsMaxY, y[v]);
                boundsMaxZ = Math.Max(boundsMaxZ, z[v]);
                var entity = (int)mesh.Entity;
                entityHasBox[entity] = true;
                entityMinX[entity] = Math.Min(entityMinX[entity], x[v]);
                entityMinY[entity] = Math.Min(entityMinY[entity], y[v]);
                entityMinZ[entity] = Math.Min(entityMinZ[entity], z[v]);
                entityMaxX[entity] = Math.Max(entityMaxX[entity], x[v]);
                entityMaxY[entity] = Math.Max(entityMaxY[entity], y[v]);
                entityMaxZ[entity] = Math.Max(entityMaxZ[entity], z[v]);
            }

            var indices = new uint[mesh.Indices.Length];
            for (var t = 0; t < mesh.Indices.Length; t++)
            {
                indices[t] = (uint)mesh.Indices[t];
            }

            prototypes[i] = new Prototype
            {
                Local = local,
                VertexCount = count,
                Indices = indices,
                TriangleCount = mesh.Indices.Length / 3,
                Watertight = TsraGeometry.IsWatertight(local, count, mesh.Indices),
                PositionX = mesh.MinX,
                PositionY = mesh.MinY,
                PositionZ = mesh.MinZ,
                MinX = 0,
                MinY = 0,
                MinZ = 0,
                MaxX = (int)(mesh.MaxX - mesh.MinX),
                MaxY = (int)(mesh.MaxY - mesh.MinY),
                MaxZ = (int)(mesh.MaxZ - mesh.MinZ)
            };
            worldX[i] = x;
            worldY[i] = y;
            worldZ[i] = z;
        }

        var empty = _meshes.Count == 0;
        var originX = empty ? 0 : boundsMinX / 2 + boundsMaxX / 2;
        var originY = empty ? 0 : boundsMinY / 2 + boundsMaxY / 2;
        var originZ = empty ? 0 : boundsMinZ / 2 + boundsMaxZ / 2;

        var flat = new List<Cluster>();
        for (var i = 0; i < _meshes.Count; i++)
        {
            var word = (_meshes[i].Style << 16) | (prototypes[i].Watertight ? 2u : 0u);
            foreach (var cluster in TsraGeometry.Clusterize(worldX[i], worldY[i], worldZ[i], _meshes[i].Indices, 0))
            {
                cluster.SlotEntity = _meshes[i].Entity;
                cluster.SlotMaterial = word;
                flat.Add(cluster);
            }
        }

        var built = new List<BuiltChunk>();
        foreach (var group in TsraGeometry.Split(flat, options.ChunkTriangles))
        {
            var slots = new List<(uint Entity, uint Material)>();
            var slotOf = new Dictionary<(uint, uint), uint>();
            foreach (var cluster in group)
            {
                var key = (cluster.SlotEntity, cluster.SlotMaterial);
                if (!slotOf.TryGetValue(key, out var slot))
                {
                    slot = (uint)slots.Count;
                    slots.Add(key);
                    slotOf[key] = slot;
                }

                for (var e = 0; e < cluster.Elements.Count; e++)
                {
                    cluster.Elements[e] = slot;
                }
            }

            built.Add(TsraGeometry.EncodeChunk(group, slots));
        }

        var blobBytes = new List<byte[]>();
        var blobOf = new uint[prototypes.Length];
        var blobIds = new Dictionary<string, uint>(StringComparer.Ordinal);
        for (var i = 0; i < prototypes.Length; i++)
        {
            var blob = TsraGeometry.EncodeBlob(prototypes[i].Local, prototypes[i].VertexCount, prototypes[i].Indices, prototypes[i].TriangleCount);
            var key = Convert.ToHexString(TsraHash.Hash16(blob));
            if (!blobIds.TryGetValue(key, out var id))
            {
                id = (uint)blobBytes.Count;
                blobBytes.Add(blob);
                blobIds[key] = id;
            }

            blobOf[i] = id;
        }

        var (canonicalIndex, canonicalData) = EncodeCanonical(blobBytes, options);
        var withGeometry = 0;
        for (var i = 0; i < entityHasBox.Length; i++)
        {
            if (entityHasBox[i])
            {
                withGeometry++;
            }
        }

        uint meshlets = 0;
        uint triangles = 0;
        foreach (var chunk in built)
        {
            meshlets += chunk.Meshlets;
            triangles += chunk.Triangles;
        }

        var sections = new List<(TsraKind Kind, byte[] Raw)>
        {
            (TsraKind.Meta, MetaJson(originX, originY, originZ, empty, boundsMinX, boundsMinY, boundsMinZ, boundsMaxX, boundsMaxY, boundsMaxZ, withGeometry, built.Count, meshlets, triangles)),
            (TsraKind.Strings, EncodeStrings()),
            (TsraKind.Entities, EncodeEntities()),
            (TsraKind.Styles, EncodeStyles()),
            (TsraKind.Proxy, EncodeProxy(entityHasBox, entityMinX, entityMinY, entityMinZ, entityMaxX, entityMaxY, entityMaxZ, empty, boundsMinX, boundsMinY, boundsMinZ, boundsMaxX, boundsMaxY, boundsMaxZ, options.ProxyBoxes)),
            (TsraKind.ChunkIndex, EncodeChunkIndex(built, options.ZstdLevel, out var renderData)),
            (TsraKind.Prototypes, EncodePrototypes(prototypes, blobOf)),
            (TsraKind.Parts, EncodeParts(prototypes, originX, originY, originZ)),
            (TsraKind.RenderData, renderData),
            (TsraKind.Relations, EncodeRelations()),
            (TsraKind.Properties, EncodeProperties()),
            (TsraKind.CanonicalIndex, canonicalIndex),
            (TsraKind.CanonicalData, canonicalData)
        };
        return TsraContainer.Finish(sections, options.ZstdLevel);
    }

    private byte[] MetaJson(
        long originX, long originY, long originZ, bool empty,
        long minX, long minY, long minZ, long maxX, long maxY, long maxZ,
        int withGeometry, int chunks, uint meshlets, uint triangles)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteString("generator", Generator ?? "");
            writer.WriteString("schema", Schema ?? "");
            writer.WriteStartObject("source");
            writer.WriteString("name", SourceName ?? "");
            writer.WriteNumber("bytes", SourceBytes);
            writer.WriteString("blake3", SourceBlake3 ?? "");
            writer.WriteEndObject();
            writer.WriteNumber("grid_per_metre", GridPerMetre);
            writer.WriteStartArray("origin");
            writer.WriteNumberValue(originX);
            writer.WriteNumberValue(originY);
            writer.WriteNumberValue(originZ);
            writer.WriteEndArray();
            writer.WriteStartObject("bounds");
            writer.WriteStartArray("min");
            writer.WriteNumberValue(empty ? 0 : minX);
            writer.WriteNumberValue(empty ? 0 : minY);
            writer.WriteNumberValue(empty ? 0 : minZ);
            writer.WriteEndArray();
            writer.WriteStartArray("max");
            writer.WriteNumberValue(empty ? 0 : maxX);
            writer.WriteNumberValue(empty ? 0 : maxY);
            writer.WriteNumberValue(empty ? 0 : maxZ);
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteStartObject("counts");
            writer.WriteNumber("entities", (ulong)_entities.Count);
            writer.WriteNumber("elements_with_geometry", (ulong)withGeometry);
            writer.WriteNumber("parts", (ulong)_meshes.Count);
            writer.WriteNumber("prototypes", (ulong)_meshes.Count);
            writer.WriteNumber("instanced_parts", 0ul);
            writer.WriteNumber("chunks", (ulong)chunks);
            writer.WriteNumber("meshlets", (ulong)meshlets);
            writer.WriteNumber("triangles", (ulong)triangles);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private byte[] EncodeStrings()
    {
        var output = new OutBuf();
        output.U32((uint)_strings.Count);
        uint offset = 0;
        output.U32(0);
        var bytes = new List<byte[]>(_strings.Count);
        foreach (var text in _strings)
        {
            var encoded = Encoding.UTF8.GetBytes(text);
            offset += (uint)encoded.Length;
            output.U32(offset);
            bytes.Add(encoded);
        }

        foreach (var encoded in bytes)
        {
            output.Bytes(encoded);
        }

        return output.ToArray();
    }

    private byte[] EncodeEntities()
    {
        var output = new OutBuf();
        output.U32((uint)_entities.Count);
        output.U32((uint)_classNames.Count);
        foreach (var name in _classStringIds)
        {
            output.U32(name);
        }

        foreach (var entity in _entities)
        {
            output.Bytes(entity.GlobalId);
        }

        uint previous = 0;
        foreach (var entity in _entities)
        {
            output.U32(entity.StepId - previous);
            previous = entity.StepId;
        }

        foreach (var entity in _entities)
        {
            output.U16(entity.Class);
        }

        output.Align(4);
        foreach (var entity in _entities)
        {
            output.U32(entity.Name);
        }

        foreach (var entity in _entities)
        {
            output.U32(entity.Type);
        }

        foreach (var entity in _entities)
        {
            output.U32(entity.Level);
        }

        foreach (var entity in _entities)
        {
            output.U32(entity.Material);
        }

        foreach (var entity in _entities)
        {
            output.U32(0);
        }

        return output.ToArray();
    }

    private byte[] EncodeStyles()
    {
        var output = new OutBuf();
        output.U32((uint)_styles.Count);
        foreach (var style in _styles)
        {
            output.U32((uint)(style.R | (style.G << 8) | (style.B << 16) | (style.A << 24)));
        }

        return output.ToArray();
    }

    private byte[] EncodeProxy(
        bool[] hasBox, long[] minX, long[] minY, long[] minZ, long[] maxX, long[] maxY, long[] maxZ,
        bool empty, long modelMinX, long modelMinY, long modelMinZ, long modelMaxX, long modelMaxY, long modelMaxZ,
        int limit)
    {
        var order = new List<int>();
        for (var i = 0; i < hasBox.Length; i++)
        {
            if (hasBox[i])
            {
                order.Add(i);
            }
        }

        order.Sort((a, b) =>
        {
            var volume = BoxVolume(maxX[b] - minX[b], maxY[b] - minY[b], maxZ[b] - minZ[b])
                .CompareTo(BoxVolume(maxX[a] - minX[a], maxY[a] - minY[a], maxZ[a] - minZ[a]));
            return volume != 0 ? volume : a.CompareTo(b);
        });
        if (order.Count > limit)
        {
            order.RemoveRange(limit, order.Count - limit);
        }

        order.Sort();
        var output = new OutBuf();
        output.U32((uint)order.Count);
        foreach (var entity in order)
        {
            output.U32((uint)entity);
            output.U16(Quantise(minX[entity], modelMinX, modelMaxX, false, empty));
            output.U16(Quantise(minY[entity], modelMinY, modelMaxY, false, empty));
            output.U16(Quantise(minZ[entity], modelMinZ, modelMaxZ, false, empty));
            output.U16(Quantise(maxX[entity], modelMinX, modelMaxX, true, empty));
            output.U16(Quantise(maxY[entity], modelMinY, modelMaxY, true, empty));
            output.U16(Quantise(maxZ[entity], modelMinZ, modelMaxZ, true, empty));
        }

        return output.ToArray();
    }

    private static byte[] EncodeChunkIndex(List<BuiltChunk> chunks, int level, out byte[] renderData)
    {
        var index = new OutBuf();
        var data = new OutBuf();
        index.U32((uint)chunks.Count);
        index.U32(0);
        foreach (var chunk in chunks)
        {
            var compressed = TsraZstd.Compress(chunk.Raw, level);
            data.Align(8);
            index.U64((ulong)data.Length);
            data.Bytes(compressed);
            index.U32((uint)compressed.Length);
            index.U32((uint)chunk.Raw.Length);
            index.U32(TsraGeometry.ChunkWorld);
            index.U32(chunk.Meshlets);
            index.U32(chunk.Triangles);
            index.U32(chunk.Vertices);
            index.I64(chunk.OriginX);
            index.I64(chunk.OriginY);
            index.I64(chunk.OriginZ);
            index.I32(chunk.MinX);
            index.I32(chunk.MinY);
            index.I32(chunk.MinZ);
            index.I32(chunk.MaxX);
            index.I32(chunk.MaxY);
            index.I32(chunk.MaxZ);
            index.Bytes(TsraHash.Hash16(chunk.Raw));
        }

        renderData = data.ToArray();
        return index.ToArray();
    }

    private static byte[] EncodePrototypes(Prototype[] prototypes, uint[] blobOf)
    {
        var output = new OutBuf();
        output.U32((uint)prototypes.Length);
        output.U32(0);
        for (var i = 0; i < prototypes.Length; i++)
        {
            var prototype = prototypes[i];
            output.U32(NoEntity);
            output.U32(0);
            output.U32(0);
            output.U32(blobOf[i]);
            output.I32(prototype.MinX);
            output.I32(prototype.MinY);
            output.I32(prototype.MinZ);
            output.I32(prototype.MaxX);
            output.I32(prototype.MaxY);
            output.I32(prototype.MaxZ);
            output.U32(prototype.Watertight ? 1u : 0u);
            output.U32(0);
        }

        return output.ToArray();
    }

    private byte[] EncodeParts(Prototype[] prototypes, long originX, long originY, long originZ)
    {
        var rotation = TsraGeometry.IdentityRotation();
        var rows = new OutBuf();
        for (var i = 0; i < _meshes.Count; i++)
        {
            var mesh = _meshes[i];
            var word = (mesh.Style << 16) | (prototypes[i].Watertight ? 2u : 0u);
            rows.U32(mesh.Entity);
            rows.U32((uint)i);
            rows.I32(CheckedRelative(prototypes[i].PositionX, originX));
            rows.I32(CheckedRelative(prototypes[i].PositionY, originY));
            rows.I32(CheckedRelative(prototypes[i].PositionZ, originZ));
            rows.U32(rotation.Word0);
            rows.U32(rotation.Word1);
            rows.U32(rotation.Word2);
            rows.F32(1);
            rows.F32(1);
            rows.F32(1);
            rows.U32(word);
        }

        var output = new OutBuf();
        output.U32((uint)_meshes.Count);
        output.U32(0);
        output.Bytes(rows.ToArray());
        return output.ToArray();
    }

    private byte[] EncodeRelations()
    {
        var output = new OutBuf();
        var kinds = new List<(uint Kind, List<(uint Key, uint Value)> Pairs)>();
        if (_aggregates.Count > 0)
        {
            kinds.Add((1, _aggregates.Select(pair => (pair.Whole, pair.Part)).ToList()));
        }

        if (_contained.Count > 0)
        {
            kinds.Add((3, _contained.Select(pair => (pair.Container, pair.Element)).ToList()));
        }

        output.U32((uint)kinds.Count);
        foreach (var (kind, pairs) in kinds)
        {
            output.U32(kind);
            EncodeCsr(output, pairs);
        }

        return output.ToArray();
    }

    private byte[] EncodeProperties()
    {
        var values = new List<(byte Kind, ulong Data)>();
        var valueIds = new Dictionary<(byte Kind, ulong Data), uint>();
        var sets = new List<StoredSet>();
        var setIds = new Dictionary<string, uint>(StringComparer.Ordinal);
        var links = new List<(uint Entity, uint Set)>();

        uint Value(byte kind, ulong data)
        {
            if (!valueIds.TryGetValue((kind, data), out var id))
            {
                id = (uint)values.Count;
                values.Add((kind, data));
                valueIds[(kind, data)] = id;
            }

            return id;
        }

        foreach (var group in _propertySets)
        {
            var pairs = new List<(uint Name, uint Value)>();
            var types = new List<uint>();
            foreach (var property in group.Values)
            {
                uint value;
                uint type = 0;
                if (property.Real is double real)
                {
                    value = Value(2, (ulong)BitConverter.DoubleToUInt64Bits(real));
                    if (!string.IsNullOrEmpty(property.IfcType))
                    {
                        type = Intern(property.IfcType);
                    }
                }
                else
                {
                    value = Value(1, Intern(property.Text));
                }

                pairs.Add((Intern(property.Name), value));
                types.Add(type);
            }

            if (types.TrueForAll(type => type == 0))
            {
                types.Clear();
            }

            var name = Intern(group.Name);
            var key = SetKey(0, name, pairs, types);
            if (!setIds.TryGetValue(key, out var setId))
            {
                setId = (uint)sets.Count;
                sets.Add(new StoredSet { Name = name, Pairs = pairs, Types = types });
                setIds[key] = setId;
            }

            links.Add((group.Entity, setId));
        }

        var output = new OutBuf();
        output.U32((uint)values.Count);
        foreach (var value in values)
        {
            output.U8(value.Kind);
        }

        output.Align(4);
        foreach (var value in values)
        {
            output.U64(value.Data);
        }

        output.U32(0);
        output.U32(0);

        var shapes = new List<(byte Kind, uint Name, List<uint> Names, List<uint> Types)>();
        var shapeIds = new Dictionary<string, uint>(StringComparer.Ordinal);
        var setShape = new uint[sets.Count];
        for (var i = 0; i < sets.Count; i++)
        {
            var set = sets[i];
            var names = set.Pairs.Select(pair => pair.Name).ToList();
            var types = new List<uint>(set.Pairs.Count);
            for (var p = 0; p < set.Pairs.Count; p++)
            {
                types.Add(p < set.Types.Count ? set.Types[p] : 0);
            }

            var key = SetKey(0, set.Name, names.Select(item => (item, 0u)).ToList(), types);
            if (!shapeIds.TryGetValue(key, out var shape))
            {
                shape = (uint)shapes.Count;
                shapes.Add((0, set.Name, names, types));
                shapeIds[key] = shape;
            }

            setShape[i] = shape;
        }

        output.U32((uint)shapes.Count);
        output.U32((uint)shapes.Sum(shape => shape.Names.Count));
        foreach (var shape in shapes)
        {
            output.U32(shape.Name);
        }

        foreach (var shape in shapes)
        {
            output.U8(shape.Kind);
        }

        output.Align(4);
        foreach (var shape in shapes)
        {
            output.U32((uint)shape.Names.Count);
        }

        foreach (var shape in shapes)
        {
            foreach (var name in shape.Names)
            {
                output.U32(name);
            }
        }

        foreach (var shape in shapes)
        {
            foreach (var type in shape.Types)
            {
                output.U32(type);
            }
        }

        output.U32((uint)sets.Count);
        output.U32((uint)sets.Sum(set => set.Pairs.Count));
        foreach (var shape in setShape)
        {
            output.U32(shape);
        }

        foreach (var set in sets)
        {
            foreach (var pair in set.Pairs)
            {
                output.U32(pair.Value);
            }
        }

        var grouped = EncodeLinkLists(links);
        output.U32((uint)grouped.Lists.Count);
        output.U32((uint)grouped.Lists.Sum(list => list.Count));
        foreach (var list in grouped.Lists)
        {
            output.U32((uint)list.Count);
        }

        foreach (var list in grouped.Lists)
        {
            foreach (var set in list)
            {
                output.U32(set);
            }
        }

        output.U32((uint)grouped.Keys.Count);
        foreach (var key in grouped.Keys)
        {
            output.U32(key);
        }

        foreach (var list in grouped.EntityList)
        {
            output.U32(list);
        }

        return output.ToArray();
    }

    private static (List<uint> Keys, List<uint> EntityList, List<List<uint>> Lists) EncodeLinkLists(List<(uint Entity, uint Set)> links)
    {
        var pairs = links.ToList();
        pairs.Sort((a, b) => a.Entity != b.Entity ? a.Entity.CompareTo(b.Entity) : a.Set.CompareTo(b.Set));
        var keys = new List<uint>();
        var rows = new List<List<uint>>();
        foreach (var (entity, set) in pairs)
        {
            if (keys.Count == 0 || keys[^1] != entity)
            {
                keys.Add(entity);
                rows.Add(new List<uint>());
            }

            if (rows[^1].Count == 0 || rows[^1][^1] != set)
            {
                rows[^1].Add(set);
            }
        }

        var lists = new List<List<uint>>();
        var ids = new Dictionary<string, uint>(StringComparer.Ordinal);
        var entityList = new List<uint>(keys.Count);
        foreach (var row in rows)
        {
            var key = string.Join(",", row);
            if (!ids.TryGetValue(key, out var id))
            {
                id = (uint)lists.Count;
                lists.Add(row);
                ids[key] = id;
            }

            entityList.Add(id);
        }

        return (keys, entityList, lists);
    }

    private static (byte[] Index, byte[] Data) EncodeCanonical(List<byte[]> blobs, TsraWriteOptions options)
    {
        var rawChunks = new List<List<byte>> { new() };
        var entries = new List<(int Chunk, int Offset, int Length, byte[] Blob)>();
        var cut = false;
        foreach (var blob in blobs)
        {
            if (rawChunks[^1].Count > 0 && (cut || rawChunks[^1].Count + blob.Length > options.CanonicalChunkBytes))
            {
                rawChunks.Add(new List<byte>());
            }

            cut = ContentCut(TsraHash.Hash16(blob), blob.Length, options.CanonicalChunkBytes);
            entries.Add((rawChunks.Count - 1, rawChunks[^1].Count, blob.Length, blob));
            rawChunks[^1].AddRange(blob);
        }

        if (rawChunks.Count > 0 && rawChunks[^1].Count == 0)
        {
            rawChunks.RemoveAt(rawChunks.Count - 1);
        }

        var compressed = new byte[rawChunks.Count][];
        for (var i = 0; i < rawChunks.Count; i++)
        {
            compressed[i] = TsraZstd.Compress(rawChunks[i].ToArray(), options.ZstdLevel);
        }

        var index = new OutBuf();
        var data = new OutBuf();
        index.U32((uint)rawChunks.Count);
        index.U32((uint)blobs.Count);
        for (var i = 0; i < rawChunks.Count; i++)
        {
            data.Align(8);
            index.U64((ulong)data.Length);
            index.U32((uint)compressed[i].Length);
            index.U32((uint)rawChunks[i].Count);
            index.Bytes(TsraHash.Hash16(rawChunks[i].ToArray()));
            data.Bytes(compressed[i]);
        }

        foreach (var entry in entries)
        {
            index.U32((uint)entry.Chunk);
            index.U32((uint)entry.Offset);
            index.U32((uint)entry.Length);
            index.Bytes(entry.Blob.AsSpan(0, 8));
            index.U32(0);
            index.Bytes(TsraHash.Hash16(entry.Blob).AsSpan(0, 8));
        }

        return (index.ToArray(), data.ToArray());
    }

    private static void EncodeCsr(OutBuf output, List<(uint Key, uint Value)> pairs)
    {
        pairs.Sort((a, b) => a.Key != b.Key ? a.Key.CompareTo(b.Key) : a.Value.CompareTo(b.Value));
        var keys = new List<uint>();
        var values = new List<uint>();
        var offsets = new List<int> { 0 };
        foreach (var (key, value) in pairs)
        {
            if (values.Count > 0 && keys[^1] == key && values[^1] == value)
            {
                continue;
            }

            if (keys.Count == 0 || keys[^1] != key)
            {
                if (keys.Count > 0)
                {
                    offsets.Add(values.Count);
                }

                keys.Add(key);
            }

            values.Add(value);
        }

        if (keys.Count > 0)
        {
            offsets.Add(values.Count);
        }

        output.U32((uint)keys.Count);
        output.U32((uint)values.Count);
        foreach (var key in keys)
        {
            output.U32(key);
        }

        for (var i = 0; i < keys.Count; i++)
        {
            output.U32((uint)(offsets[i + 1] - offsets[i]));
        }

        foreach (var value in values)
        {
            output.U32(value);
        }
    }

    private void PrepareStrings()
    {
        foreach (var group in _propertySets)
        {
            Intern(group.Name);
            foreach (var property in group.Values)
            {
                Intern(property.Name);
                if (property.Real is null)
                {
                    Intern(property.Text);
                }
                else if (!string.IsNullOrEmpty(property.IfcType))
                {
                    Intern(property.IfcType);
                }
            }
        }
    }

    private static bool ContentCut(byte[] hash, int size, int budget)
    {
        var head = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(hash);
        var limit = Math.Min(1.0, 2.0 * size / Math.Max(1, budget)) * uint.MaxValue;
        return head < limit;
    }

    private static int CheckedRelative(long position, long origin)
    {
        var relative = position - origin;
        if (relative < int.MinValue || relative > int.MaxValue)
        {
            throw new InvalidOperationException("A part is more than 214 km from the model origin.");
        }

        return (int)relative;
    }

    private static double BoxVolume(long dx, long dy, long dz)
    {
        return (dx + 1.0) * (dy + 1.0) * (dz + 1.0);
    }

    private static ushort Quantise(long value, long min, long max, bool up, bool empty)
    {
        if (empty)
        {
            return 0;
        }

        var span = Math.Max(1.0, max - min);
        var t = (value - min) / span * 65535.0;
        var rounded = up ? Math.Ceiling(t) : Math.Floor(t);
        return (ushort)Math.Clamp(rounded, 0.0, 65535.0);
    }

    private static string SetKey(byte kind, uint name, List<(uint Name, uint Value)> pairs, List<uint> types)
    {
        var builder = new StringBuilder();
        builder.Append(kind).Append('|').Append(name).Append('|');
        foreach (var pair in pairs)
        {
            builder.Append(pair.Name).Append(':').Append(pair.Value).Append(',');
        }

        builder.Append('|');
        foreach (var type in types)
        {
            builder.Append(type).Append(',');
        }

        return builder.ToString();
    }

    private List<PropertyValue> Properties(uint entity, string setName)
    {
        if (entity >= (uint)_entities.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(entity));
        }

        setName = string.IsNullOrWhiteSpace(setName) ? "Revit" : setName;
        if (!_propertyIndex.TryGetValue((entity, setName), out var index))
        {
            index = _propertySets.Count;
            _propertySets.Add(new PropertySet { Entity = entity, Name = setName, Values = new List<PropertyValue>() });
            _propertyIndex[(entity, setName)] = index;
        }

        return _propertySets[index].Values;
    }

    private void AddPair(List<(uint Whole, uint Part)> pairs, uint whole, uint part)
    {
        if (whole >= (uint)_entities.Count || part >= (uint)_entities.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(whole));
        }

        pairs.Add((whole, part));
    }

    private sealed class EntityRow
    {
        public ushort Class;
        public byte[] GlobalId = new byte[16];
        public uint StepId;
        public uint Name;
        public uint Type;
        public uint Level;
        public uint Material;
    }

    private sealed class MeshRow
    {
        public uint Entity;
        public uint Style;
        public long[] Grid = Array.Empty<long>();
        public int[] Indices = Array.Empty<int>();
        public long MinX;
        public long MinY;
        public long MinZ;
        public long MaxX;
        public long MaxY;
        public long MaxZ;
    }

    private sealed class Prototype
    {
        public int[] Local = Array.Empty<int>();
        public int VertexCount;
        public uint[] Indices = Array.Empty<uint>();
        public int TriangleCount;
        public bool Watertight;
        public long PositionX;
        public long PositionY;
        public long PositionZ;
        public int MinX;
        public int MinY;
        public int MinZ;
        public int MaxX;
        public int MaxY;
        public int MaxZ;
    }

    private sealed class PropertySet
    {
        public uint Entity;
        public string Name = "";
        public List<PropertyValue> Values = new();
    }

    private sealed class PropertyValue
    {
        public string Name = "";
        public string? Text;
        public double? Real;
        public string? IfcType;
    }

    private sealed class StoredSet
    {
        public uint Name;
        public List<(uint Name, uint Value)> Pairs = new();
        public List<uint> Types = new();
    }
}
