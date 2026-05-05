using MsPaintFile.Container;

namespace MsPaintFile.Properties;

public sealed record Cmpc(FourCc Algorithm, byte CompressedUnitType);
