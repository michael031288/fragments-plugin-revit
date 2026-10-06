using System.Text;
using Blake3;
using Tessera.Core;
using Xunit;

namespace Tessera.Core.Tests;

public class Blake3Tests
{
    [Fact]
    public void ManagedHashMatchesTheOfficialLibrary()
    {
        AssertMatches(ReadOnlySpan<byte>.Empty);
        AssertMatches(""u8);
        AssertMatches("abc"u8);
        AssertMatches(Encoding.UTF8.GetBytes("Größe"));

        var sequential = new byte[4097];
        for (var i = 0; i < sequential.Length; i++)
        {
            sequential[i] = (byte)i;
        }

        foreach (var length in new[] { 1, 63, 64, 65, 127, 128, 1023, 1024, 1025, 2048, 4096, 4097 })
        {
            AssertMatches(sequential.AsSpan(0, length));
        }
    }

    private static void AssertMatches(ReadOnlySpan<byte> data)
    {
        var official = Hasher.Hash(data).AsSpan();
        var managed = TsraHash.Hex(data);
        Assert.Equal(Convert.ToHexString(official).ToLowerInvariant(), managed);
        Assert.Equal(official.Slice(0, 16).ToArray(), TsraHash.Hash16(data));
    }
}
