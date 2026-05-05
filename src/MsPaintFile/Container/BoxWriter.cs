using System.Buffers.Binary;

namespace MsPaintFile.Container;

/// <summary>
/// Streaming writer for ISOBMFF boxes. Uses backpatching: <see cref="BeginBox"/> writes
/// a placeholder size and the box returns a <see cref="BoxScope"/> whose <c>Dispose</c>
/// rewrites the size once the payload is known. Requires a seekable stream.
/// </summary>
public sealed class BoxWriter
{
    private readonly Stream _stream;

    public BoxWriter(Stream stream)
    {
        if (!stream.CanWrite) throw new ArgumentException("Stream must be writable.", nameof(stream));
        if (!stream.CanSeek) throw new ArgumentException("Stream must be seekable.", nameof(stream));
        _stream = stream;
    }

    public long Position
    {
        get => _stream.Position;
        set => _stream.Position = value;
    }

    public Stream BaseStream => _stream;

    public BoxScope BeginBox(string fourcc) => BeginBox(FourCc.Of(fourcc));

    public BoxScope BeginBox(FourCc type)
    {
        long start = _stream.Position;
        WriteU32(0);              // size placeholder
        WriteFourCc(type);
        return new BoxScope(this, start);
    }

    public void WriteFullBoxHeader(byte version, uint flags)
    {
        if (flags > 0x00FFFFFF) throw new ArgumentOutOfRangeException(nameof(flags), "Flags must fit in 24 bits.");
        WriteU8(version);
        WriteU24(flags);
    }

    public void WriteU8(byte v) => _stream.WriteByte(v);

    public void WriteU16(ushort v)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(b, v);
        _stream.Write(b);
    }

    public void WriteU24(uint v)
    {
        if (v > 0x00FFFFFF) throw new ArgumentOutOfRangeException(nameof(v));
        Span<byte> b = stackalloc byte[3];
        b[0] = (byte)(v >> 16);
        b[1] = (byte)(v >> 8);
        b[2] = (byte)v;
        _stream.Write(b);
    }

    public void WriteU32(uint v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, v);
        _stream.Write(b);
    }

    public void WriteU64(ulong v)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(b, v);
        _stream.Write(b);
    }

    public void WriteI16(short v)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteInt16BigEndian(b, v);
        _stream.Write(b);
    }

    public void WriteI32(int v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(b, v);
        _stream.Write(b);
    }

    public void WriteFourCc(FourCc fc) => WriteU32(fc.Value);

    public void WriteBytes(ReadOnlySpan<byte> bytes) => _stream.Write(bytes);

    public void WriteNullTerminatedAscii(string s)
    {
        Span<byte> b = stackalloc byte[s.Length];
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c > 0x7F) throw new ArgumentException($"Non-ASCII character '{c}' in box string.", nameof(s));
            b[i] = (byte)c;
        }
        _stream.Write(b);
        _stream.WriteByte(0);
    }

    public void WriteZeros(long count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        Span<byte> chunk = stackalloc byte[256];
        chunk.Clear();
        while (count > 0)
        {
            int n = (int)Math.Min(count, chunk.Length);
            _stream.Write(chunk[..n]);
            count -= n;
        }
    }

    /// <summary>
    /// Backpatch a 32-bit big-endian value at an absolute file offset, preserving the current position.
    /// Used to fix up iloc extent offsets after the mdat location is known.
    /// </summary>
    public void PatchU32At(long absoluteOffset, uint value)
    {
        long save = _stream.Position;
        _stream.Position = absoluteOffset;
        WriteU32(value);
        _stream.Position = save;
    }

    internal void EndBox(long start)
    {
        long end = _stream.Position;
        long size = end - start;
        if (size < 8) throw new InvalidOperationException($"Box size {size} is shorter than minimum header (8).");
        if (size > uint.MaxValue)
            throw new InvalidOperationException($"Box size {size} exceeds 32-bit. 64-bit largesize encoding not implemented for writer.");
        _stream.Position = start;
        WriteU32(checked((uint)size));
        _stream.Position = end;
    }
}

public readonly struct BoxScope : IDisposable
{
    private readonly BoxWriter _writer;
    private readonly long _start;

    internal BoxScope(BoxWriter writer, long start)
    {
        _writer = writer;
        _start = start;
    }

    public long PayloadStart => _start + 8;
    public long Start => _start;

    public void Dispose() => _writer.EndBox(_start);
}
