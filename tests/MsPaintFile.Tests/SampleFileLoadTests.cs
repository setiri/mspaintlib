using FluentAssertions;
using MsPaintFile;
using Xunit;

namespace MsPaintFile.Tests;

public class SampleFileLoadTests
{
    [SkippableFact]
    public void Loads_every_sample_in_samples_dir_without_throwing()
    {
        var files = TestSamples.Files().ToList();
        Skip.IfNot(files.Count > 0, "No .paint samples in samples/. Drop one in to enable integration tests.");

        foreach (var path in files)
        {
            var doc = PaintDocument.Load(path);
            doc.Width.Should().BeGreaterThan(0, $"in {Path.GetFileName(path)}");
            doc.Height.Should().BeGreaterThan(0, $"in {Path.GetFileName(path)}");
            doc.Layers.Should().NotBeEmpty($"in {Path.GetFileName(path)}");
            foreach (var layer in doc.Layers)
            {
                layer.Width.Should().BeGreaterThan(0);
                layer.Height.Should().BeGreaterThan(0);
                layer.PixelsRgba.Length.Should().Be(layer.Width * layer.Height * 4);
            }
        }
    }
}

internal static class TestSamples
{
    public static string Dir => Path.Combine(AppContext.BaseDirectory, "samples");

    public static IEnumerable<string> Files() =>
        Directory.Exists(Dir) ? Directory.EnumerateFiles(Dir, "*.paint") : [];
}
