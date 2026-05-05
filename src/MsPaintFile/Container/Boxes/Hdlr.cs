namespace MsPaintFile.Container.Boxes;

public static class HdlrBox
{
    public static FourCc ReadHandlerType(BoxReader reader, BoxHeader header)
    {
        reader.Position = header.PayloadStart;
        _ = reader.ReadFullBoxHeader();
        _ = reader.ReadU32();              // pre_defined
        var handler = reader.ReadFourCc(); // handler_type
        return handler;
    }
}
