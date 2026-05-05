using MsPaintFile.Container;
using MsPaintFile.Container.Boxes;
using MsPaintFile.Items;
using MsPaintFile.Properties;

namespace MsPaintFile;

public sealed class PaintDocument
{
    public int Width { get; set; }
    public int Height { get; set; }
    public IList<PaintLayer> Layers { get; } = new List<PaintLayer>();
    public RgbaColor CanvasFill { get; set; } = RgbaColor.OpaqueWhite;

    public PaintDocument() { }

    public PaintDocument(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public static PaintDocument Load(string path)
    {
        using var fs = File.OpenRead(path);
        return Load(fs);
    }

    public void Save(string path)
    {
        using var fs = File.Create(path);
        Save(fs);
    }

    public void Save(Stream stream) => PaintDocumentWriter.Write(this, stream);

    public static PaintDocument Load(Stream stream)
    {
        var reader = new BoxReader(stream);

        FtypInfo? ftyp = null;
        BoxHeader? metaHeader = null;
        long mdatStart = -1;
        long mdatEnd = -1;

        while (reader.TryReadBoxHeader(reader.Length, out var box))
        {
            switch (box.Type.ToString())
            {
                case "ftyp":
                    ftyp = FtypBox.Parse(reader, box);
                    break;
                case "meta":
                    metaHeader = box;
                    break;
                case "mdat":
                    mdatStart = box.PayloadStart;
                    mdatEnd = box.PayloadEnd;
                    break;
            }
            reader.Position = box.PayloadEnd;
        }

        if (ftyp is null)
            throw new PaintFormatException("File is missing ftyp.");
        if (!ftyp.HasBrand(FourCc.Of("mif1")))
            throw new PaintFormatException($"ftyp does not advertise the 'mif1' brand (major='{ftyp.MajorBrand}', compat=[{string.Join(",", ftyp.CompatibleBrands)}]).");
        if (metaHeader is null)
            throw new PaintFormatException("File is missing meta.");

        var table = MetaParser.Parse(reader, metaHeader.Value);

        var primary = table.GetItem(table.PrimaryItemId);
        if (primary.Type != FourCc.Of("iovl"))
            throw new PaintFormatException($"Primary item type '{primary.Type}' is not 'iovl'; this reader only supports iovl-rooted .paint files.");

        var dimgRef = table.References.FirstOrDefault(r => r.Type == FourCc.Of("dimg") && r.FromItemId == primary.Id)
            ?? throw new PaintFormatException("Primary iovl item has no dimg references; cannot resolve layers.");

        var iovlBytes = ResolveItemBytes(primary, table, mdatStart, mdatEnd, reader.BaseStream);
        var iovl = IovlItem.Parse(iovlBytes, dimgRef.ToItemIds.Count);

        var doc = new PaintDocument(iovl.OutputWidth, iovl.OutputHeight)
        {
            CanvasFill = new RgbaColor(
                NarrowFill16(iovl.FillR),
                NarrowFill16(iovl.FillG),
                NarrowFill16(iovl.FillB),
                NarrowFill16(iovl.FillA))
        };

        for (int i = 0; i < dimgRef.ToItemIds.Count; i++)
        {
            uint layerId = dimgRef.ToItemIds[i];
            var layerItem = table.GetItem(layerId);
            if (layerItem.Type != FourCc.Of("unci"))
                throw new PaintFormatException($"Layer item {layerId} has type '{layerItem.Type}'; expected 'unci'.");

            var ispe = table.RequireProperty<Ispe>(layerId);
            var compressed = ResolveItemBytes(layerItem, table, mdatStart, mdatEnd, reader.BaseStream);
            var pixels = UnciItem.DecodePixels(layerId, compressed, table);
            var input = iovl.Inputs[i];

            doc.Layers.Add(new PaintLayer
            {
                Name = layerItem.Name,
                OffsetX = input.OffsetX,
                OffsetY = input.OffsetY,
                Width = ispe.Width,
                Height = ispe.Height,
                PixelsRgba = pixels,
            });
        }

        return doc;
    }

    private static byte[] ResolveItemBytes(ItemEntry item, ItemTable table, long mdatStart, long mdatEnd, Stream src)
    {
        long total = 0;
        foreach (var ext in item.Extents) total += ext.Length;
        if (total > int.MaxValue)
            throw new PaintFormatException($"Item {item.Id} extents total {total} bytes, exceeds Int32.");
        var buf = new byte[total];
        int written = 0;

        switch (item.ConstructionMethod)
        {
            case 0:        // file offset
                if (mdatStart < 0)
                    throw new PaintFormatException("Item references file offsets but no mdat box was found.");
                foreach (var ext in item.Extents)
                {
                    long off = item.BaseOffset + ext.Offset;
                    long end = off + ext.Length;
                    if (off < 0 || end > src.Length)
                        throw new PaintFormatException($"Item {item.Id} extent [{off},{end}) is outside the file.");
                    src.Position = off;
                    int n = ReadFully(src, buf, written, (int)ext.Length);
                    if (n != ext.Length)
                        throw new PaintFormatException($"Short read for item {item.Id}: got {n} of {ext.Length} bytes.");
                    written += n;
                }
                return buf;

            case 1:        // idat
                if (table.IdatBytes.Length == 0)
                    throw new PaintFormatException($"Item {item.Id} construction_method=1 but meta has no idat.");
                foreach (var ext in item.Extents)
                {
                    long off = item.BaseOffset + ext.Offset;
                    long end = off + ext.Length;
                    if (off < 0 || end > table.IdatBytes.Length)
                        throw new PaintFormatException($"Item {item.Id} idat extent [{off},{end}) is outside idat ({table.IdatBytes.Length} bytes).");
                    Array.Copy(table.IdatBytes, off, buf, written, ext.Length);
                    written += (int)ext.Length;
                }
                return buf;

            default:
                throw new PaintFormatException(
                    $"Item {item.Id} construction_method={item.ConstructionMethod} not supported (only 0=file and 1=idat).");
        }
    }

    private static int ReadFully(Stream src, byte[] buf, int offset, int count)
    {
        int total = 0;
        while (total < count)
        {
            int n = src.Read(buf, offset + total, count - total);
            if (n <= 0) break;
            total += n;
        }
        return total;
    }

    private static byte NarrowFill16(ushort v) => (byte)(v >> 8);
}
