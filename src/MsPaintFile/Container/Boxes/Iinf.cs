namespace MsPaintFile.Container.Boxes;

public sealed record InfeEntry(uint ItemId, ushort ProtectionIndex, FourCc ItemType, string ItemName);

public static class IinfBox
{
    public static IReadOnlyList<InfeEntry> Parse(BoxReader reader, BoxHeader header)
    {
        reader.Position = header.PayloadStart;
        var (version, _) = reader.ReadFullBoxHeader();
        uint count = version < 2 ? reader.ReadU16() : reader.ReadU32();

        var entries = new List<InfeEntry>(checked((int)count));
        for (uint i = 0; i < count; i++)
        {
            if (!reader.TryReadBoxHeader(header.PayloadEnd, out var infe))
                throw new PaintFormatException($"iinf declared {count} entries but stream ended after {i}.");
            if (infe.Type != FourCc.Of("infe"))
                throw new PaintFormatException($"iinf child has unexpected type '{infe.Type}'.");
            entries.Add(ParseInfe(reader, infe));
            reader.Position = infe.PayloadEnd;
        }
        return entries;
    }

    private static InfeEntry ParseInfe(BoxReader reader, BoxHeader header)
    {
        reader.Position = header.PayloadStart;
        var (version, _) = reader.ReadFullBoxHeader();
        if (version < 2)
            throw new PaintFormatException($"infe version {version} not supported (need v2 or v3).");

        uint itemId = version == 2 ? reader.ReadU16() : reader.ReadU32();
        ushort protection = reader.ReadU16();
        var itemType = reader.ReadFourCc();
        var itemName = reader.ReadNullTerminatedAscii(header.PayloadEnd);
        // mime/uri trailing fields are not needed for any item type used by .paint.
        return new InfeEntry(itemId, protection, itemType, itemName);
    }
}
