using MsPaintFile.Compression;
using MsPaintFile.Container;
using MsPaintFile.Properties;

namespace MsPaintFile.Items;

public static class UnciItem
{
    private const ushort ChannelR = 4;
    private const ushort ChannelG = 5;
    private const ushort ChannelB = 6;
    private const ushort ChannelA = 7;

    /// <summary>
    /// Validates an `unci` item's properties and decodes its pixels into RGBA8 bytes
    /// (top-down rows, no padding). The on-disk byte order is whatever cmpd+uncC declare —
    /// the sample we have stores BGRA, despite spec §3.3 implying RGBA. We swizzle to RGBA.
    /// </summary>
    public static byte[] DecodePixels(uint itemId, ReadOnlySpan<byte> compressed, ItemTable table)
    {
        var ispe = table.RequireProperty<Ispe>(itemId);
        var cmpc = table.RequireProperty<Cmpc>(itemId);
        var pixi = table.RequireProperty<Pixi>(itemId);
        var cmpd = table.RequireProperty<Cmpd>(itemId);
        var uncc = table.RequireProperty<UncC>(itemId);
        var colr = table.RequireProperty<Colr>(itemId);

        ValidateCompression(cmpc, itemId);
        ValidatePixi(pixi, itemId);
        ValidateColr(colr, itemId);
        var swizzle = BuildRgbaSwizzle(cmpd, uncc, itemId);

        long expected = (long)ispe.Width * ispe.Height * 4;
        if (expected > int.MaxValue)
            throw new PaintFormatException($"Item {itemId} pixel buffer would exceed Int32 (w={ispe.Width}, h={ispe.Height}).");

        var raw = DeflateCodec.Inflate(compressed, (int)expected);
        ApplySwizzleInPlace(raw, swizzle);
        return raw;
    }

    public static void ValidateCompression(Cmpc cmpc, uint itemId)
    {
        if (cmpc.Algorithm != FourCc.Of("defl"))
        {
            string hint = cmpc.Algorithm == FourCc.Of("zlib")
                ? " — note: 'zlib' is wrapped, this reader only handles raw DEFLATE ('defl')."
                : "";
            throw new PaintFormatException(
                $"Item {itemId} cmpC.algorithm '{cmpc.Algorithm}' not supported (only 'defl').{hint}");
        }
    }

    public static void ValidatePixi(Pixi pixi, uint itemId)
    {
        if (pixi.BitsPerChannel.Count != 4 ||
            pixi.BitsPerChannel[0] != 8 || pixi.BitsPerChannel[1] != 8 ||
            pixi.BitsPerChannel[2] != 8 || pixi.BitsPerChannel[3] != 8)
        {
            throw new PaintFormatException(
                $"Item {itemId} pixi must be 4 channels of 8 bits each; got [{string.Join(",", pixi.BitsPerChannel)}].");
        }
    }

    public static void ValidateColr(Colr colr, uint itemId)
    {
        if (colr.Primaries != 1 || colr.Transfer != 13 || colr.Matrix != 0 || !colr.FullRange)
        {
            throw new PaintFormatException(
                $"Item {itemId} colr must be sRGB (primaries=1, transfer=13, matrix=0, full_range=1); " +
                $"got primaries={colr.Primaries}, transfer={colr.Transfer}, matrix={colr.Matrix}, full_range={colr.FullRange}.");
        }
    }

    /// <summary>
    /// Returns an int[4] where index = output RGBA slot (0=R,1=G,2=B,3=A) and value = input byte index.
    /// Returns null if the input is already RGBA in memory (no swizzle needed).
    /// </summary>
    internal static int[]? BuildRgbaSwizzle(Cmpd cmpd, UncC uncc, uint itemId)
    {
        if (cmpd.Components.Count < 4)
            throw new PaintFormatException($"Item {itemId} cmpd declares {cmpd.Components.Count} components; need at least 4.");
        if (uncc.Components.Count != 4)
            throw new PaintFormatException($"Item {itemId} uncC must reference 4 components; got {uncc.Components.Count}.");
        if (uncc.Interleave != 1)
            throw new PaintFormatException($"Item {itemId} uncC interleave={uncc.Interleave}; only 1 (pixel-interleaved) is supported.");
        if (uncc.SamplingType != 0)
            throw new PaintFormatException($"Item {itemId} uncC sampling_type={uncc.SamplingType}; only 0 (no sub-sampling) is supported.");

        int[] positions = new int[4]; // [R, G, B, A] → input byte index
        bool[] seen = new bool[4];

        for (int byteIdx = 0; byteIdx < 4; byteIdx++)
        {
            var c = uncc.Components[byteIdx];
            if (c.BitDepthMinusOne != 7)
                throw new PaintFormatException(
                    $"Item {itemId} uncC component {byteIdx} bit_depth_minus_one={c.BitDepthMinusOne}; only 7 (8-bit) is supported.");
            if (c.ComponentFormat != 0)
                throw new PaintFormatException(
                    $"Item {itemId} uncC component {byteIdx} format={c.ComponentFormat}; only 0 (unsigned int) is supported.");
            if (c.ComponentIndex >= cmpd.Components.Count)
                throw new PaintFormatException(
                    $"Item {itemId} uncC component {byteIdx} index {c.ComponentIndex} is outside cmpd range ({cmpd.Components.Count}).");

            ushort channel = cmpd.Components[c.ComponentIndex].ComponentType;
            int slot = channel switch
            {
                ChannelR => 0,
                ChannelG => 1,
                ChannelB => 2,
                ChannelA => 3,
                _ => throw new PaintFormatException(
                    $"Item {itemId} cmpd component {c.ComponentIndex} type {channel} is not R/G/B/Alpha (4/5/6/7)."),
            };
            if (seen[slot])
                throw new PaintFormatException($"Item {itemId} channel {channel} appears more than once in uncC.");
            seen[slot] = true;
            positions[slot] = byteIdx;
        }
        if (!seen[0] || !seen[1] || !seen[2] || !seen[3])
            throw new PaintFormatException($"Item {itemId} uncC/cmpd components do not cover all of R, G, B, Alpha.");

        if (positions[0] == 0 && positions[1] == 1 && positions[2] == 2 && positions[3] == 3)
            return null; // already RGBA
        return positions;
    }

    private static void ApplySwizzleInPlace(byte[] pixels, int[]? swizzle)
    {
        if (swizzle is null) return;
        int r = swizzle[0], g = swizzle[1], b = swizzle[2], a = swizzle[3];
        for (int o = 0; o + 4 <= pixels.Length; o += 4)
        {
            byte br = pixels[o + r];
            byte bg = pixels[o + g];
            byte bb = pixels[o + b];
            byte ba = pixels[o + a];
            pixels[o]     = br;
            pixels[o + 1] = bg;
            pixels[o + 2] = bb;
            pixels[o + 3] = ba;
        }
    }
}
