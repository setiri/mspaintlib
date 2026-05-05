using MsPaintFile.Container;

namespace MsPaintFile.Properties;

public static class PropertyParser
{
    public static object Parse(BoxReader reader, BoxHeader header) => header.Type.ToString() switch
    {
        "ispe" => ParseIspe(reader, header),
        "cmpC" => ParseCmpc(reader, header),
        "colr" => ParseColr(reader, header),
        "cmpd" => ParseCmpd(reader, header),
        "uncC" => ParseUncC(reader, header),
        "pixi" => ParsePixi(reader, header),
        _      => new UnknownProperty(header.Type),
    };

    public sealed record UnknownProperty(FourCc Type);

    private static Ispe ParseIspe(BoxReader reader, BoxHeader header)
    {
        reader.Position = header.PayloadStart;
        _ = reader.ReadFullBoxHeader();
        uint w = reader.ReadU32();
        uint h = reader.ReadU32();
        if (w > int.MaxValue || h > int.MaxValue)
            throw new PaintFormatException($"ispe dimensions {w}x{h} exceed Int32.");
        return new Ispe((int)w, (int)h);
    }

    private static Cmpc ParseCmpc(BoxReader reader, BoxHeader header)
    {
        // ISO/IEC 23001-17 cmpC (CompressionConfigurationProperty):
        //   u8 version, u24 flags, FourCc compression_type, u8 compressed_unit_type
        reader.Position = header.PayloadStart;
        _ = reader.ReadFullBoxHeader();
        var algo = reader.ReadFourCc();
        byte unitType = reader.ReadU8();
        return new Cmpc(algo, unitType);
    }

    private static Colr ParseColr(BoxReader reader, BoxHeader header)
    {
        reader.Position = header.PayloadStart;
        var type = reader.ReadFourCc();
        if (type != FourCc.Of("nclx"))
            throw new PaintFormatException($"colr type '{type}' not supported (only 'nclx').");
        ushort primaries = reader.ReadU16();
        ushort transfer = reader.ReadU16();
        ushort matrix = reader.ReadU16();
        byte flags = reader.ReadU8();
        bool fullRange = (flags & 0x80) != 0;
        return new Colr(type, primaries, transfer, matrix, fullRange);
    }

    private static Cmpd ParseCmpd(BoxReader reader, BoxHeader header)
    {
        reader.Position = header.PayloadStart;
        uint count = reader.ReadU32();
        var comps = new List<CmpdComponent>(checked((int)count));
        for (uint i = 0; i < count; i++)
        {
            ushort ctype = reader.ReadU16();
            byte uriPresent = 0;
            if (ctype >= 0x8000)
            {
                uriPresent = 1;
                _ = reader.ReadNullTerminatedAscii(header.PayloadEnd);
            }
            comps.Add(new CmpdComponent(ctype, uriPresent));
        }
        return new Cmpd(comps);
    }

    private static UncC ParseUncC(BoxReader reader, BoxHeader header)
    {
        reader.Position = header.PayloadStart;
        var (version, _) = reader.ReadFullBoxHeader();
        var profile = reader.ReadFourCc();

        if (version == 1)
        {
            return new UncC(profile, [], 0, 0, 0, false, false, false, false, false, 0, 0, 0, 0, 0);
        }
        if (version != 0)
            throw new PaintFormatException($"uncC version {version} not supported.");

        uint compCount = reader.ReadU32();
        var comps = new List<UncCComponent>(checked((int)compCount));
        for (uint i = 0; i < compCount; i++)
        {
            ushort idx = reader.ReadU16();
            byte bdMinus1 = reader.ReadU8();
            byte format = reader.ReadU8();
            byte alignSize = reader.ReadU8();
            comps.Add(new UncCComponent(idx, bdMinus1, format, alignSize));
        }
        byte sampling = reader.ReadU8();
        byte interleave = reader.ReadU8();
        byte blockSize = reader.ReadU8();
        byte flagByte = reader.ReadU8();
        bool compsLE = (flagByte & 0x01) != 0;
        bool padLsb = (flagByte & 0x02) != 0;
        bool blockLE = (flagByte & 0x04) != 0;
        bool blockRev = (flagByte & 0x08) != 0;
        bool padUnknown = (flagByte & 0x10) != 0;
        uint pixelSize = reader.ReadU32();
        uint rowAlign = reader.ReadU32();
        uint tileAlign = reader.ReadU32();
        uint tileColsM1 = reader.ReadU32();
        uint tileRowsM1 = reader.ReadU32();
        return new UncC(profile, comps, sampling, interleave, blockSize,
            compsLE, padLsb, blockLE, blockRev, padUnknown,
            pixelSize, rowAlign, tileAlign, tileColsM1, tileRowsM1);
    }

    private static Pixi ParsePixi(BoxReader reader, BoxHeader header)
    {
        reader.Position = header.PayloadStart;
        _ = reader.ReadFullBoxHeader();
        byte numChannels = reader.ReadU8();
        var bits = new byte[numChannels];
        for (int i = 0; i < numChannels; i++)
            bits[i] = reader.ReadU8();
        return new Pixi(bits);
    }
}
