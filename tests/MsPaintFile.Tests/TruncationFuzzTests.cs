using FluentAssertions;
using MsPaintFile;
using Xunit;

namespace MsPaintFile.Tests;

public class TruncationFuzzTests
{
    [SkippableFact]
    public void Random_truncations_throw_PaintFormatException_only()
    {
        var files = TestSamples.Files().ToList();
        Skip.IfNot(files.Count > 0, "No .paint samples in samples/.");

        foreach (var path in files)
        {
            var bytes = File.ReadAllBytes(path);
            var rng = new Random((int)(path.GetHashCode() ^ bytes.Length));
            for (int i = 0; i < 32; i++)
            {
                long cut = rng.NextInt64(0, bytes.Length);
                AssertCleanFailureOrSuccess(bytes, cut, Path.GetFileName(path));
            }
        }
    }

    private static void AssertCleanFailureOrSuccess(byte[] bytes, long cut, string fileName)
    {
        var truncated = new byte[cut];
        Array.Copy(bytes, truncated, cut);

        try
        {
            using var ms = new MemoryStream(truncated);
            _ = PaintDocument.Load(ms);
            // Truncation may incidentally land at a coherent boundary; not necessarily a bug.
        }
        catch (PaintFormatException) { /* expected */ }
        catch (Exception ex)
        {
            ex.Should().BeOfType<PaintFormatException>(
                $"truncation at byte {cut} of {fileName} produced {ex.GetType().Name}: {ex.Message}");
        }
    }
}
