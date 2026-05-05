namespace MsPaintFile.Properties;

public sealed record CmpdComponent(ushort ComponentType, byte ComponentTypeUriPresent);

public sealed record Cmpd(IReadOnlyList<CmpdComponent> Components);
