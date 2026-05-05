using MsPaintFile;
using PaintDotNet;

namespace MsPaintFileType;

internal static class SurfaceBlitter
{
    /// <summary>
    /// Copies an RGBA8 source layer (top-down rows) onto a Paint.NET BGRA surface,
    /// positioned at the layer's (OffsetX, OffsetY). Pixels outside the layer rect
    /// are left at the surface's existing value (Surface starts zero-initialized,
    /// i.e. transparent black). Per spec §5.3, BGRA in Paint.NET v5 is straight
    /// (not premultiplied).
    /// </summary>
    /// <summary>
    /// Reads a Paint.NET BGRA surface into a fresh RGBA top-down byte buffer.
    /// </summary>
    public static unsafe byte[] ReadSurfaceAsRgba(Surface surface)
    {
        int w = surface.Width, h = surface.Height;
        var buf = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            ColorBgra* srcRow = surface.GetRowPointerUnchecked(y);
            int dstBase = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                var c = srcRow[x];
                int o = dstBase + x * 4;
                buf[o]     = c.R;
                buf[o + 1] = c.G;
                buf[o + 2] = c.B;
                buf[o + 3] = c.A;
            }
        }
        return buf;
    }

    public static unsafe void BlitRgbaTopDown(Surface surface, PaintLayer layer)
    {
        if (layer.PixelsRgba.Length != layer.Width * layer.Height * 4)
            throw new ArgumentException(
                $"Layer pixel buffer is {layer.PixelsRgba.Length} bytes; expected {layer.Width * layer.Height * 4}.");

        int xStart = Math.Max(0, layer.OffsetX);
        int yStart = Math.Max(0, layer.OffsetY);
        int xEnd = Math.Min(surface.Width, layer.OffsetX + layer.Width);
        int yEnd = Math.Min(surface.Height, layer.OffsetY + layer.Height);
        if (xStart >= xEnd || yStart >= yEnd) return;

        fixed (byte* srcBase = layer.PixelsRgba)
        {
            for (int dstY = yStart; dstY < yEnd; dstY++)
            {
                int srcY = dstY - layer.OffsetY;
                byte* srcRow = srcBase + ((long)srcY * layer.Width * 4);
                ColorBgra* dstRow = surface.GetRowPointerUnchecked(dstY);

                for (int dstX = xStart; dstX < xEnd; dstX++)
                {
                    int srcX = dstX - layer.OffsetX;
                    byte* p = srcRow + (srcX * 4);
                    dstRow[dstX] = ColorBgra.FromBgra(p[2], p[1], p[0], p[3]);
                }
            }
        }
    }
}
