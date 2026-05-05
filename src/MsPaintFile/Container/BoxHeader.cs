namespace MsPaintFile.Container;

public readonly record struct BoxHeader(
    FourCc Type,
    long HeaderStart,
    long PayloadStart,
    long PayloadLength)
{
    public long PayloadEnd => PayloadStart + PayloadLength;
    public long BoxEnd => PayloadEnd;
}
