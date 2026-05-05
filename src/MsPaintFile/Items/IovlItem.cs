using System.Buffers.Binary;
using MsPaintFile.Container;

namespace MsPaintFile.Items;

public sealed record IovlInput(int OffsetX, int OffsetY);

public sealed record IovlPayload(
    byte Version,
    byte Flags,
    ushort FillR,
    ushort FillG,
    ushort FillB,
    ushort FillA,
    int OutputWidth,
    int OutputHeight,
    IReadOnlyList<IovlInput> Inputs);

public static class IovlItem
{
    public static IovlPayload Parse(ReadOnlySpan<byte> bytes, int inputCount)
    {
        if (bytes.Length < 12)
            throw new PaintFormatException($"iovl payload too short: {bytes.Length} bytes.");
        int p = 0;
        byte version = bytes[p++];
        byte flags = bytes[p++];
        if ((flags & ~0x01) != 0)
            throw new PaintFormatException($"iovl reserved flag bits set (flags=0x{flags:X2}); only bit 0 is defined.");
        bool wide = (flags & 0x01) != 0;

        ushort fillR = BinaryPrimitives.ReadUInt16BigEndian(bytes[p..]); p += 2;
        ushort fillG = BinaryPrimitives.ReadUInt16BigEndian(bytes[p..]); p += 2;
        ushort fillB = BinaryPrimitives.ReadUInt16BigEndian(bytes[p..]); p += 2;
        ushort fillA = BinaryPrimitives.ReadUInt16BigEndian(bytes[p..]); p += 2;

        int outputW, outputH;
        if (wide)
        {
            outputW = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes[p..])); p += 4;
            outputH = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes[p..])); p += 4;
        }
        else
        {
            outputW = BinaryPrimitives.ReadUInt16BigEndian(bytes[p..]); p += 2;
            outputH = BinaryPrimitives.ReadUInt16BigEndian(bytes[p..]); p += 2;
        }

        int perInput = wide ? 8 : 4;
        long expected = (long)p + (long)inputCount * perInput;
        if (bytes.Length < expected)
            throw new PaintFormatException(
                $"iovl payload {bytes.Length} bytes is too short for {inputCount} inputs (need {expected}).");

        var inputs = new IovlInput[inputCount];
        for (int i = 0; i < inputCount; i++)
        {
            int dx, dy;
            if (wide)
            {
                dx = BinaryPrimitives.ReadInt32BigEndian(bytes[p..]); p += 4;
                dy = BinaryPrimitives.ReadInt32BigEndian(bytes[p..]); p += 4;
            }
            else
            {
                dx = BinaryPrimitives.ReadInt16BigEndian(bytes[p..]); p += 2;
                dy = BinaryPrimitives.ReadInt16BigEndian(bytes[p..]); p += 2;
            }
            inputs[i] = new IovlInput(dx, dy);
        }

        return new IovlPayload(version, flags, fillR, fillG, fillB, fillA, outputW, outputH, inputs);
    }
}
