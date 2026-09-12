using System.IO.Compression;

namespace Fragments.Core;

/// <summary>
/// RFC 1950 zlib wrapper around raw DEFLATE. Matches pako.deflate / That Open .frag files.
/// System.IO.Compression.DeflateStream is raw DEFLATE only (RFC 1951) and is not enough.
/// </summary>
public static class ZlibCodec
{
    public static bool IsZlib(byte[] data)
    {
        if (data == null || data.Length < 6)
        {
            return false;
        }

        return data[0] == 0x78 && (data[1] == 0x01 || data[1] == 0x9C || data[1] == 0xDA);
    }

    public static byte[] Compress(byte[] raw)
    {
        if (raw == null)
        {
            throw new ArgumentNullException(nameof(raw));
        }

        using (var output = new MemoryStream())
        {
            output.WriteByte(0x78);
            output.WriteByte(0x9C);
            using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                deflate.Write(raw, 0, raw.Length);
            }

            var adler = Adler32(raw);
            output.WriteByte((byte)((adler >> 24) & 0xFF));
            output.WriteByte((byte)((adler >> 16) & 0xFF));
            output.WriteByte((byte)((adler >> 8) & 0xFF));
            output.WriteByte((byte)(adler & 0xFF));
            return output.ToArray();
        }
    }

    public static byte[] Decompress(byte[] data)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        if (!IsZlib(data))
        {
            return data;
        }

        using (var input = new MemoryStream(data, 2, data.Length - 6))
        using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
        using (var output = new MemoryStream())
        {
            deflate.CopyTo(output);
            return output.ToArray();
        }
    }

    public static uint Adler32(byte[] data)
    {
        const uint mod = 65521;
        uint a = 1;
        uint b = 0;
        for (var i = 0; i < data.Length; i++)
        {
            a = (a + data[i]) % mod;
            b = (b + a) % mod;
        }

        return (b << 16) | a;
    }
}
