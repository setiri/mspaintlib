using MsPaintFile.Container;

namespace MsPaintFile.Properties;

public sealed record Colr(FourCc ColourType, ushort Primaries, ushort Transfer, ushort Matrix, bool FullRange);
