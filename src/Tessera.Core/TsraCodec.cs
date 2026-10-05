using System.Text;
using Blake3;
using ZstdSharp;

namespace Tessera.Core;

internal static class TsraHash
{
    public static byte[] Hash16(ReadOnlySpan<byte> data)
    {
        return Hasher.Hash(data).AsSpan().Slice(0, 16).ToArray();
    }

    public static string Hex(ReadOnlySpan<byte> data)
    {
        return Convert.ToHexString(Hasher.Hash(data).AsSpan()).ToLowerInvariant();
    }
}

internal static class TsraZstd
{
    public static byte[] Compress(ReadOnlySpan<byte> raw, int level)
    {
        using var compressor = new Compressor(level);
        return compressor.Wrap(raw).ToArray();
    }
}

internal static class TsraPages
{
    public const int MinPage = 4 * 1024;
    public const int AvgPage = 16 * 1024;
    public const int MaxPage = 64 * 1024;
    private const ulong Mask = (ulong)(AvgPage - 1) << 48;
    private static readonly ulong[] Gear = BuildGear();

    public static byte[] Encode(ReadOnlySpan<byte> raw, int level)
    {
        var ends = Boundaries(raw);
        var frames = new List<(byte[] Stored, int Raw, byte[] Hash)>(ends.Count);
        var start = 0;
        foreach (var end in ends)
        {
            var page = raw.Slice(start, end - start).ToArray();
            frames.Add((TsraZstd.Compress(page, level), page.Length, TsraHash.Hash16(page)));
            start = end;
        }

        var output = new OutBuf();
        output.U32((uint)frames.Count);
        output.U32(0);
        foreach (var frame in frames)
        {
            output.U32((uint)frame.Stored.Length);
            output.U32((uint)frame.Raw);
            output.Bytes(frame.Hash);
        }

        foreach (var frame in frames)
        {
            output.Bytes(frame.Stored);
        }

        return output.ToArray();
    }

    private static List<int> Boundaries(ReadOnlySpan<byte> raw)
    {
        var ends = new List<int>();
        var start = 0;
        while (start < raw.Length)
        {
            var end = Math.Min(start + MaxPage, raw.Length);
            var cut = end;
            ulong hash = 0;
            var i = start;
            while (i < end)
            {
                hash = (hash << 1) + Gear[raw[i]];
                i++;
                if (i - start >= MinPage && (hash & Mask) == 0)
                {
                    cut = i;
                    break;
                }
            }

            ends.Add(cut);
            start = cut;
        }

        return ends;
    }

    private static ulong[] BuildGear()
    {
        var table = new ulong[256];
        ulong state = 0;
        for (var i = 0; i < table.Length; i++)
        {
            state += 0x9e3779b97f4a7c15UL;
            var z = state;
            z = (z ^ (z >> 30)) * 0xbf58476d1ce4e5b9UL;
            z = (z ^ (z >> 27)) * 0x94d049bb133111ebUL;
            table[i] = z ^ (z >> 31);
        }

        return table;
    }
}

internal enum TsraKind
{
    Meta,
    Strings,
    Entities,
    Styles,
    Proxy,
    ChunkIndex,
    Prototypes,
    Parts,
    RenderData,
    Relations,
    Properties,
    CanonicalIndex,
    CanonicalData
}

internal static class TsraContainer
{
    private const int EntryLength = 48;

    public static byte[] Finish(IReadOnlyList<(TsraKind Kind, byte[] Raw)> sections, int zstdLevel)
    {
        var pending = new List<(TsraKind Kind, uint Flags, byte[] Stored, ulong Raw, byte[] Hash)>(sections.Count);
        foreach (var (kind, raw) in sections)
        {
            var hash = TsraHash.Hash16(raw);
            uint flags = Required(kind) ? 1u : 0u;
            byte[] stored;
            if (Compressed(kind))
            {
                flags |= 2u | 4u;
                stored = TsraPages.Encode(raw, zstdLevel);
            }
            else
            {
                stored = raw;
            }

            pending.Add((kind, flags, stored, (ulong)raw.Length, hash));
        }

        var output = new OutBuf();
        output.Bytes("TSRA"u8);
        output.U16(0);
        output.U16(6);
        output.U32((uint)pending.Count);
        output.U32(0);
        var directoryStart = output.Length;
        var offset = Align8(directoryStart + pending.Count * EntryLength + 16);
        foreach (var section in pending)
        {
            output.Bytes(Encoding.ASCII.GetBytes(Tag(section.Kind)));
            output.U32(section.Flags);
            output.U64((ulong)offset);
            output.U64((ulong)section.Stored.Length);
            output.U64(section.Raw);
            output.Bytes(section.Hash);
            offset = Align8(offset + section.Stored.Length);
        }

        var directory = output.ToArray();
        output.Bytes(TsraHash.Hash16(directory.AsSpan(directoryStart)));
        foreach (var section in pending)
        {
            output.Align(8);
            output.Bytes(section.Stored);
        }

        return output.ToArray();
    }

    private static int Align8(int n) => (n + 7) & ~7;

    private static bool Required(TsraKind kind) => kind is
        TsraKind.Meta or TsraKind.Strings or TsraKind.Entities or TsraKind.ChunkIndex
        or TsraKind.Prototypes or TsraKind.Parts or TsraKind.RenderData;

    private static bool Compressed(TsraKind kind) => kind is not (TsraKind.Meta or TsraKind.RenderData or TsraKind.CanonicalData);

    public static string Tag(TsraKind kind) => kind switch
    {
        TsraKind.Meta => "META",
        TsraKind.Strings => "STRS",
        TsraKind.Entities => "ENTS",
        TsraKind.Styles => "STYL",
        TsraKind.Proxy => "PRXY",
        TsraKind.ChunkIndex => "CHNK",
        TsraKind.Prototypes => "PROT",
        TsraKind.Parts => "PART",
        TsraKind.RenderData => "RDAT",
        TsraKind.Relations => "RELS",
        TsraKind.Properties => "PROP",
        TsraKind.CanonicalIndex => "CIDX",
        TsraKind.CanonicalData => "CANO",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
