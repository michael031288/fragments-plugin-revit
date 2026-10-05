using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Tessera.Core;
using Xunit;
using ZstdSharp;

namespace Tessera.Core.Tests;

public class TsraFormatTests
{
    [Fact]
    public void EmptyModelIsAVersion06Container()
    {
        var model = new TsraModel { SourceName = "empty.rvt" };
        model.AddEntity("IfcProject", "Empty", TsraModel.StableId("project"), 1, null, TsraModel.NoEntity, null);
        var file = Open(model.ToBytes(new TsraWriteOptions { ZstdLevel = 3 }));

        Assert.Equal(new[] { "META", "STRS", "ENTS", "STYL", "PRXY", "CHNK", "PROT", "PART", "RDAT", "RELS", "PROP", "CIDX", "CANO" }, file.Tags);
        Assert.Equal(1u, file.Flags("META"));
        Assert.Equal(7u, file.Flags("STRS"));
        Assert.Equal(7u, file.Flags("PART"));
        Assert.Equal(6u, file.Flags("STYL"));
        Assert.Equal(6u, file.Flags("PROP"));
        Assert.Equal(1u, file.Flags("RDAT"));
        Assert.Equal(0u, file.Flags("CANO"));
        using var meta = JsonDocument.Parse(file.Section("META"));
        Assert.Equal(10000, meta.RootElement.GetProperty("grid_per_metre").GetInt32());
        Assert.Equal(0, meta.RootElement.GetProperty("counts").GetProperty("triangles").GetInt32());
        Assert.Equal("empty.rvt", meta.RootElement.GetProperty("source").GetProperty("name").GetString());
    }

    [Fact]
    public void CubeRoundTripsOnTheGridWithClassColorAndProperties()
    {
        var model = new TsraModel
        {
            Generator = "Tessera.Revit",
            Schema = "Revit",
            SourceName = "Tower.rvt"
        };
        var project = model.AddEntity("IfcProject", "Tower", TsraModel.StableId("project:Tower"), 1, null, TsraModel.NoEntity, null);
        var wall = model.AddEntity("IfcWall", "Größe", WallId(), 7, "Basic Wall", project, "Concrete");
        model.AddAggregate(project, wall);
        model.AddContainedIn(project, wall);
        model.AddTextProperty(wall, "Revit", "Mark", "W1");
        model.AddRealProperty(wall, "Pset_Quantity", "Height", 3.5, "IFCLENGTHMEASURE");
        var style = model.AddStyle(230, 126, 34, 255);
        AddCube(model, wall, style, 0, 0, 0, 1);
        var bytes = model.ToBytes(new TsraWriteOptions { ZstdLevel = 3 });
        var file = Open(bytes);

        using var meta = JsonDocument.Parse(file.Section("META"));
        var counts = meta.RootElement.GetProperty("counts");
        Assert.Equal(2, counts.GetProperty("entities").GetInt32());
        Assert.Equal(1, counts.GetProperty("elements_with_geometry").GetInt32());
        Assert.Equal(1, counts.GetProperty("parts").GetInt32());
        Assert.Equal(12, counts.GetProperty("triangles").GetInt32());
        Assert.Equal(0, counts.GetProperty("instanced_parts").GetInt32());
        Assert.Equal(new long[] { 5000, 5000, 5000 }, meta.RootElement.GetProperty("origin").EnumerateArray().Select(item => item.GetInt64()).ToArray());

        var strings = ReadStrings(file.Section("STRS"));
        Assert.Equal("", strings[0]);
        Assert.Contains("IfcWall", strings);
        Assert.Contains("Größe", strings);
        Assert.Contains("Concrete", strings);
        Assert.Contains("Mark", strings);
        Assert.Contains("W1", strings);
        Assert.Contains("IFCLENGTHMEASURE", strings);

        var entities = ReadEntities(file.Section("ENTS"), strings);
        Assert.Equal("IfcWall", entities[1].Class);
        Assert.Equal("Größe", entities[1].Name);
        Assert.Equal("Basic Wall", entities[1].Type);
        Assert.Equal(project, entities[1].Level);
        Assert.Equal("Concrete", entities[1].Material);
        Assert.Equal(7u, entities[1].StepId);
        Assert.Equal(WallId(), entities[1].GlobalId);

        var styles = ReadStyles(file.Section("STYL"));
        Assert.Equal(new byte[] { 230, 126, 34, 255 }, styles[0]);

        var word = (1u << 16) | 2u;
        var got = ReadWorldTriangles(file);
        var want = CubeTriangles(0, 0, 0, 10_000, wall, word);
        Assert.Equal(Canon(want), Canon(got));

        var local = ReadCanonical(file);
        Assert.Equal(Canon(CubeTriangles(0, 0, 0, 10_000, 0, 0)), Canon(local));

        var part = ReadParts(file.Section("PART"))[0];
        Assert.Equal(wall, part.Entity);
        Assert.Equal(0u, part.Prototype);
        Assert.Equal(new[] { -5000, -5000, -5000 }, part.Position);
        Assert.Equal(0xC01FFFFFu, part.Rotation[0]);
        Assert.Equal(2_097_151u, part.Rotation[1]);
        Assert.Equal(2_097_151u, part.Rotation[2]);
        Assert.Equal(new[] { 1f, 1f, 1f }, part.Scale);
        Assert.Equal(word, part.Material);
        Assert.Equal(0u, part.MatrixForm);

        var values = ReadPropertyValues(file.Section("PROP"));
        Assert.Contains(values, item => item.Kind == 1 && item.Data == (ulong)strings.IndexOf("W1"));
        Assert.Contains(values, item => item.Kind == 2 && item.Data == (ulong)BitConverter.DoubleToUInt64Bits(3.5));

        var relations = ReadRelations(file.Section("RELS"));
        Assert.Equal(new[] { wall }, relations[1]);
        Assert.Equal(new[] { wall }, relations[3]);

        var broken = bytes.ToArray();
        broken[20] ^= 0xFF;
        Assert.Throws<InvalidDataException>(() => Open(broken));
    }

    [Fact]
    public void NegativeCoordinatesStayOnTheTenthOfAMillimetreGrid()
    {
        var model = new TsraModel();
        var entity = model.AddEntity("IfcSlab", "Slab", new byte[16], 2, null, TsraModel.NoEntity, null);
        model.AddMesh(entity, 0, new double[]
        {
            -1.5, 0.25, 3,
            -1.5, 1.25, 3,
            -0.5, 0.25, 3
        });
        var file = Open(model.ToBytes(new TsraWriteOptions { ZstdLevel = 1 }));
        var got = ReadWorldTriangles(file);
        var expected = Grid(-1.5, 0.25, 3);
        var b = Grid(-1.5, 1.25, 3);
        var c = Grid(-0.5, 0.25, 3);
        Assert.Equal(Canon([new Surface(expected.X, expected.Y, expected.Z, b.X, b.Y, b.Z, c.X, c.Y, c.Z, entity, 0)]), Canon(got));
    }

    private static void AddCube(TsraModel model, uint entity, uint style, double ox, double oy, double oz, double size)
    {
        var corner = new (double X, double Y, double Z)[8];
        for (var k = 0; k < 8; k++)
        {
            corner[k] = (ox + (k & 1) * size, oy + ((k >> 1) & 1) * size, oz + ((k >> 2) & 1) * size);
        }

        int[] triangles =
        [
            0, 2, 1, 1, 2, 3,
            4, 5, 6, 5, 7, 6,
            0, 1, 4, 1, 5, 4,
            2, 6, 3, 3, 6, 7,
            0, 4, 2, 2, 4, 6,
            1, 3, 5, 3, 7, 5
        ];
        var coords = new List<double>(triangles.Length * 3);
        foreach (var index in triangles)
        {
            coords.Add(corner[index].X);
            coords.Add(corner[index].Y);
            coords.Add(corner[index].Z);
        }

        model.AddMesh(entity, style, coords);
    }

    private static List<Surface> CubeTriangles(long ox, long oy, long oz, long size, uint entity, uint material)
    {
        var corner = new (long X, long Y, long Z)[8];
        for (var k = 0; k < 8; k++)
        {
            corner[k] = (ox + (k & 1) * size, oy + ((k >> 1) & 1) * size, oz + ((k >> 2) & 1) * size);
        }

        int[] triangles =
        [
            0, 2, 1, 1, 2, 3,
            4, 5, 6, 5, 7, 6,
            0, 1, 4, 1, 5, 4,
            2, 6, 3, 3, 6, 7,
            0, 4, 2, 2, 4, 6,
            1, 3, 5, 3, 7, 5
        ];
        var result = new List<Surface>();
        for (var i = 0; i < triangles.Length; i += 3)
        {
            var a = corner[triangles[i]];
            var b = corner[triangles[i + 1]];
            var c = corner[triangles[i + 2]];
            result.Add(new Surface(a.X, a.Y, a.Z, b.X, b.Y, b.Z, c.X, c.Y, c.Z, entity, material));
        }

        return result;
    }

    private static (long X, long Y, long Z) Grid(double x, double y, double z)
    {
        return (TsraGeometry.ToGrid(x), TsraGeometry.ToGrid(y), TsraGeometry.ToGrid(z));
    }

    private static byte[] WallId() =>
    [
        0x10, 0x32, 0x54, 0x76, 0x98, 0xBA, 0xDC, 0xFE,
        0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF
    ];

    private static List<string> Canon(IEnumerable<Surface> triangles)
    {
        return triangles
            .Select(item =>
            {
                var tri = new[]
                {
                    (item.Ax, item.Ay, item.Az),
                    (item.Bx, item.By, item.Bz),
                    (item.Cx, item.Cy, item.Cz)
                };
                var start = 0;
                for (var i = 1; i < 3; i++)
                {
                    if (Compare(tri[i], tri[start]) < 0)
                    {
                        start = i;
                    }
                }

                var ordered = new[] { tri[start], tri[(start + 1) % 3], tri[(start + 2) % 3] };
                return $"{ordered[0].Item1},{ordered[0].Item2},{ordered[0].Item3}/{ordered[1].Item1},{ordered[1].Item2},{ordered[1].Item3}/{ordered[2].Item1},{ordered[2].Item2},{ordered[2].Item3}|{item.Entity}|{item.Material}";
            })
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToList();
    }

    private static int Compare((long X, long Y, long Z) a, (long X, long Y, long Z) b)
    {
        var x = a.X.CompareTo(b.X);
        if (x != 0)
        {
            return x;
        }

        var y = a.Y.CompareTo(b.Y);
        return y != 0 ? y : a.Z.CompareTo(b.Z);
    }

    private static Parsed Open(byte[] file)
    {
        if (file.Length < 16 || Encoding.ASCII.GetString(file, 0, 4) != "TSRA")
        {
            throw new InvalidDataException("magic");
        }

        if (BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(4)) != 0 || BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(6)) != 6)
        {
            throw new InvalidDataException("version");
        }

        var count = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(8));
        var directory = file.AsSpan(16, count * 48);
        var hash = file.AsSpan(16 + directory.Length, 16);
        if (!hash.SequenceEqual(TsraHash.Hash16(directory)))
        {
            throw new InvalidDataException("directory hash");
        }

        var parsed = new Parsed();
        for (var i = 0; i < count; i++)
        {
            var entry = directory.Slice(i * 48, 48);
            var tag = Encoding.ASCII.GetString(entry.Slice(0, 4));
            var flags = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(4));
            var offset = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(entry.Slice(8)));
            var storedLength = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(entry.Slice(16)));
            var rawLength = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(entry.Slice(24)));
            var sectionHash = entry.Slice(32, 16).ToArray();
            var stored = file.AsSpan(offset, storedLength);
            var raw = (flags & 4) != 0 ? DecodePages(stored, rawLength) : stored.ToArray();
            if (raw.Length != rawLength || !sectionHash.AsSpan().SequenceEqual(TsraHash.Hash16(raw)))
            {
                throw new InvalidDataException(tag);
            }

            parsed.Add(tag, flags, raw);
        }

        return parsed;
    }

    private static byte[] DecodePages(ReadOnlySpan<byte> stored, int rawLength)
    {
        var reader = new Reader(stored.ToArray());
        var pages = reader.U32();
        reader.U32();
        var rows = new List<(int Stored, int Raw, byte[] Hash)>();
        for (var i = 0; i < pages; i++)
        {
            rows.Add(((int)reader.U32(), (int)reader.U32(), reader.Bytes(16)));
        }

        var output = new List<byte>(rawLength);
        foreach (var row in rows)
        {
            var frame = reader.Bytes(row.Stored);
            if (ContentSize(frame) != (ulong)row.Raw)
            {
                throw new InvalidDataException("zstd content size");
            }

            var raw = Inflate(frame, row.Raw);
            if (!TsraHash.Hash16(raw).AsSpan().SequenceEqual(row.Hash))
            {
                throw new InvalidDataException("page hash");
            }

            output.AddRange(raw);
        }

        if (output.Count != rawLength || reader.Remaining != 0)
        {
            throw new InvalidDataException("pages");
        }

        return output.ToArray();
    }

    private static ulong ContentSize(byte[] frame)
    {
        if (frame.Length < 6 || frame[0] != 0x28 || frame[1] != 0xB5 || frame[2] != 0x2F || frame[3] != 0xFD)
        {
            throw new InvalidDataException("zstd magic");
        }

        var descriptor = frame[4];
        var flag = descriptor >> 6;
        var single = (descriptor & 0x20) != 0;
        var sizeBytes = flag switch
        {
            0 => single ? 1 : 0,
            1 => 2,
            2 => 4,
            _ => 8
        };
        if (sizeBytes == 0)
        {
            throw new InvalidDataException("zstd content size");
        }

        var at = single ? 5 : 6;
        ulong size = 0;
        for (var i = 0; i < sizeBytes; i++)
        {
            size |= (ulong)frame[at + i] << (8 * i);
        }

        return sizeBytes == 2 ? size + 256 : size;
    }

    private static byte[] Inflate(byte[] frame, int rawLength)
    {
        using var decompressor = new Decompressor();
        var raw = decompressor.Unwrap(frame).ToArray();
        if (raw.Length != rawLength)
        {
            throw new InvalidDataException("frame");
        }

        return raw;
    }

    private static List<string> ReadStrings(byte[] raw)
    {
        var reader = new Reader(raw);
        var count = (int)reader.U32();
        var offsets = new uint[count + 1];
        for (var i = 0; i < offsets.Length; i++)
        {
            offsets[i] = reader.U32();
        }

        var bytes = reader.Bytes(reader.Remaining);
        var strings = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            strings.Add(Encoding.UTF8.GetString(bytes, (int)offsets[i], (int)(offsets[i + 1] - offsets[i])));
        }

        return strings;
    }

    private static List<Entity> ReadEntities(byte[] raw, List<string> strings)
    {
        var reader = new Reader(raw);
        var count = (int)reader.U32();
        var classes = new uint[reader.U32()];
        for (var i = 0; i < classes.Length; i++)
        {
            classes[i] = reader.U32();
        }

        var entities = new List<Entity>(count);
        var gids = new byte[count][];
        for (var i = 0; i < count; i++)
        {
            gids[i] = reader.Bytes(16);
        }

        uint step = 0;
        var steps = new uint[count];
        for (var i = 0; i < count; i++)
        {
            step += reader.U32();
            steps[i] = step;
        }

        var classIndex = new ushort[count];
        for (var i = 0; i < count; i++)
        {
            classIndex[i] = reader.U16();
        }

        reader.Align(4);
        var names = reader.U32s(count);
        var types = reader.U32s(count);
        var levels = reader.U32s(count);
        var materials = reader.U32s(count);
        reader.U32s(count);
        for (var i = 0; i < count; i++)
        {
            entities.Add(new Entity(
                strings[(int)classes[classIndex[i]]],
                strings[(int)names[i]],
                types[i] == uint.MaxValue ? "" : strings[(int)types[i]],
                levels[i],
                materials[i] == 0 ? "" : strings[(int)materials[i]],
                steps[i],
                gids[i]));
        }

        return entities;
    }

    private static List<byte[]> ReadStyles(byte[] raw)
    {
        var reader = new Reader(raw);
        var count = (int)reader.U32();
        var styles = new List<byte[]>();
        for (var i = 0; i < count; i++)
        {
            styles.Add(reader.Bytes(4));
        }

        return styles;
    }

    private static List<Part> ReadParts(byte[] raw)
    {
        var reader = new Reader(raw);
        var count = (int)reader.U32();
        var matrices = (int)reader.U32();
        var parts = new List<Part>();
        for (var i = 0; i < count; i++)
        {
            var entity = reader.U32();
            var prototype = reader.U32();
            var position = new[] { reader.I32(), reader.I32(), reader.I32() };
            var rotation = new[] { reader.U32(), reader.U32(), reader.U32() };
            var scale = new[] { reader.F32(), reader.F32(), reader.F32() };
            var flags = reader.U32();
            parts.Add(new Part(entity, prototype, position, rotation, scale, flags & 1u, flags & ~1u));
        }

        Assert.Equal(matrices * 36, reader.Remaining);
        return parts;
    }

    private static List<Surface> ReadWorldTriangles(Parsed file)
    {
        var index = new Reader(file.Section("CHNK"));
        var count = (int)index.U32();
        index.U32();
        var data = file.Section("RDAT");
        var triangles = new List<Surface>();
        for (var i = 0; i < count; i++)
        {
            var offset = (int)index.U64();
            var stored = (int)index.U32();
            var rawLength = (int)index.U32();
            Assert.Equal(0u, index.U32());
            index.U32();
            index.U32();
            index.U32();
            var origin = (index.I64(), index.I64(), index.I64());
            index.I32();
            index.I32();
            index.I32();
            index.I32();
            index.I32();
            index.I32();
            var hash = index.Bytes(16);
            var frame = data.AsSpan(offset, stored).ToArray();
            Assert.Equal((ulong)rawLength, ContentSize(frame));
            var raw = Inflate(frame, rawLength);
            Assert.True(hash.AsSpan().SequenceEqual(TsraHash.Hash16(raw)));
            triangles.AddRange(DecodeChunk(raw, origin));
        }

        return triangles;
    }

    private static List<Surface> ReadCanonical(Parsed file)
    {
        var index = new Reader(file.Section("CIDX"));
        var chunks = (int)index.U32();
        var blobs = (int)index.U32();
        var rows = new List<(int Offset, int Stored, int Raw)>();
        for (var i = 0; i < chunks; i++)
        {
            var offset = (int)index.U64();
            var stored = (int)index.U32();
            var raw = (int)index.U32();
            index.Bytes(16);
            rows.Add((offset, stored, raw));
        }

        var data = file.Section("CANO");
        var inflated = rows.Select(row =>
        {
            var frame = data.AsSpan(row.Offset, row.Stored).ToArray();
            Assert.Equal((ulong)row.Raw, ContentSize(frame));
            return Inflate(frame, row.Raw);
        }).ToArray();
        var triangles = new List<Surface>();
        for (var i = 0; i < blobs; i++)
        {
            var chunk = (int)index.U32();
            var offset = (int)index.U32();
            var length = (int)index.U32();
            var vertices = (int)index.U32();
            var triangleCount = (int)index.U32();
            index.U32();
            index.Bytes(8);
            var blob = inflated[chunk].AsSpan(offset, length).ToArray();
            var reader = new Reader(blob);
            Assert.Equal(vertices, (int)reader.U32());
            Assert.Equal(triangleCount, (int)reader.U32());
            var vlen = (int)reader.U32();
            var ilen = (int)reader.U32();
            var vertexBytes = Meshopt.DecodeVertices(reader.Bytes(vlen), vertices, 12);
            var indices = Meshopt.DecodeIndices(reader.Bytes(ilen), triangleCount * 3);
            (long X, long Y, long Z) At(int vertex)
            {
                var at = vertex * 12;
                return (
                    BinaryPrimitives.ReadInt32LittleEndian(vertexBytes.AsSpan(at)),
                    BinaryPrimitives.ReadInt32LittleEndian(vertexBytes.AsSpan(at + 4)),
                    BinaryPrimitives.ReadInt32LittleEndian(vertexBytes.AsSpan(at + 8)));
            }

            for (var t = 0; t < indices.Length; t += 3)
            {
                var a = At((int)indices[t]);
                var b = At((int)indices[t + 1]);
                var c = At((int)indices[t + 2]);
                triangles.Add(new Surface(a.X, a.Y, a.Z, b.X, b.Y, b.Z, c.X, c.Y, c.Z, 0, 0));
            }
        }

        return triangles;
    }

    private static List<Surface> DecodeChunk(byte[] raw, (long X, long Y, long Z) origin)
    {
        var reader = new Reader(raw);
        Assert.Equal("TCHK", Encoding.ASCII.GetString(reader.Bytes(4)));
        var flags = reader.U32();
        var meshlets = (int)reader.U32();
        var vertexCount = (int)reader.U32();
        var triangleCount = (int)reader.U32();
        var elementCount = (int)reader.U32();
        var vertexBytes = (int)reader.U32();
        var triangleBytes = (int)reader.U32();
        var records = new List<Meshlet>();
        for (var i = 0; i < meshlets; i++)
        {
            var record = new Meshlet
            {
                FirstVertex = (int)reader.U32(),
                FirstTriangle = (int)reader.U32(),
                VertexCount = reader.U8(),
                TriangleCount = reader.U8(),
                ElementCount = reader.U8(),
                Shift = reader.U8(),
                FirstElement = (int)reader.U32(),
                Origin = (reader.I32(), reader.I32(), reader.I32())
            };
            reader.F32();
            reader.F32();
            reader.F32();
            reader.F32();
            reader.Bytes(4);
            records.Add(record);
        }

        var elements = reader.U32s(elementCount);
        var materials = (flags & 2) != 0 ? reader.U32s(elementCount) : new uint[elementCount];
        var decoded = Meshopt.DecodeVertices(reader.Bytes(vertexBytes), vertexCount, 8);
        var corners = DecodeTriangles(records, reader.Bytes(triangleBytes), triangleCount);
        var result = new List<Surface>();
        foreach (var record in records)
        {
            for (var t = 0; t < record.TriangleCount; t++)
            {
                var tri = corners[record.FirstTriangle + t];
                (long X, long Y, long Z) At(byte local)
                {
                    var at = (record.FirstVertex + local) * 8;
                    return (
                        origin.X + record.Origin.X + ((long)BinaryPrimitives.ReadUInt16LittleEndian(decoded.AsSpan(at)) << record.Shift),
                        origin.Y + record.Origin.Y + ((long)BinaryPrimitives.ReadUInt16LittleEndian(decoded.AsSpan(at + 2)) << record.Shift),
                        origin.Z + record.Origin.Z + ((long)BinaryPrimitives.ReadUInt16LittleEndian(decoded.AsSpan(at + 4)) << record.Shift));
                }

                var entity = record.ElementCount == 0 ? uint.MaxValue : elements[record.FirstElement + tri.E];
                var material = record.ElementCount == 0 ? 0 : materials[record.FirstElement + tri.E];
                var a = At(tri.A);
                var b = At(tri.B);
                var c = At(tri.C);
                result.Add(new Surface(a.X, a.Y, a.Z, b.X, b.Y, b.Z, c.X, c.Y, c.Z, entity, material));
            }
        }

        return result;
    }

    private static (byte A, byte B, byte C, byte E)[] DecodeTriangles(List<Meshlet> meshlets, byte[] stream, int triangleCount)
    {
        var triangles = new (byte A, byte B, byte C, byte E)[triangleCount];
        var at = 0;
        foreach (var meshlet in meshlets)
        {
            byte high = 0;
            for (var i = 0; i < meshlet.TriangleCount; i++)
            {
                var corner = at * 3;
                byte Corner(int k)
                {
                    var value = stream[corner + k];
                    var index = (byte)(high - value);
                    if (value == 0)
                    {
                        high++;
                    }

                    return index;
                }

                triangles[at] = (Corner(0), Corner(1), Corner(2), 0);
                at++;
            }
        }

        var runs = triangleCount * 3;
        foreach (var meshlet in meshlets.Where(item => item.ElementCount > 1))
        {
            var k = 0;
            while (k < meshlet.TriangleCount)
            {
                var n = stream[runs++];
                var element = stream[runs++];
                for (var i = 0; i < n; i++)
                {
                    var tri = triangles[meshlet.FirstTriangle + k + i];
                    triangles[meshlet.FirstTriangle + k + i] = (tri.A, tri.B, tri.C, element);
                }

                k += n;
            }
        }

        return triangles;
    }

    private static List<(byte Kind, ulong Data)> ReadPropertyValues(byte[] raw)
    {
        var reader = new Reader(raw);
        var count = (int)reader.U32();
        var kinds = reader.Bytes(count);
        reader.Align(4);
        var values = new List<(byte, ulong)>(count);
        for (var i = 0; i < count; i++)
        {
            values.Add((kinds[i], reader.U64()));
        }

        return values;
    }

    private static Dictionary<uint, uint[]> ReadRelations(byte[] raw)
    {
        var reader = new Reader(raw);
        var count = (int)reader.U32();
        var relations = new Dictionary<uint, uint[]>();
        for (var i = 0; i < count; i++)
        {
            var kind = reader.U32();
            var keys = (int)reader.U32();
            var valueCount = (int)reader.U32();
            var keyValues = reader.U32s(keys);
            var counts = reader.U32s(keys);
            var values = reader.U32s(valueCount);
            var flat = new List<uint>();
            var at = 0;
            for (var k = 0; k < keys; k++)
            {
                for (var n = 0; n < counts[k]; n++)
                {
                    flat.Add(keyValues[k]);
                    flat.Add(values[at++]);
                }
            }

            relations[kind] = flat.Where((_, index) => index % 2 == 1).ToArray();
        }

        return relations;
    }

    private sealed class Parsed
    {
        private readonly List<(string Tag, uint Flags, byte[] Raw)> _sections = new();

        public IReadOnlyList<string> Tags => _sections.Select(section => section.Tag).ToArray();

        public void Add(string tag, uint flags, byte[] raw) => _sections.Add((tag, flags, raw));

        public uint Flags(string tag) => _sections.First(section => section.Tag == tag).Flags;

        public byte[] Section(string tag) => _sections.First(section => section.Tag == tag).Raw;
    }

    private sealed class Meshlet
    {
        public int FirstVertex;
        public int FirstTriangle;
        public byte VertexCount;
        public byte TriangleCount;
        public byte ElementCount;
        public byte Shift;
        public int FirstElement;
        public (int X, int Y, int Z) Origin;
    }

    private sealed record Surface(long Ax, long Ay, long Az, long Bx, long By, long Bz, long Cx, long Cy, long Cz, uint Entity, uint Material);

    private sealed record Entity(string Class, string Name, string Type, uint Level, string Material, uint StepId, byte[] GlobalId);

    private sealed record Part(uint Entity, uint Prototype, int[] Position, uint[] Rotation, float[] Scale, uint MatrixForm, uint Material);

    private sealed class Reader
    {
        private readonly ReadOnlyMemory<byte> _data;
        private int _at;

        public Reader(ReadOnlyMemory<byte> data) => _data = data;

        public Reader(byte[] data) => _data = data;

        public int Remaining => _data.Length - _at;

        public byte U8() => Bytes(1)[0];

        public ushort U16()
        {
            var value = BinaryPrimitives.ReadUInt16LittleEndian(_data.Span.Slice(_at));
            _at += 2;
            return value;
        }

        public uint U32()
        {
            var value = BinaryPrimitives.ReadUInt32LittleEndian(_data.Span.Slice(_at));
            _at += 4;
            return value;
        }

        public uint[] U32s(int count)
        {
            var values = new uint[count];
            for (var i = 0; i < count; i++)
            {
                values[i] = U32();
            }

            return values;
        }

        public ulong U64()
        {
            var value = BinaryPrimitives.ReadUInt64LittleEndian(_data.Span.Slice(_at));
            _at += 8;
            return value;
        }

        public int I32()
        {
            var value = BinaryPrimitives.ReadInt32LittleEndian(_data.Span.Slice(_at));
            _at += 4;
            return value;
        }

        public long I64()
        {
            var value = BinaryPrimitives.ReadInt64LittleEndian(_data.Span.Slice(_at));
            _at += 8;
            return value;
        }

        public float F32()
        {
            var value = BinaryPrimitives.ReadSingleLittleEndian(_data.Span.Slice(_at));
            _at += 4;
            return value;
        }

        public byte[] Bytes(int count)
        {
            var bytes = _data.Span.Slice(_at, count).ToArray();
            _at += count;
            return bytes;
        }

        public void Align(int n)
        {
            var pad = (n - (_at % n)) % n;
            _at += pad;
        }
    }
}
