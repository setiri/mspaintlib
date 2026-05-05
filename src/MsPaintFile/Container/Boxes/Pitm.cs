namespace MsPaintFile.Container.Boxes;

public static class PitmBox
{
    public static uint Parse(BoxReader reader, BoxHeader header)
    {
        reader.Position = header.PayloadStart;
        var (version, _) = reader.ReadFullBoxHeader();
        return version == 0 ? reader.ReadU16() : reader.ReadU32();
    }
}
