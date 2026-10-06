namespace Tessera.Core;

/// <summary>
/// BLAKE3, ported from the official reference implementation. Revit does not
/// search the add-in folder for native libraries, so the hasher stays managed.
/// </summary>
internal static class ManagedBlake3
{
    private const int BlockLen = 64;
    private const int ChunkLen = 1024;
    private const int OutLen = 32;
    private const uint FlagChunkStart = 1;
    private const uint FlagChunkEnd = 2;
    private const uint FlagParent = 4;
    private const uint FlagRoot = 8;

    private static readonly uint[] Iv =
    {
        0x6A09E667, 0xBB67AE85, 0x3C6EF372, 0xA54FF53A,
        0x510E527F, 0x9B05688C, 0x1F83D9AB, 0x5BE0CD19
    };

    private static readonly int[] MessagePermutation = { 2, 6, 3, 10, 7, 0, 4, 13, 1, 11, 12, 5, 9, 14, 15, 8 };

    public static byte[] Hash(ReadOnlySpan<byte> input)
    {
        var hasher = new Hasher();
        hasher.Update(input);
        var output = new byte[OutLen];
        hasher.Finalize(output);
        return output;
    }

    private static Output ParentNode(uint[] left, uint[] right, uint[] key, uint flags)
    {
        var words = new uint[16];
        Array.Copy(left, 0, words, 0, 8);
        Array.Copy(right, 0, words, 8, 8);
        var chaining = new uint[8];
        Array.Copy(key, chaining, 8);
        return new Output(chaining, words, 0, BlockLen, FlagParent | flags);
    }

    private static void Compress(uint[] chaining, uint[] blockWords, ulong counter, uint blockLen, uint flags, uint[] state)
    {
        state[0] = chaining[0];
        state[1] = chaining[1];
        state[2] = chaining[2];
        state[3] = chaining[3];
        state[4] = chaining[4];
        state[5] = chaining[5];
        state[6] = chaining[6];
        state[7] = chaining[7];
        state[8] = Iv[0];
        state[9] = Iv[1];
        state[10] = Iv[2];
        state[11] = Iv[3];
        state[12] = (uint)counter;
        state[13] = (uint)(counter >> 32);
        state[14] = blockLen;
        state[15] = flags;

        Span<uint> message = stackalloc uint[16];
        for (var i = 0; i < 16; i++)
        {
            message[i] = blockWords[i];
        }

        for (var round = 0; round < 7; round++)
        {
            Round(state, message);
            if (round < 6)
            {
                Permute(message);
            }
        }

        for (var i = 0; i < 8; i++)
        {
            state[i] ^= state[i + 8];
            state[i + 8] ^= chaining[i];
        }
    }

    private static void Round(uint[] state, ReadOnlySpan<uint> m)
    {
        G(state, 0, 4, 8, 12, m[0], m[1]);
        G(state, 1, 5, 9, 13, m[2], m[3]);
        G(state, 2, 6, 10, 14, m[4], m[5]);
        G(state, 3, 7, 11, 15, m[6], m[7]);
        G(state, 0, 5, 10, 15, m[8], m[9]);
        G(state, 1, 6, 11, 12, m[10], m[11]);
        G(state, 2, 7, 8, 13, m[12], m[13]);
        G(state, 3, 4, 9, 14, m[14], m[15]);
    }

    private static void G(uint[] state, int a, int b, int c, int d, uint mx, uint my)
    {
        state[a] = state[a] + state[b] + mx;
        state[d] = Rot(state[d] ^ state[a], 16);
        state[c] = state[c] + state[d];
        state[b] = Rot(state[b] ^ state[c], 12);
        state[a] = state[a] + state[b] + my;
        state[d] = Rot(state[d] ^ state[a], 8);
        state[c] = state[c] + state[d];
        state[b] = Rot(state[b] ^ state[c], 7);
    }

    private static uint Rot(uint value, int bits) => (value >> bits) | (value << (32 - bits));

    private static void Permute(Span<uint> message)
    {
        Span<uint> next = stackalloc uint[16];
        for (var i = 0; i < 16; i++)
        {
            next[i] = message[MessagePermutation[i]];
        }

        next.CopyTo(message);
    }

    private static void WordsFromBytes(ReadOnlySpan<byte> bytes, uint[] words)
    {
        for (var i = 0; i < words.Length; i++)
        {
            var at = i * 4;
            words[i] = (uint)(bytes[at] | (bytes[at + 1] << 8) | (bytes[at + 2] << 16) | (bytes[at + 3] << 24));
        }
    }

    private sealed class Hasher
    {
        private readonly ChunkState _chunk = new(Iv, 0, 0);
        private readonly uint[][] _stack = new uint[54][];
        private int _stackLen;

        public Hasher()
        {
            for (var i = 0; i < _stack.Length; i++)
            {
                _stack[i] = new uint[8];
            }
        }

        public void Update(ReadOnlySpan<byte> input)
        {
            while (!input.IsEmpty)
            {
                if (_chunk.Length == ChunkLen)
                {
                    var chunkCv = _chunk.Output().ChainingValue();
                    var totalChunks = _chunk.ChunkCounter + 1;
                    AddChunk(chunkCv, totalChunks);
                    _chunk.Reset(Iv, totalChunks, 0);
                }

                var take = Math.Min(ChunkLen - _chunk.Length, input.Length);
                _chunk.Update(input.Slice(0, take));
                input = input.Slice(take);
            }
        }

        public void Finalize(Span<byte> output)
        {
            var node = _chunk.Output();
            var remaining = _stackLen;
            while (remaining > 0)
            {
                remaining--;
                node = ParentNode(_stack[remaining], node.ChainingValue(), Iv, 0);
            }

            node.RootBytes(output);
        }

        private void AddChunk(uint[] cv, ulong totalChunks)
        {
            while ((totalChunks & 1) == 0)
            {
                cv = ParentNode(Pop(), cv, Iv, 0).ChainingValue();
                totalChunks >>= 1;
            }

            Push(cv);
        }

        private void Push(uint[] cv)
        {
            Array.Copy(cv, _stack[_stackLen], 8);
            _stackLen++;
        }

        private uint[] Pop()
        {
            _stackLen--;
            var cv = new uint[8];
            Array.Copy(_stack[_stackLen], cv, 8);
            return cv;
        }
    }

    private sealed class ChunkState
    {
        private readonly uint[] _chaining = new uint[8];
        private readonly byte[] _block = new byte[BlockLen];
        private readonly uint[] _blockWords = new uint[16];
        private byte _blockLen;
        private byte _blocksCompressed;
        private uint _flags;

        public ChunkState(uint[] key, ulong counter, uint flags)
        {
            Reset(key, counter, flags);
        }

        public ulong ChunkCounter { get; private set; }

        public int Length => (BlockLen * _blocksCompressed) + _blockLen;

        public void Reset(uint[] key, ulong counter, uint flags)
        {
            Array.Copy(key, _chaining, 8);
            ChunkCounter = counter;
            Array.Clear(_block);
            _blockLen = 0;
            _blocksCompressed = 0;
            _flags = flags;
        }

        public void Update(ReadOnlySpan<byte> input)
        {
            while (!input.IsEmpty)
            {
                if (_blockLen == BlockLen)
                {
                    WordsFromBytes(_block, _blockWords);
                    var state = new uint[16];
                    Compress(_chaining, _blockWords, ChunkCounter, BlockLen, _flags | StartFlag(), state);
                    Array.Copy(state, _chaining, 8);
                    _blocksCompressed++;
                    Array.Clear(_block);
                    _blockLen = 0;
                }

                var take = Math.Min(BlockLen - _blockLen, input.Length);
                input.Slice(0, take).CopyTo(_block.AsSpan(_blockLen));
                _blockLen += (byte)take;
                input = input.Slice(take);
            }
        }

        public Output Output()
        {
            WordsFromBytes(_block, _blockWords);
            var chaining = new uint[8];
            Array.Copy(_chaining, chaining, 8);
            var words = new uint[16];
            Array.Copy(_blockWords, words, 16);
            return new Output(chaining, words, ChunkCounter, _blockLen, _flags | StartFlag() | FlagChunkEnd);
        }

        private uint StartFlag() => _blocksCompressed == 0 ? FlagChunkStart : 0;
    }

    private readonly struct Output
    {
        private readonly uint[] _chaining;
        private readonly uint[] _blockWords;
        private readonly ulong _counter;
        private readonly uint _blockLen;
        private readonly uint _flags;

        public Output(uint[] chaining, uint[] blockWords, ulong counter, uint blockLen, uint flags)
        {
            _chaining = chaining;
            _blockWords = blockWords;
            _counter = counter;
            _blockLen = blockLen;
            _flags = flags;
        }

        public uint[] ChainingValue()
        {
            var state = new uint[16];
            Compress(_chaining, _blockWords, _counter, _blockLen, _flags, state);
            var cv = new uint[8];
            Array.Copy(state, cv, 8);
            return cv;
        }

        public void RootBytes(Span<byte> destination)
        {
            var state = new uint[16];
            Compress(_chaining, _blockWords, 0, _blockLen, _flags | FlagRoot, state);
            for (var i = 0; i < 8; i++)
            {
                var word = state[i];
                var at = i * 4;
                destination[at] = (byte)word;
                destination[at + 1] = (byte)(word >> 8);
                destination[at + 2] = (byte)(word >> 16);
                destination[at + 3] = (byte)(word >> 24);
            }
        }
    }
}
