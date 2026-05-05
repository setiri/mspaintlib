using FluentAssertions;
using MsPaintFile;
using Xunit;

namespace MsPaintFile.Tests;

public class RoundTripTests
{
    [SkippableFact]
    public void Load_save_load_preserves_all_layers_and_metadata()
    {
        var files = TestSamples.Files().ToList();
        Skip.IfNot(files.Count > 0, "No .paint samples in samples/.");

        foreach (var path in files)
        {
            var original = PaintDocument.Load(path);

            using var ms = new MemoryStream();
            original.Save(ms);
            ms.Position = 0;
            var reloaded = PaintDocument.Load(ms);

            reloaded.Width.Should().Be(original.Width, $"in {Path.GetFileName(path)}");
            reloaded.Height.Should().Be(original.Height, $"in {Path.GetFileName(path)}");
            reloaded.CanvasFill.Should().Be(original.CanvasFill, $"in {Path.GetFileName(path)}");
            reloaded.Layers.Should().HaveCount(original.Layers.Count, $"in {Path.GetFileName(path)}");

            for (int i = 0; i < original.Layers.Count; i++)
            {
                var a = original.Layers[i];
                var b = reloaded.Layers[i];
                b.Width.Should().Be(a.Width, $"layer {i} in {Path.GetFileName(path)}");
                b.Height.Should().Be(a.Height, $"layer {i} in {Path.GetFileName(path)}");
                b.OffsetX.Should().Be(a.OffsetX);
                b.OffsetY.Should().Be(a.OffsetY);
                b.PixelsRgba.Should().BeEquivalentTo(a.PixelsRgba, o => o.WithStrictOrdering(),
                    $"layer {i} pixels in {Path.GetFileName(path)}");
            }
        }
    }
}
