namespace MsPaintFile.Container.Boxes;

public sealed record ItemReferenceEntry(FourCc Type, uint FromItemId, IReadOnlyList<uint> ToItemIds);

public static class IrefBox
{
    public static IReadOnlyList<ItemReferenceEntry> Parse(BoxReader reader, BoxHeader header)
    {
        reader.Position = header.PayloadStart;
        var (version, _) = reader.ReadFullBoxHeader();
        if (version > 1)
            throw new PaintFormatException($"iref version {version} not supported.");

        var refs = new List<ItemReferenceEntry>();
        while (reader.Position < header.PayloadEnd)
        {
            if (!reader.TryReadBoxHeader(header.PayloadEnd, out var child))
                break;
            uint from = version == 0 ? reader.ReadU16() : reader.ReadU32();
            ushort count = reader.ReadU16();
            var to = new uint[count];
            for (int i = 0; i < count; i++)
                to[i] = version == 0 ? reader.ReadU16() : reader.ReadU32();
            refs.Add(new ItemReferenceEntry(child.Type, from, to));
            reader.Position = child.PayloadEnd;
        }
        return refs;
    }
}
