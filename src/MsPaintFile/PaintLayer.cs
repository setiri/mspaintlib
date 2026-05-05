namespace MsPaintFile;

public sealed class PaintLayer
{
    public string Name { get; set; } = "";
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public byte[] PixelsRgba { get; set; } = [];
    public bool IsVisible { get; set; } = true;
    public byte Opacity { get; set; } = 255;
}
