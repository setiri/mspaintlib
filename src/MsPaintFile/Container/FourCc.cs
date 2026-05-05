using System.Buffers.Binary;
using System.Text;

namespace MsPaintFile.Container;

public readonly record struct FourCc(uint Value)
{
    public static FourCc From(ReadOnlySpan<byte> four) => new(BinaryPrimitives.ReadUInt32BigEndian(four));

    public static FourCc Of(string ascii)
    {
        if (ascii.Length != 4)
            throw new ArgumentException("FourCC must be exactly 4 ASCII characters.", nameof(ascii));
        Span<byte> b = stackalloc byte[4];
        Encoding.ASCII.GetBytes(ascii, b);
        return From(b);
    }

    public override string ToString()
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, Value);
        for (int i = 0; i < 4; i++)
        {
            if (b[i] < 0x20 || b[i] > 0x7E)
                return $"0x{Value:X8}";
        }
        return Encoding.ASCII.GetString(b);
    }
}
