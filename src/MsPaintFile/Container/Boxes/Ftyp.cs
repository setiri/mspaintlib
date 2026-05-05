namespace MsPaintFile.Container.Boxes;

public sealed record FtypInfo(FourCc MajorBrand, uint MinorVersion, IReadOnlyList<FourCc> CompatibleBrands)
{
    public bool HasBrand(FourCc brand) =>
        MajorBrand == brand || CompatibleBrands.Contains(brand);
}

public static class FtypBox
{
    public static FtypInfo Parse(BoxReader reader, BoxHeader header)
    {
        if (header.PayloadLength < 8)
            throw new PaintFormatException($"ftyp payload {header.PayloadLength} bytes is too short.");
        reader.Position = header.PayloadStart;
        var major = reader.ReadFourCc();
        var minor = reader.ReadU32();

        long compatBytes = header.PayloadEnd - reader.Position;
        if (compatBytes < 0 || compatBytes % 4 != 0)
            throw new PaintFormatException($"ftyp compatible brands area is not a multiple of 4 bytes.");
        var compat = new FourCc[compatBytes / 4];
        for (int i = 0; i < compat.Length; i++)
            compat[i] = reader.ReadFourCc();
        return new FtypInfo(major, minor, compat);
    }
}
