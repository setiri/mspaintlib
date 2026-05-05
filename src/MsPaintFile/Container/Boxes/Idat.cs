namespace MsPaintFile.Container.Boxes;

public static class IdatBox
{
    public static byte[] Parse(BoxReader reader, BoxHeader header)
    {
        reader.Position = header.PayloadStart;
        return reader.ReadBytes(checked((int)header.PayloadLength));
    }
}
