using System.IO.Compression;
using FluentAssertions;
using MsPaintFile;
using MsPaintFile.Compression;

namespace MsPaintFile.Tests;

public class UnciDecodeTests
{
    [Fact]
    public void Round_trips_a_2x2_rgba_pattern_via_raw_deflate()
    {
        // 2x2 RGBA, top-down, no padding
        byte[] pixels =
        [
            255, 0,   0,   255,    // (0,0) red
            0,   255, 0,   255,    // (1,0) green
            0,   0,   255, 255,    // (0,1) blue
            255, 255, 255, 128,    // (1,1) translucent white
        ];
        var compressed = RawDeflateCompress(pixels);

        var roundTripped = DeflateCodec.Inflate(compressed, pixels.Length);
        roundTripped.Should().BeEquivalentTo(pixels);
    }

    [Fact]
    public void Throws_when_decompressed_size_does_not_match_expected()
    {
        byte[] pixels = new byte[16];
        var compressed = RawDeflateCompress(pixels);
        Action act = () => DeflateCodec.Inflate(compressed, expectedDecompressedLength: 32);
        act.Should().Throw<PaintFormatException>();
    }

    [Fact]
    public void Throws_when_decompressed_data_exceeds_expected()
    {
        byte[] pixels = new byte[64];
        var compressed = RawDeflateCompress(pixels);
        Action act = () => DeflateCodec.Inflate(compressed, expectedDecompressedLength: 16);
        act.Should().Throw<PaintFormatException>().WithMessage("*more than 16 bytes*");
    }

    private static byte[] RawDeflateCompress(byte[] input)
    {
        using var ms = new MemoryStream();
        using (var d = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            d.Write(input, 0, input.Length);
        }
        return ms.ToArray();
    }
}
