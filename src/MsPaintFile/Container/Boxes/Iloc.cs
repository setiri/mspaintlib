namespace MsPaintFile.Container.Boxes;

public sealed record IlocExtent(long Offset, long Length);

public sealed record IlocEntry(
    uint ItemId,
    byte ConstructionMethod,    // 0=file, 1=idat, 2=item
    ushort DataReferenceIndex,
    long BaseOffset,
    IReadOnlyList<IlocExtent> Extents);

public static class IlocBox
{
    public static IReadOnlyDictionary<uint, IlocEntry> Parse(BoxReader reader, BoxHeader header)
    {
        reader.Position = header.PayloadStart;
        var (version, _) = reader.ReadFullBoxHeader();
        if (version > 2)
            throw new PaintFormatException($"iloc version {version} not supported.");

        byte b1 = reader.ReadU8();
        byte b2 = reader.ReadU8();
        int offsetSize = (b1 >> 4) & 0xF;
        int lengthSize = b1 & 0xF;
        int baseOffsetSize = (b2 >> 4) & 0xF;
        int indexSize = (version >= 1) ? (b2 & 0xF) : 0;

        ValidateFieldSize("offset_size", offsetSize);
        ValidateFieldSize("length_size", lengthSize);
        ValidateFieldSize("base_offset_size", baseOffsetSize);
        ValidateFieldSize("index_size", indexSize);

        uint itemCount = version < 2 ? reader.ReadU16() : reader.ReadU32();
        var dict = new Dictionary<uint, IlocEntry>(checked((int)itemCount));
        for (uint i = 0; i < itemCount; i++)
        {
            uint itemId = version < 2 ? reader.ReadU16() : reader.ReadU32();
            byte constructionMethod = 0;
            if (version >= 1)
            {
                ushort cm = reader.ReadU16();
                constructionMethod = (byte)(cm & 0xF);    // low nibble; high 12 bits reserved
            }
            ushort dataRefIndex = reader.ReadU16();
            long baseOffset = ReadSized(reader, baseOffsetSize);
            uint extentCount = version < 2 ? reader.ReadU16() : reader.ReadU16();
            // Per ISO/IEC 14496-12 §8.11.3.1, extent_count is u16 in all versions.

            var extents = new List<IlocExtent>(checked((int)extentCount));
            for (uint e = 0; e < extentCount; e++)
            {
                if (indexSize > 0) _ = ReadSized(reader, indexSize);
                long off = ReadSized(reader, offsetSize);
                long len = ReadSized(reader, lengthSize);
                if (len < 0) throw new PaintFormatException($"iloc extent length {len} is negative for item {itemId}.");
                extents.Add(new IlocExtent(off, len));
            }
            dict[itemId] = new IlocEntry(itemId, constructionMethod, dataRefIndex, baseOffset, extents);
        }
        return dict;
    }

    private static void ValidateFieldSize(string name, int size)
    {
        if (size != 0 && size != 4 && size != 8)
            throw new PaintFormatException($"iloc {name}={size}; only 0, 4, or 8 are valid.");
    }

    private static long ReadSized(BoxReader reader, int size) => size switch
    {
        0 => 0L,
        4 => reader.ReadU32(),
        8 => checked((long)reader.ReadU64()),
        _ => throw new PaintFormatException($"Internal: unexpected sized read of {size}."),
    };
}
