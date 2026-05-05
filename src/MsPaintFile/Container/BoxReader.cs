using System.Buffers.Binary;
using System.Text;

namespace MsPaintFile.Container;

public sealed class BoxReader
{
    private readonly Stream _stream;

    public BoxReader(Stream stream)
    {
        if (!stream.CanSeek)
            throw new ArgumentException("Stream must be seekable.", nameof(stream));
        if (!stream.CanRead)
            throw new ArgumentException("Stream must be readable.", nameof(stream));
        _stream = stream;
    }

    public long Position
    {
        get => _stream.Position;
        set => _stream.Position = value;
    }

    public long Length => _stream.Length;

    public Stream BaseStream => _stream;

    public bool TryReadBoxHeader(long containerEndExclusive, out BoxHeader header)
    {
        if (containerEndExclusive < 0 || containerEndExclusive > _stream.Length)
            throw new PaintFormatException($"Container end {containerEndExclusive} is outside stream length {_stream.Length}.");

        long headerStart = _stream.Position;
        if (headerStart >= containerEndExclusive)
        {
            header = default;
            return false;
        }
        if (containerEndExclusive - headerStart < 8)
            throw new PaintFormatException($"Truncated box header at offset {headerStart}: only {containerEndExclusive - headerStart} bytes remain.");

        Span<byte> hdr = stackalloc byte[8];
        ReadExactly(hdr);
        uint size32 = BinaryPrimitives.ReadUInt32BigEndian(hdr);
        FourCc type = FourCc.From(hdr[4..]);

        long payloadStart;
        long payloadLength;
        if (size32 == 1)
        {
            if (containerEndExclusive - _stream.Position < 8)
                throw new PaintFormatException($"Truncated 64-bit largesize for box '{type}' at offset {headerStart}.");
            Span<byte> large = stackalloc byte[8];
            ReadExactly(large);
            ulong size64 = BinaryPrimitives.ReadUInt64BigEndian(large);
            if (size64 < 16)
                throw new PaintFormatException($"Box '{type}' at offset {headerStart} declares 64-bit size {size64}, smaller than minimum header (16).");
            payloadStart = _stream.Position;
            payloadLength = checked((long)size64) - 16;
        }
        else if (size32 == 0)
        {
            payloadStart = _stream.Position;
            payloadLength = containerEndExclusive - payloadStart;
        }
        else if (size32 < 8)
        {
            throw new PaintFormatException($"Box '{type}' at offset {headerStart} declares size {size32}, smaller than minimum header (8).");
        }
        else
        {
            payloadStart = _stream.Position;
            payloadLength = size32 - 8L;
        }

        if (payloadStart + payloadLength > containerEndExclusive)
            throw new PaintFormatException(
                $"Box '{type}' at offset {headerStart} extends to {payloadStart + payloadLength}, past container end {containerEndExclusive}.");

        header = new BoxHeader(type, headerStart, payloadStart, payloadLength);
        return true;
    }

    public byte ReadU8()
    {
        int b = _stream.ReadByte();
        if (b < 0) throw EndOfStream(1);
        return (byte)b;
    }

    public ushort ReadU16()
    {
        Span<byte> buf = stackalloc byte[2];
        ReadExactly(buf);
        return BinaryPrimitives.ReadUInt16BigEndian(buf);
    }

    public uint ReadU24()
    {
        Span<byte> buf = stackalloc byte[3];
        ReadExactly(buf);
        return ((uint)buf[0] << 16) | ((uint)buf[1] << 8) | buf[2];
    }

    public uint ReadU32()
    {
        Span<byte> buf = stackalloc byte[4];
        ReadExactly(buf);
        return BinaryPrimitives.ReadUInt32BigEndian(buf);
    }

    public ulong ReadU64()
    {
        Span<byte> buf = stackalloc byte[8];
        ReadExactly(buf);
        return BinaryPrimitives.ReadUInt64BigEndian(buf);
    }

    public short ReadI16()
    {
        Span<byte> buf = stackalloc byte[2];
        ReadExactly(buf);
        return BinaryPrimitives.ReadInt16BigEndian(buf);
    }

    public int ReadI32()
    {
        Span<byte> buf = stackalloc byte[4];
        ReadExactly(buf);
        return BinaryPrimitives.ReadInt32BigEndian(buf);
    }

    public FourCc ReadFourCc()
    {
        Span<byte> buf = stackalloc byte[4];
        ReadExactly(buf);
        return FourCc.From(buf);
    }

    public byte[] ReadBytes(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (count == 0) return [];
        var bytes = new byte[count];
        ReadExactly(bytes);
        return bytes;
    }

    public string ReadNullTerminatedAscii(long boundaryExclusive)
    {
        if (boundaryExclusive < _stream.Position)
            throw new ArgumentOutOfRangeException(nameof(boundaryExclusive));
        var sb = new StringBuilder();
        while (_stream.Position < boundaryExclusive)
        {
            int b = _stream.ReadByte();
            if (b < 0) throw EndOfStream(1);
            if (b == 0) return sb.ToString();
            sb.Append((char)b);
        }
        throw new PaintFormatException("Null-terminated string ran past container boundary without a terminator.");
    }

    public void Skip(long count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        long target = _stream.Position + count;
        if (target > _stream.Length)
            throw EndOfStream(count);
        _stream.Position = target;
    }

    public (uint Version, uint Flags) ReadFullBoxHeader()
    {
        uint v = ReadU8();
        uint f = ReadU24();
        return (v, f);
    }

    private void ReadExactly(Span<byte> dst)
    {
        int total = 0;
        while (total < dst.Length)
        {
            int n = _stream.Read(dst[total..]);
            if (n <= 0) throw EndOfStream(dst.Length - total);
            total += n;
        }
    }

    private static PaintFormatException EndOfStream(long needed) =>
        new($"Unexpected end of stream while reading {needed} byte(s).");
}
