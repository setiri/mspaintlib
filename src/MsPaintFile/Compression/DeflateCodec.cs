using System.IO.Compression;

namespace MsPaintFile.Compression;

public static class DeflateCodec
{
    /// <summary>
    /// Compress raw bytes with raw DEFLATE (RFC 1951 — no zlib wrapper).
    /// Matches MS Paint's <c>cmpC</c> algorithm = "defl".
    /// </summary>
    public static byte[] Deflate(ReadOnlySpan<byte> raw, CompressionLevel level = CompressionLevel.Optimal)
    {
        using var ms = new MemoryStream();
        using (var d = new DeflateStream(ms, level, leaveOpen: true))
        {
            d.Write(raw);
        }
        return ms.ToArray();
    }


    /// <summary>
    /// Inflate raw DEFLATE-compressed bytes (RFC 1951 — no zlib header).
    /// Reads at most <paramref name="expectedDecompressedLength"/> bytes from the inflated stream;
    /// throws if the output would exceed that cap (defuses DEFLATE bombs).
    /// </summary>
    public static byte[] Inflate(ReadOnlySpan<byte> compressed, int expectedDecompressedLength)
    {
        if (expectedDecompressedLength < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedDecompressedLength));

        var output = new byte[expectedDecompressedLength];
        using var src = new MemoryStream(compressed.ToArray(), writable: false);
        using var infl = new DeflateStream(src, CompressionMode.Decompress, leaveOpen: false);

        int total = 0;
        while (total < expectedDecompressedLength)
        {
            int n = infl.Read(output.AsSpan(total));
            if (n == 0) break;
            total += n;
        }
        if (total != expectedDecompressedLength)
            throw new PaintFormatException(
                $"DEFLATE inflate produced {total} bytes, expected {expectedDecompressedLength}.");

        // Confirm there isn't more data we'd be silently discarding (a sign of mismatch / bomb).
        Span<byte> tail = stackalloc byte[1];
        int extra = infl.Read(tail);
        if (extra != 0)
            throw new PaintFormatException(
                $"DEFLATE stream produced more than {expectedDecompressedLength} bytes; refusing to over-allocate.");

        return output;
    }
}
