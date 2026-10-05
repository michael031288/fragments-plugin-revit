using System.Text;

namespace Tessera.Core;

internal sealed class OutBuf
{
    private readonly MemoryStream _stream = new();
    private readonly BinaryWriter _writer;

    public OutBuf()
    {
        _writer = new BinaryWriter(_stream, Encoding.UTF8, leaveOpen: true);
    }

    public int Length => (int)_stream.Length;

    public void U8(byte value) => _writer.Write(value);

    public void U16(ushort value) => _writer.Write(value);

    public void U32(uint value) => _writer.Write(value);

    public void U64(ulong value) => _writer.Write(value);

    public void I32(int value) => _writer.Write(value);

    public void I64(long value) => _writer.Write(value);

    public void F32(float value) => _writer.Write(value);

    public void Bytes(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return;
        }

        _writer.Write(data.ToArray());
    }

    public void Align(int n)
    {
        var pad = (n - (Length % n)) % n;
        for (var i = 0; i < pad; i++)
        {
            U8(0);
        }
    }

    public byte[] ToArray()
    {
        _writer.Flush();
        return _stream.ToArray();
    }
}
