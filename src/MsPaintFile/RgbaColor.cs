namespace MsPaintFile;

public readonly record struct RgbaColor(byte R, byte G, byte B, byte A)
{
    public static RgbaColor OpaqueWhite => new(255, 255, 255, 255);
}
