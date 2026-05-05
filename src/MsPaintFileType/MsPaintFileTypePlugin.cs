using MsPaintFile;
using PaintDotNet;

[assembly: PluginSupportInfo<MsPaintFileType.PluginSupportInfo>]

namespace MsPaintFileType;

public sealed class MsPaintFileTypePlugin : FileType
{
    public MsPaintFileTypePlugin()
        : base(
            "MS Paint Project",
            new FileTypeOptions
            {
                LoadExtensions = [".paint"],
                SaveExtensions = [".paint"],
                SupportsLayers = true,
                SupportsCancellation = true,
            })
    {
    }

    protected override Document OnLoad(Stream input)
    {
        var paintDoc = PaintDocument.Load(input);
        var pdnDoc = new Document(paintDoc.Width, paintDoc.Height);
        pdnDoc.CustomHeaders = PaintHeaderCodec.EmbedCanvasFill(pdnDoc.CustomHeaders, paintDoc.CanvasFill);

        for (int i = 0; i < paintDoc.Layers.Count; i++)
        {
            var src = paintDoc.Layers[i];
            var bitmapLayer = new BitmapLayer(paintDoc.Width, paintDoc.Height);
            bitmapLayer.Name = string.IsNullOrEmpty(src.Name) ? $"Layer {i + 1}" : src.Name;
            bitmapLayer.Visible = src.IsVisible;
            bitmapLayer.Opacity = src.Opacity;
            bitmapLayer.IsBackground = i == 0;
            SurfaceBlitter.BlitRgbaTopDown(bitmapLayer.Surface, src);
            pdnDoc.Layers.Add(bitmapLayer);
        }
        return pdnDoc;
    }

    protected override void OnSave(
        Document input,
        Stream output,
        SaveConfigToken token,
        Surface scratchSurface,
        ProgressEventHandler progressCallback)
    {
        var paintDoc = new PaintDocument(input.Width, input.Height)
        {
            CanvasFill = PaintHeaderCodec.ExtractCanvasFill(input.CustomHeaders) ?? RgbaColor.OpaqueWhite,
        };

        for (int i = 0; i < input.Layers.Count; i++)
        {
            if (input.Layers[i] is not BitmapLayer bitmapLayer)
                throw new NotSupportedException($"Layer {i} is not a BitmapLayer; only bitmap layers are supported.");

            paintDoc.Layers.Add(new PaintLayer
            {
                Name = bitmapLayer.Name ?? "",
                Width = input.Width,
                Height = input.Height,
                OffsetX = 0,
                OffsetY = 0,
                PixelsRgba = SurfaceBlitter.ReadSurfaceAsRgba(bitmapLayer.Surface),
                IsVisible = bitmapLayer.Visible,
                Opacity = bitmapLayer.Opacity,
            });
            progressCallback?.Invoke(this, new ProgressEventArgs(((double)(i + 1) / input.Layers.Count) * 100));
        }

        paintDoc.Save(output);
    }
}
