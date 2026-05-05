using System.Globalization;
using MsPaintFile;

namespace MsPaintFileType;

/// <summary>
/// Round-trips canvas-level metadata (currently just the canvas fill color) through
/// Paint.NET's <c>Document.CustomHeaders</c> string, so that when a user saves a .paint
/// as .pdn and reopens it later — or when we add Phase 2 save support — the data we
/// can't represent as a Paint.NET layer survives.
/// </summary>
internal static class PaintHeaderCodec
{
    private const string CanvasFillKey = "mspaintlib.canvasFill";

    public static string EmbedCanvasFill(string? existing, RgbaColor fill)
    {
        var lines = SplitLines(existing).Where(l => !l.StartsWith(CanvasFillKey + "=", StringComparison.Ordinal));
        var withOurs = lines.Append($"{CanvasFillKey}={fill.R},{fill.G},{fill.B},{fill.A}");
        return string.Join('\n', withOurs);
    }

    public static RgbaColor? ExtractCanvasFill(string? existing)
    {
        foreach (var line in SplitLines(existing))
        {
            if (!line.StartsWith(CanvasFillKey + "=", StringComparison.Ordinal)) continue;
            var rhs = line.AsSpan(CanvasFillKey.Length + 1);
            var parts = rhs.ToString().Split(',');
            if (parts.Length != 4) return null;
            if (byte.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) &&
                byte.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var g) &&
                byte.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var b) &&
                byte.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var a))
            {
                return new RgbaColor(r, g, b, a);
            }
            return null;
        }
        return null;
    }

    private static IEnumerable<string> SplitLines(string? s) =>
        string.IsNullOrEmpty(s) ? [] : s.Split('\n');
}
