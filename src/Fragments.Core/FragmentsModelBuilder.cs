namespace Fragments.Core;

public sealed class FragmentsModelBuilder
{
    private readonly List<ItemRecord> _items = new List<ItemRecord>();
    private readonly Dictionary<uint, int> _localIdIndex = new Dictionary<uint, int>();
    private readonly List<ShellRecord> _shells = new List<ShellRecord>();
    private readonly Dictionary<string, int> _shellIndexByHash = new Dictionary<string, int>(StringComparer.Ordinal);
    private readonly List<MaterialRecord> _materials = new List<MaterialRecord>();
    private readonly Dictionary<string, int> _materialIndexByHash = new Dictionary<string, int>(StringComparer.Ordinal);
    private readonly List<SampleRecord> _samples = new List<SampleRecord>();
    private readonly List<FragmentTransform> _globalTransforms = new List<FragmentTransform>();
    private readonly List<uint> _meshesItems = new List<uint>();
    private readonly List<FragmentTransform> _localTransforms = new List<FragmentTransform>();
    private uint _nextLocalId = 1;

    public FragmentsModelBuilder()
    {
        _localTransforms.Add(FragmentTransform.Identity);
    }

    public string ModelGuid { get; set; } = Guid.NewGuid().ToString();

    public string Metadata { get; set; } = "{\"schema\":\"IFC4\",\"origin\":\"Fragments.Core\"}";

    public FragmentTransform Coordinates { get; set; } = FragmentTransform.Identity;

    public FragmentSpatialNode? SpatialStructure { get; set; }

    public IReadOnlyList<FragmentTransform> LocalTransforms => _localTransforms;

    public int ItemCount => _items.Count;

    public int ShellCount => _shells.Count;

    public int SampleCount => _samples.Count;

    public uint AddItem(string category, string? guid = null, IEnumerable<FragmentAttribute>? attributes = null)
    {
        var localId = _nextLocalId++;
        var record = new ItemRecord
        {
            LocalId = localId,
            Category = string.IsNullOrWhiteSpace(category) ? "IFCBUILDINGELEMENTPROXY" : category,
            Guid = string.IsNullOrWhiteSpace(guid) ? null : guid,
            ItemId = -1
        };

        if (attributes != null)
        {
            record.Attributes.AddRange(attributes);
        }

        _localIdIndex[localId] = _items.Count;
        _items.Add(record);
        return localId;
    }

    public void AddAttributes(uint localId, IEnumerable<FragmentAttribute> attributes)
    {
        var item = GetItem(localId);
        item.Attributes.AddRange(attributes);
    }

    public void AddRelation(uint localId, string name, IReadOnlyList<uint> relatedLocalIds)
    {
        GetItem(localId).Relations.Add(new FragmentRelation(name, relatedLocalIds));
    }

    public int AddMaterial(byte r, byte g, byte b, byte a = 255, bool doubleSided = true)
    {
        var hash = MeshHasher.HashMaterial(r, g, b, a, doubleSided);
        if (_materialIndexByHash.TryGetValue(hash, out var existing))
        {
            return existing;
        }

        var index = _materials.Count;
        _materials.Add(new MaterialRecord
        {
            R = r,
            G = g,
            B = b,
            A = a,
            DoubleSided = doubleSided
        });
        _materialIndexByHash[hash] = index;
        return index;
    }

    public int AddTriangleShell(IReadOnlyList<Vec3> points, IReadOnlyList<int> indices, IReadOnlyList<ushort>? faceIds = null)
    {
        if (points == null || points.Count == 0)
        {
            throw new ArgumentException("Shell needs at least one point.", nameof(points));
        }

        if (indices == null || indices.Count < 3 || indices.Count % 3 != 0)
        {
            throw new ArgumentException("Triangle indices must be a non-empty multiple of 3.", nameof(indices));
        }

        var hash = MeshHasher.HashTriangleMesh(points, indices);
        if (_shellIndexByHash.TryGetValue(hash, out var existing))
        {
            return existing;
        }

        var min = new Vec3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vec3(float.MinValue, float.MinValue, float.MinValue);
        var copiedPoints = new Vec3[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            copiedPoints[i] = p;
            min = new Vec3(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
            max = new Vec3(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
        }

        var triangleCount = indices.Count / 3;
        var profiles = new int[triangleCount][];
        var profileFaceIds = new ushort[triangleCount];
        for (var t = 0; t < triangleCount; t++)
        {
            profiles[t] = new[] { indices[t * 3], indices[t * 3 + 1], indices[t * 3 + 2] };
            profileFaceIds[t] = faceIds != null && t < faceIds.Count ? faceIds[t] : (ushort)t;
        }

        var index = _shells.Count;
        _shells.Add(new ShellRecord
        {
            Points = copiedPoints,
            Profiles = profiles,
            FaceIds = profileFaceIds,
            Min = min,
            Max = max
        });
        _shellIndexByHash[hash] = index;
        return index;
    }

    public void AddInstance(uint localId, int shellIndex, int materialIndex, FragmentTransform? worldTransform = null, int localTransformIndex = 0)
    {
        if (shellIndex < 0 || shellIndex >= _shells.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(shellIndex));
        }

        if (materialIndex < 0 || materialIndex >= _materials.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(materialIndex));
        }

        var item = GetItem(localId);
        if (item.ItemId < 0)
        {
            item.ItemId = _globalTransforms.Count;
            _globalTransforms.Add(worldTransform ?? FragmentTransform.Identity);
            _meshesItems.Add((uint)_localIdIndex[localId]);
        }

        _samples.Add(new SampleRecord
        {
            ItemId = (uint)item.ItemId,
            MaterialIndex = (uint)materialIndex,
            RepresentationIndex = (uint)shellIndex,
            LocalTransformIndex = (uint)Math.Max(0, localTransformIndex)
        });
    }

    public byte[] Build(bool compress = true)
    {
        if (_items.Count == 0)
        {
            throw new InvalidOperationException("A Fragments model needs at least one item.");
        }

        if (_materials.Count == 0)
        {
            AddMaterial(180, 180, 180);
        }

        var raw = FragmentsWriter.Write(this);
        return compress ? ZlibCodec.Compress(raw) : raw;
    }

    internal IReadOnlyList<ItemRecord> Items => _items;

    internal IReadOnlyList<ShellRecord> Shells => _shells;

    internal IReadOnlyList<MaterialRecord> Materials => _materials;

    internal IReadOnlyList<SampleRecord> Samples => _samples;

    internal IReadOnlyList<FragmentTransform> GlobalTransforms => _globalTransforms;

    internal IReadOnlyList<uint> MeshesItems => _meshesItems;

    internal IReadOnlyList<FragmentTransform> LocalTransformList => _localTransforms;

    internal uint MaxLocalIdHint => _nextLocalId;

    private ItemRecord GetItem(uint localId)
    {
        if (!_localIdIndex.TryGetValue(localId, out var index))
        {
            throw new ArgumentException("Unknown local id " + localId, nameof(localId));
        }

        return _items[index];
    }

    internal sealed class ItemRecord
    {
        public uint LocalId;
        public string Category = "IFCBUILDINGELEMENTPROXY";
        public string? Guid;
        public int ItemId;
        public List<FragmentAttribute> Attributes { get; } = new List<FragmentAttribute>();
        public List<FragmentRelation> Relations { get; } = new List<FragmentRelation>();
    }

    internal sealed class ShellRecord
    {
        public Vec3[] Points = Array.Empty<Vec3>();
        public int[][] Profiles = Array.Empty<int[]>();
        public ushort[] FaceIds = Array.Empty<ushort>();
        public Vec3 Min;
        public Vec3 Max;
    }

    internal sealed class MaterialRecord
    {
        public byte R;
        public byte G;
        public byte B;
        public byte A = 255;
        public bool DoubleSided = true;
    }

    internal sealed class SampleRecord
    {
        public uint ItemId;
        public uint MaterialIndex;
        public uint RepresentationIndex;
        public uint LocalTransformIndex;
    }
}
