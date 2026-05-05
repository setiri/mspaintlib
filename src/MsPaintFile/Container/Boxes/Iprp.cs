using MsPaintFile.Properties;

namespace MsPaintFile.Container.Boxes;

public sealed record PropertyAssociation(uint ItemId, IReadOnlyList<PropertyAssociationEntry> Entries);

public sealed record PropertyAssociationEntry(int PropertyIndex, bool Essential);

public sealed record IprpResult(
    IReadOnlyList<object> Properties,                  // 0-based; 1-based when referenced from ipma
    IReadOnlyList<PropertyAssociation> Associations);

public static class IprpBox
{
    public static IprpResult Parse(BoxReader reader, BoxHeader header)
    {
        IReadOnlyList<object>? properties = null;
        var associations = new List<PropertyAssociation>();

        reader.Position = header.PayloadStart;
        while (reader.TryReadBoxHeader(header.PayloadEnd, out var child))
        {
            if (child.Type == FourCc.Of("ipco"))
                properties = ParseIpco(reader, child);
            else if (child.Type == FourCc.Of("ipma"))
                associations.AddRange(ParseIpma(reader, child));
            reader.Position = child.PayloadEnd;
        }
        if (properties is null)
            throw new PaintFormatException("iprp missing required ipco container.");
        return new IprpResult(properties, associations);
    }

    private static IReadOnlyList<object> ParseIpco(BoxReader reader, BoxHeader header)
    {
        var props = new List<object>();
        reader.Position = header.PayloadStart;
        while (reader.TryReadBoxHeader(header.PayloadEnd, out var child))
        {
            props.Add(PropertyParser.Parse(reader, child));
            reader.Position = child.PayloadEnd;
        }
        return props;
    }

    private static IEnumerable<PropertyAssociation> ParseIpma(BoxReader reader, BoxHeader header)
    {
        reader.Position = header.PayloadStart;
        var (version, flags) = reader.ReadFullBoxHeader();
        bool indexIs16Bit = (flags & 1) != 0;

        uint entryCount = reader.ReadU32();
        for (uint i = 0; i < entryCount; i++)
        {
            uint itemId = version == 0 ? reader.ReadU16() : reader.ReadU32();
            byte assocCount = reader.ReadU8();
            var entries = new PropertyAssociationEntry[assocCount];
            for (int a = 0; a < assocCount; a++)
            {
                int idx;
                bool essential;
                if (indexIs16Bit)
                {
                    ushort raw = reader.ReadU16();
                    essential = (raw & 0x8000) != 0;
                    idx = raw & 0x7FFF;
                }
                else
                {
                    byte raw = reader.ReadU8();
                    essential = (raw & 0x80) != 0;
                    idx = raw & 0x7F;
                }
                entries[a] = new PropertyAssociationEntry(idx, essential);
            }
            yield return new PropertyAssociation(itemId, entries);
        }
    }
}
