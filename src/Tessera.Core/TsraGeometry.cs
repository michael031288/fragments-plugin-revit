using System.Buffers.Binary;

namespace Tessera.Core;

internal sealed class Cluster
{
    public readonly List<long> X = new();
    public readonly List<long> Y = new();
    public readonly List<long> Z = new();
    public readonly List<byte> A = new();
    public readonly List<byte> B = new();
    public readonly List<byte> C = new();
    public readonly List<uint> Elements = new();
    public double Cx;
    public double Cy;
    public double Cz;
    public float Radius;
    public readonly sbyte[] Cone = [0, 0, 0, 127];
    public uint SlotEntity;
    public uint SlotMaterial;
    public int TriangleCount => A.Count;

    public void GetBounds(out long minX, out long minY, out long minZ, out long maxX, out long maxY, out long maxZ)
    {
        minX = minY = minZ = long.MaxValue;
        maxX = maxY = maxZ = long.MinValue;
        for (var i = 0; i < X.Count; i++)
        {
            minX = Math.Min(minX, X[i]);
            minY = Math.Min(minY, Y[i]);
            minZ = Math.Min(minZ, Z[i]);
            maxX = Math.Max(maxX, X[i]);
            maxY = Math.Max(maxY, Y[i]);
            maxZ = Math.Max(maxZ, Z[i]);
        }
    }
}

internal sealed class BuiltChunk
{
    public byte[] Raw { get; init; } = Array.Empty<byte>();
    public long OriginX { get; init; }
    public long OriginY { get; init; }
    public long OriginZ { get; init; }
    public int MinX { get; init; }
    public int MinY { get; init; }
    public int MinZ { get; init; }
    public int MaxX { get; init; }
    public int MaxY { get; init; }
    public int MaxZ { get; init; }
    public uint Meshlets { get; init; }
    public uint Triangles { get; init; }
    public uint Vertices { get; init; }
}

internal static class TsraGeometry
{
    public const int MaxVertices = 64;
    public const int MaxTriangles = 124;
    public const uint None = uint.MaxValue;
    public const uint ChunkWorld = 0;
    private const uint FlagMaterials = 1u << 1;
    private const double SqrtHalf = 0.70710678118654757;

    public static List<Cluster> Clusterize(long[] x, long[] y, long[] z, int[] triangles, uint element)
    {
        var clusters = new List<Cluster>();
        if (triangles.Length == 0 || x.Length == 0)
        {
            return clusters;
        }

        var stampOf = new uint[x.Length];
        Array.Fill(stampOf, uint.MaxValue);
        var localOf = new byte[x.Length];
        uint stamp = 0;
        var used = new List<int>();
        var current = new Cluster();

        void Flush()
        {
            if (current.TriangleCount == 0)
            {
                return;
            }

            current.GetBounds(out var minX, out var minY, out var minZ, out var maxX, out var maxY, out var maxZ);
            current.Cx = (minX + (double)maxX) / 2.0;
            current.Cy = (minY + (double)maxY) / 2.0;
            current.Cz = (minZ + (double)maxZ) / 2.0;
            var rx = (maxX - minX) / 2.0;
            var ry = (maxY - minY) / 2.0;
            var rz = (maxZ - minZ) / 2.0;
            current.Radius = (float)Math.Sqrt(rx * rx + ry * ry + rz * rz) + 1f;
            clusters.Add(current);
            current = new Cluster();
            used.Clear();
        }

        for (var t = 0; t < triangles.Length; t += 3)
        {
            var i0 = triangles[t];
            var i1 = triangles[t + 1];
            var i2 = triangles[t + 2];
            var fresh = (stampOf[i0] != stamp ? 1 : 0) + (stampOf[i1] != stamp ? 1 : 0) + (stampOf[i2] != stamp ? 1 : 0);
            if (current.TriangleCount == MaxTriangles || used.Count + fresh > MaxVertices)
            {
                Flush();
                stamp++;
            }

            current.A.Add(Local(i0));
            current.B.Add(Local(i1));
            current.C.Add(Local(i2));
            current.Elements.Add(element);
        }

        Flush();
        return clusters;

        byte Local(int vertex)
        {
            if (stampOf[vertex] != stamp)
            {
                stampOf[vertex] = stamp;
                localOf[vertex] = (byte)used.Count;
                used.Add(vertex);
                current.X.Add(x[vertex]);
                current.Y.Add(y[vertex]);
                current.Z.Add(z[vertex]);
            }

            return localOf[vertex];
        }
    }

    public static List<List<Cluster>> Split(IReadOnlyList<Cluster> clusters, int budget)
    {
        var groups = new List<List<Cluster>>();
        var count = 0;
        long minX = 0, minY = 0, minZ = 0, maxX = 0, maxY = 0, maxZ = 0;
        foreach (var cluster in clusters)
        {
            cluster.GetBounds(out var cMinX, out var cMinY, out var cMinZ, out var cMaxX, out var cMaxY, out var cMaxZ);
            var wide = groups.Count > 0 && count > 0 && Exceeds(minX, minY, minZ, maxX, maxY, maxZ, cMinX, cMinY, cMinZ, cMaxX, cMaxY, cMaxZ);
            if (groups.Count == 0 || count + cluster.TriangleCount > budget || wide)
            {
                groups.Add(new List<Cluster>());
                count = 0;
                minX = cMinX;
                minY = cMinY;
                minZ = cMinZ;
                maxX = cMaxX;
                maxY = cMaxY;
                maxZ = cMaxZ;
            }
            else
            {
                minX = Math.Min(minX, cMinX);
                minY = Math.Min(minY, cMinY);
                minZ = Math.Min(minZ, cMinZ);
                maxX = Math.Max(maxX, cMaxX);
                maxY = Math.Max(maxY, cMaxY);
                maxZ = Math.Max(maxZ, cMaxZ);
            }

            count += cluster.TriangleCount;
            groups[^1].Add(cluster);
        }

        return groups;
    }

    public static BuiltChunk EncodeChunk(IReadOnlyList<Cluster> clusters, IReadOnlyList<(uint Entity, uint Material)> slots)
    {
        long originX = long.MaxValue, originY = long.MaxValue, originZ = long.MaxValue;
        foreach (var cluster in clusters)
        {
            for (var i = 0; i < cluster.X.Count; i++)
            {
                originX = Math.Min(originX, cluster.X[i]);
                originY = Math.Min(originY, cluster.Y[i]);
                originZ = Math.Min(originZ, cluster.Z[i]);
            }
        }

        var meshlets = new List<MeshletRecord>();
        var elements = new List<uint>();
        var materials = new List<uint>();
        var vertices = new List<(ushort X, ushort Y, ushort Z)>();
        var triangles = new List<(byte A, byte B, byte C, byte E)>();
        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var minZ = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;
        var maxZ = int.MinValue;

        foreach (var cluster in clusters)
        {
            long loX = long.MaxValue, loY = long.MaxValue, loZ = long.MaxValue;
            long hiX = long.MinValue, hiY = long.MinValue, hiZ = long.MinValue;
            for (var i = 0; i < cluster.X.Count; i++)
            {
                loX = Math.Min(loX, cluster.X[i]);
                loY = Math.Min(loY, cluster.Y[i]);
                loZ = Math.Min(loZ, cluster.Z[i]);
                hiX = Math.Max(hiX, cluster.X[i]);
                hiY = Math.Max(hiY, cluster.Y[i]);
                hiZ = Math.Max(hiZ, cluster.Z[i]);
            }

            var shift = 0;
            long alignedX, alignedY, alignedZ, half;
            while (true)
            {
                var step = 1L << shift;
                alignedX = DivEuclid(loX, step) * step;
                alignedY = DivEuclid(loY, step) * step;
                alignedZ = DivEuclid(loZ, step) * step;
                half = step >> 1;
                if (Fits(hiX, alignedX, half, shift) && Fits(hiY, alignedY, half, shift) && Fits(hiZ, alignedZ, half, shift))
                {
                    break;
                }

                shift++;
                if (shift > 40)
                {
                    throw new InvalidOperationException("A Tessera meshlet is wider than the grid can quantise.");
                }
            }

            var firstVertex = (uint)vertices.Count;
            for (var i = 0; i < cluster.X.Count; i++)
            {
                var qx = Quantise(cluster.X[i], alignedX, half, shift);
                var qy = Quantise(cluster.Y[i], alignedY, half, shift);
                var qz = Quantise(cluster.Z[i], alignedZ, half, shift);
                Include(alignedX + ((long)qx << shift), originX, ref minX, ref maxX);
                Include(alignedY + ((long)qy << shift), originY, ref minY, ref maxY);
                Include(alignedZ + ((long)qz << shift), originZ, ref minZ, ref maxZ);
                vertices.Add((qx, qy, qz));
            }

            var table = new List<uint>();
            var firstTriangle = (uint)triangles.Count;
            for (var i = 0; i < cluster.TriangleCount; i++)
            {
                var element = cluster.Elements[i];
                var local = 0;
                if (element != None)
                {
                    local = table.IndexOf(element);
                    if (local < 0)
                    {
                        table.Add(element);
                        local = table.Count - 1;
                    }
                }

                triangles.Add((cluster.A[i], cluster.B[i], cluster.C[i], (byte)local));
            }

            var firstElement = (uint)elements.Count;
            foreach (var slot in table)
            {
                if (slot >= (uint)slots.Count)
                {
                    throw new InvalidOperationException("A Tessera meshlet referenced a missing element.");
                }

                elements.Add(slots[(int)slot].Entity);
                materials.Add(slots[(int)slot].Material);
            }

            meshlets.Add(new MeshletRecord
            {
                FirstVertex = firstVertex,
                FirstTriangle = firstTriangle,
                VertexCount = (byte)cluster.X.Count,
                TriangleCount = (byte)cluster.TriangleCount,
                ElementCount = (byte)table.Count,
                Shift = (byte)shift,
                FirstElement = firstElement,
                OriginX = Relative(alignedX, originX),
                OriginY = Relative(alignedY, originY),
                OriginZ = Relative(alignedZ, originZ),
                SphereX = (float)(cluster.Cx - originX),
                SphereY = (float)(cluster.Cy - originY),
                SphereZ = (float)(cluster.Cz - originZ),
                Radius = cluster.Radius,
                Cone = cluster.Cone
            });
        }

        var vertexBytes = new byte[vertices.Count * 8];
        for (var i = 0; i < vertices.Count; i++)
        {
            var at = i * 8;
            BinaryPrimitives.WriteUInt16LittleEndian(vertexBytes.AsSpan(at), vertices[i].X);
            BinaryPrimitives.WriteUInt16LittleEndian(vertexBytes.AsSpan(at + 2), vertices[i].Y);
            BinaryPrimitives.WriteUInt16LittleEndian(vertexBytes.AsSpan(at + 4), vertices[i].Z);
        }

        var vertexStream = Meshopt.EncodeVertices(vertexBytes, vertices.Count, 8);
        var triangleStream = EncodeTriangles(meshlets, triangles);
        var output = new OutBuf();
        output.Bytes("TCHK"u8);
        output.U32(FlagMaterials);
        output.U32((uint)meshlets.Count);
        output.U32((uint)vertices.Count);
        output.U32((uint)triangles.Count);
        output.U32((uint)elements.Count);
        output.U32((uint)vertexStream.Length);
        output.U32((uint)triangleStream.Length);
        foreach (var meshlet in meshlets)
        {
            output.U32(meshlet.FirstVertex);
            output.U32(meshlet.FirstTriangle);
            output.U8(meshlet.VertexCount);
            output.U8(meshlet.TriangleCount);
            output.U8(meshlet.ElementCount);
            output.U8(meshlet.Shift);
            output.U32(meshlet.FirstElement);
            output.I32(meshlet.OriginX);
            output.I32(meshlet.OriginY);
            output.I32(meshlet.OriginZ);
            output.F32(meshlet.SphereX);
            output.F32(meshlet.SphereY);
            output.F32(meshlet.SphereZ);
            output.F32(meshlet.Radius);
            output.U8((byte)meshlet.Cone[0]);
            output.U8((byte)meshlet.Cone[1]);
            output.U8((byte)meshlet.Cone[2]);
            output.U8((byte)meshlet.Cone[3]);
        }

        foreach (var element in elements)
        {
            output.U32(element);
        }

        foreach (var material in materials)
        {
            output.U32(material);
        }

        output.Bytes(vertexStream);
        output.Bytes(triangleStream);
        if (vertices.Count == 0)
        {
            minX = minY = minZ = maxX = maxY = maxZ = 0;
        }

        return new BuiltChunk
        {
            Raw = output.ToArray(),
            OriginX = originX,
            OriginY = originY,
            OriginZ = originZ,
            MinX = minX,
            MinY = minY,
            MinZ = minZ,
            MaxX = maxX,
            MaxY = maxY,
            MaxZ = maxZ,
            Meshlets = (uint)meshlets.Count,
            Triangles = (uint)triangles.Count,
            Vertices = (uint)vertices.Count
        };
    }

    public static byte[] EncodeBlob(ReadOnlySpan<int> xyz, int vertexCount, ReadOnlySpan<uint> indices, int triangleCount)
    {
        var bytes = new byte[vertexCount * 12];
        for (var i = 0; i < vertexCount * 3; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 4), xyz[i]);
        }

        var vertexStream = Meshopt.EncodeVertices(bytes, vertexCount, 12);
        var indexStream = Meshopt.EncodeIndices(indices, vertexCount);
        var output = new OutBuf();
        output.U32((uint)vertexCount);
        output.U32((uint)triangleCount);
        output.U32((uint)vertexStream.Length);
        output.U32((uint)indexStream.Length);
        output.Bytes(vertexStream);
        output.Bytes(indexStream);
        return output.ToArray();
    }

    public static bool IsWatertight(ReadOnlySpan<int> xyz, int vertexCount, ReadOnlySpan<int> triangles)
    {
        if (triangles.Length == 0 || vertexCount == 0)
        {
            return false;
        }

        var weld = new Dictionary<(int X, int Y, int Z), uint>();
        var ids = new uint[vertexCount];
        for (var i = 0; i < vertexCount; i++)
        {
            var key = (xyz[i * 3], xyz[i * 3 + 1], xyz[i * 3 + 2]);
            if (!weld.TryGetValue(key, out var id))
            {
                id = (uint)weld.Count;
                weld[key] = id;
            }

            ids[i] = id;
        }

        var edges = new Dictionary<(uint A, uint B), int>();
        for (var t = 0; t < triangles.Length; t += 3)
        {
            var v0 = ids[triangles[t]];
            var v1 = ids[triangles[t + 1]];
            var v2 = ids[triangles[t + 2]];
            if (v0 == v1 || v1 == v2 || v0 == v2)
            {
                continue;
            }

            Count(edges, v0, v1);
            Count(edges, v1, v2);
            Count(edges, v2, v0);
        }

        if (edges.Count == 0)
        {
            return false;
        }

        foreach (var (edge, count) in edges)
        {
            if (count != 1 || !edges.TryGetValue((edge.B, edge.A), out var reverse) || reverse != 1)
            {
                return false;
            }
        }

        var ox = (double)xyz[0];
        var oy = (double)xyz[1];
        var oz = (double)xyz[2];
        double volume = 0;
        for (var t = 0; t < triangles.Length; t += 3)
        {
            volume += Det(
                xyz[triangles[t] * 3] - ox, xyz[triangles[t] * 3 + 1] - oy, xyz[triangles[t] * 3 + 2] - oz,
                xyz[triangles[t + 1] * 3] - ox, xyz[triangles[t + 1] * 3 + 1] - oy, xyz[triangles[t + 1] * 3 + 2] - oz,
                xyz[triangles[t + 2] * 3] - ox, xyz[triangles[t + 2] * 3 + 1] - oy, xyz[triangles[t + 2] * 3 + 2] - oz);
        }

        return volume > 0;
    }

    public static (uint Word0, uint Word1, uint Word2) IdentityRotation()
    {
        return PackQuaternion(0, 0, 0, 1);
    }

    public static long ToGrid(double metres)
    {
        var rounded = Math.Round(metres * TsraModel.GridPerMetre, MidpointRounding.AwayFromZero);
        if (rounded > long.MaxValue || rounded < long.MinValue)
        {
            throw new InvalidOperationException("A coordinate is outside the Tessera grid.");
        }

        return (long)rounded;
    }

    private static (uint Word0, uint Word1, uint Word2) PackQuaternion(double x, double y, double z, double w)
    {
        var q = new[] { x, y, z, w };
        var largest = 0;
        for (var i = 1; i < 4; i++)
        {
            if (Math.Abs(q[i]) > Math.Abs(q[largest]))
            {
                largest = i;
            }
        }

        var sign = q[largest] < 0 ? -1.0 : 1.0;
        const double half = (1 << 21) - 1;
        var components = new uint[3];
        var at = 0;
        for (var i = 0; i < 4; i++)
        {
            if (i == largest)
            {
                continue;
            }

            var value = Math.Clamp(q[i] * sign / SqrtHalf, -1.0, 1.0) * half;
            components[at++] = (uint)Math.Round(value, MidpointRounding.AwayFromZero) + (uint)half;
        }

        return (((uint)largest << 30) | components[0], components[1], components[2]);
    }

    private static byte[] EncodeTriangles(List<MeshletRecord> meshlets, List<(byte A, byte B, byte C, byte E)> triangles)
    {
        var corners = new List<byte>(triangles.Count * 3);
        var runs = new List<byte>();
        foreach (var meshlet in meshlets)
        {
            byte high = 0;
            for (var i = 0; i < meshlet.TriangleCount; i++)
            {
                var tri = triangles[(int)meshlet.FirstTriangle + i];
                foreach (var index in new[] { tri.A, tri.B, tri.C })
                {
                    var corner = (byte)(high - index);
                    corners.Add(corner);
                    if (corner == 0)
                    {
                        high++;
                    }
                }
            }

            if (meshlet.ElementCount > 1)
            {
                var k = 0;
                while (k < meshlet.TriangleCount)
                {
                    var element = triangles[(int)meshlet.FirstTriangle + k].E;
                    var n = 1;
                    while (k + n < meshlet.TriangleCount && triangles[(int)meshlet.FirstTriangle + k + n].E == element)
                    {
                        n++;
                    }

                    runs.Add((byte)n);
                    runs.Add(element);
                    k += n;
                }
            }
        }

        var stream = new byte[corners.Count + runs.Count];
        for (var i = 0; i < corners.Count; i++)
        {
            stream[i] = corners[i];
        }

        for (var i = 0; i < runs.Count; i++)
        {
            stream[corners.Count + i] = runs[i];
        }

        return stream;
    }

    private static bool Exceeds(
        long minX, long minY, long minZ, long maxX, long maxY, long maxZ,
        long cMinX, long cMinY, long cMinZ, long cMaxX, long cMaxY, long cMaxZ)
    {
        return Span(Math.Min(minX, cMinX), Math.Max(maxX, cMaxX))
            || Span(Math.Min(minY, cMinY), Math.Max(maxY, cMaxY))
            || Span(Math.Min(minZ, cMinZ), Math.Max(maxZ, cMaxZ));
    }

    private static bool Span(long min, long max) => max - min > int.MaxValue;

    private static bool Fits(long hi, long aligned, long half, int shift)
    {
        var span = hi - aligned + half;
        return span >= 0 && (span >> shift) <= ushort.MaxValue;
    }

    private static ushort Quantise(long position, long aligned, long half, int shift)
    {
        var value = (position - aligned + half) >> shift;
        if (value < 0 || value > ushort.MaxValue)
        {
            throw new InvalidOperationException("A Tessera vertex fell outside its meshlet grid.");
        }

        return (ushort)value;
    }

    private static void Include(long reconstructed, long origin, ref int min, ref int max)
    {
        var relative = Relative(reconstructed, origin);
        min = Math.Min(min, relative);
        max = Math.Max(max, relative);
    }

    private static int Relative(long value, long origin)
    {
        var relative = value - origin;
        if (relative < int.MinValue || relative > int.MaxValue)
        {
            throw new InvalidOperationException("A Tessera chunk is wider than 214 km.");
        }

        return (int)relative;
    }

    private static long DivEuclid(long value, long divisor)
    {
        var quotient = value / divisor;
        if (value % divisor < 0)
        {
            quotient--;
        }

        return quotient;
    }

    private static void Count(Dictionary<(uint A, uint B), int> edges, uint a, uint b)
    {
        edges.TryGetValue((a, b), out var count);
        edges[(a, b)] = count + 1;
    }

    private static double Det(double ax, double ay, double az, double bx, double by, double bz, double cx, double cy, double cz)
    {
        return ax * (by * cz - bz * cy) - ay * (bx * cz - bz * cx) + az * (bx * cy - by * cx);
    }

    private sealed class MeshletRecord
    {
        public uint FirstVertex;
        public uint FirstTriangle;
        public byte VertexCount;
        public byte TriangleCount;
        public byte ElementCount;
        public byte Shift;
        public uint FirstElement;
        public int OriginX;
        public int OriginY;
        public int OriginZ;
        public float SphereX;
        public float SphereY;
        public float SphereZ;
        public float Radius;
        public sbyte[] Cone = [0, 0, 0, 127];
    }
}
