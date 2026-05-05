using MsPaintFile.Container;
using MsPaintFile.Container.Boxes;

namespace MsPaintFile.Items;

public sealed record ItemEntry(
    uint Id,
    FourCc Type,
    string Name,
    byte ConstructionMethod,
    long BaseOffset,
    IReadOnlyList<IlocExtent> Extents,
    IReadOnlyList<object> Properties);

public sealed class ItemTable
{
    public uint PrimaryItemId { get; }
    public IReadOnlyDictionary<uint, ItemEntry> Items { get; }
    public IReadOnlyList<ItemReferenceEntry> References { get; }
    public byte[] IdatBytes { get; }

    public ItemTable(
        uint primaryItemId,
        IReadOnlyDictionary<uint, ItemEntry> items,
        IReadOnlyList<ItemReferenceEntry> references,
        byte[] idatBytes)
    {
        PrimaryItemId = primaryItemId;
        Items = items;
        References = references;
        IdatBytes = idatBytes;
    }

    public ItemEntry GetItem(uint id)
    {
        if (!Items.TryGetValue(id, out var item))
            throw new PaintFormatException($"Item id {id} referenced but not present in item table.");
        return item;
    }

    public T? GetProperty<T>(uint itemId) where T : class
    {
        var entry = GetItem(itemId);
        T? found = null;
        foreach (var p in entry.Properties)
        {
            if (p is T match)
            {
                if (found is not null)
                    throw new PaintFormatException($"Item {itemId} has multiple {typeof(T).Name} properties associated.");
                found = match;
            }
        }
        return found;
    }

    public T RequireProperty<T>(uint itemId) where T : class =>
        GetProperty<T>(itemId)
        ?? throw new PaintFormatException($"Item {itemId} is missing required {typeof(T).Name} property.");
}

public static class MetaParser
{
    public static ItemTable Parse(BoxReader reader, BoxHeader metaHeader)
    {
        if (metaHeader.Type != FourCc.Of("meta"))
            throw new PaintFormatException($"MetaParser called on non-meta box '{metaHeader.Type}'.");

        reader.Position = metaHeader.PayloadStart;
        _ = reader.ReadFullBoxHeader();

        uint? primaryId = null;
        FourCc? handlerType = null;
        IReadOnlyList<InfeEntry> infeEntries = [];
        IReadOnlyDictionary<uint, IlocEntry> ilocEntries = new Dictionary<uint, IlocEntry>();
        IReadOnlyList<ItemReferenceEntry> references = [];
        IprpResult? iprp = null;
        byte[] idat = [];

        while (reader.TryReadBoxHeader(metaHeader.PayloadEnd, out var child))
        {
            switch (child.Type.ToString())
            {
                case "hdlr":
                    handlerType = HdlrBox.ReadHandlerType(reader, child);
                    break;
                case "pitm":
                    primaryId = PitmBox.Parse(reader, child);
                    break;
                case "iinf":
                    infeEntries = IinfBox.Parse(reader, child);
                    break;
                case "iloc":
                    ilocEntries = IlocBox.Parse(reader, child);
                    break;
                case "iref":
                    references = IrefBox.Parse(reader, child);
                    break;
                case "iprp":
                    iprp = IprpBox.Parse(reader, child);
                    break;
                case "idat":
                    idat = IdatBox.Parse(reader, child);
                    break;
            }
            reader.Position = child.PayloadEnd;
        }

        if (handlerType != FourCc.Of("pict"))
            throw new PaintFormatException($"meta handler must be 'pict', got '{handlerType?.ToString() ?? "(missing)"}'.");
        if (primaryId is null)
            throw new PaintFormatException("meta is missing required pitm box.");
        if (iprp is null)
            throw new PaintFormatException("meta is missing required iprp box.");

        // Build item id → properties map from ipma associations.
        var assoc = iprp.Associations.ToDictionary(a => a.ItemId, a => a.Entries);

        var items = new Dictionary<uint, ItemEntry>();
        foreach (var infe in infeEntries)
        {
            if (!ilocEntries.TryGetValue(infe.ItemId, out var iloc))
                throw new PaintFormatException($"Item {infe.ItemId} ('{infe.ItemType}') has infe but no iloc entry.");

            var props = new List<object>();
            if (assoc.TryGetValue(infe.ItemId, out var entries))
            {
                foreach (var ent in entries)
                {
                    if (ent.PropertyIndex < 1 || ent.PropertyIndex > iprp.Properties.Count)
                        throw new PaintFormatException(
                            $"ipma references property index {ent.PropertyIndex} for item {infe.ItemId}, but ipco has only {iprp.Properties.Count} properties.");
                    props.Add(iprp.Properties[ent.PropertyIndex - 1]);
                }
            }

            items[infe.ItemId] = new ItemEntry(
                infe.ItemId,
                infe.ItemType,
                infe.ItemName,
                iloc.ConstructionMethod,
                iloc.BaseOffset,
                iloc.Extents,
                props);
        }

        return new ItemTable(primaryId.Value, items, references, idat);
    }
}
