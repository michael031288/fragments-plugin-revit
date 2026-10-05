using System.Reflection;
using System.Runtime.InteropServices;

namespace Tessera.Core;

/// <summary>
/// meshoptimizer 0.25 vertex and index codecs, the same sources Tessera's
/// <c>meshopt</c> 0.6.2 crate links. The native library is loaded from beside
/// this assembly, which is where the Revit add-in copies it.
/// </summary>
internal static class Meshopt
{
    static Meshopt()
    {
        NativeLibrary.SetDllImportResolver(typeof(Meshopt).Assembly, Resolve);
    }

    public static byte[] EncodeVertices(ReadOnlySpan<byte> vertices, int vertexCount, int vertexSize)
    {
        if (vertexCount == 0)
        {
            return Array.Empty<byte>();
        }

        var bound = CheckedSize(meshopt_encodeVertexBufferBound((nuint)vertexCount, (nuint)vertexSize));
        var dest = new byte[bound];
        nuint written;
        unsafe
        {
            fixed (byte* destination = dest)
            fixed (byte* source = vertices)
            {
                written = meshopt_encodeVertexBuffer(destination, (nuint)dest.Length, source, (nuint)vertexCount, (nuint)vertexSize);
            }
        }

        if (written == 0 || written > (nuint)dest.Length)
        {
            throw new InvalidOperationException("meshoptimizer rejected a vertex buffer.");
        }

        return dest.AsSpan(0, (int)written).ToArray();
    }

    public static byte[] DecodeVertices(ReadOnlySpan<byte> encoded, int vertexCount, int vertexSize)
    {
        var dest = new byte[vertexCount * vertexSize];
        if (vertexCount == 0)
        {
            return dest;
        }

        int code;
        unsafe
        {
            fixed (byte* destination = dest)
            fixed (byte* source = encoded)
            {
                code = meshopt_decodeVertexBuffer(destination, (nuint)vertexCount, (nuint)vertexSize, source, (nuint)encoded.Length);
            }
        }

        if (code != 0)
        {
            throw new InvalidOperationException("meshoptimizer could not decode a vertex buffer.");
        }

        return dest;
    }

    public static byte[] EncodeIndices(ReadOnlySpan<uint> indices, int vertexCount)
    {
        if (indices.IsEmpty)
        {
            return Array.Empty<byte>();
        }

        var bound = CheckedSize(meshopt_encodeIndexBufferBound((nuint)indices.Length, (nuint)vertexCount));
        var dest = new byte[bound];
        nuint written;
        unsafe
        {
            fixed (byte* destination = dest)
            fixed (uint* source = indices)
            {
                written = meshopt_encodeIndexBuffer(destination, (nuint)dest.Length, source, (nuint)indices.Length);
            }
        }

        if (written == 0 || written > (nuint)dest.Length)
        {
            throw new InvalidOperationException("meshoptimizer rejected an index buffer.");
        }

        return dest.AsSpan(0, (int)written).ToArray();
    }

    public static uint[] DecodeIndices(ReadOnlySpan<byte> encoded, int indexCount)
    {
        var dest = new uint[indexCount];
        if (indexCount == 0)
        {
            return dest;
        }

        int code;
        unsafe
        {
            fixed (uint* destination = dest)
            fixed (byte* source = encoded)
            {
                code = meshopt_decodeIndexBuffer(destination, (nuint)indexCount, 4, source, (nuint)encoded.Length);
            }
        }

        if (code != 0)
        {
            throw new InvalidOperationException("meshoptimizer could not decode an index buffer.");
        }

        return dest;
    }

    private static int CheckedSize(nuint bound)
    {
        if (bound == 0 || bound > int.MaxValue)
        {
            throw new InvalidOperationException("meshoptimizer returned an unusable buffer bound.");
        }

        return (int)bound;
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, "meshoptimizer", StringComparison.Ordinal))
        {
            return IntPtr.Zero;
        }

        return NativeLibrary.Load(FindLibrary());
    }

    internal static string FindLibrary()
    {
        var fileName = OperatingSystem.IsWindows() ? "meshoptimizer.dll" : "libmeshoptimizer.so";
        var start = Path.GetDirectoryName(typeof(Meshopt).Assembly.Location);
        var dir = string.IsNullOrEmpty(start) ? null : new DirectoryInfo(start);
        for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            var beside = Path.Combine(dir.FullName, fileName);
            if (File.Exists(beside))
            {
                return beside;
            }

            var built = Path.Combine(dir.FullName, "native", "meshoptimizer", "build", fileName);
            if (File.Exists(built))
            {
                return built;
            }
        }

        throw new FileNotFoundException(
            "Tessera could not find " + fileName + " next to Tessera.Core.dll. Rebuild the add-in so the meshoptimizer codec is copied beside it.");
    }

    [DllImport("meshoptimizer", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe nuint meshopt_encodeVertexBuffer(byte* buffer, nuint bufferSize, byte* vertices, nuint vertexCount, nuint vertexSize);

    [DllImport("meshoptimizer", CallingConvention = CallingConvention.Cdecl)]
    private static extern nuint meshopt_encodeVertexBufferBound(nuint vertexCount, nuint vertexSize);

    [DllImport("meshoptimizer", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe int meshopt_decodeVertexBuffer(byte* destination, nuint vertexCount, nuint vertexSize, byte* buffer, nuint bufferSize);

    [DllImport("meshoptimizer", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe nuint meshopt_encodeIndexBuffer(byte* buffer, nuint bufferSize, uint* indices, nuint indexCount);

    [DllImport("meshoptimizer", CallingConvention = CallingConvention.Cdecl)]
    private static extern nuint meshopt_encodeIndexBufferBound(nuint indexCount, nuint vertexCount);

    [DllImport("meshoptimizer", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe int meshopt_decodeIndexBuffer(uint* destination, nuint indexCount, nuint indexSize, byte* buffer, nuint bufferSize);
}
