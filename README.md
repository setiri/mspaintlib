# mspaintlib

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

A .NET library for reading and writing MS Paint's `.paint` project files,
plus a Paint.NET v5 file-type plugin that uses it.

`.paint` is the new multi-layer project format that ships in MS Paint Insider
preview builds (Windows 11, ~11.2508+). It's a HEIF/MIAF container with
DEFLATE-compressed RGBA layer items and an "image overlay" composition. The
format is undocumented by Microsoft as of this writing; this project is
reverse-engineered from sample files. Files written by this library re-open
cleanly in MS Paint, with all layers and the canvas background color intact.

## Background

In late 2025 Microsoft started rolling out a layer system in MS Paint for
Windows 11, along with a new `.paint` project format that preserves the layer
stack. Up to that point MS Paint had only offered single-image formats (BMP,
PNG, JPG). The new format is built on real ISO standards (HEIF, MIAF,
ISOBMFF, "uncompressed video/images in ISOBMFF") rather than being
proprietary, but Microsoft hasn't published a spec, and as of writing no
common image library — including `libheif` — fully supports the specific
combination of `unci` (uncompressed) items and `iovl` (overlay) composition
that MS Paint emits. So `.paint` files are effectively orphans: only MS Paint
itself can open them.

This project closes that gap. It ships:

- A standalone .NET library (no native dependencies, no `libheif`) that reads
  and writes the format.
- A Paint.NET v5 file-type plugin that wires the library into Paint.NET's
  Open and Save dialogs.
- A reverse-engineered [format reference](PAINT-FORMAT-SPEC.md) describing
  the on-disk layout, property semantics, and the gotchas you'll hit if you
  try to write your own reader.

A longer write-up of the reverse-engineering process is published at
[https://bradymoritz.com](https://www.bradymoritz.com/mspaintlib-a-net-library-for-the-new-ms-paint-paint-file-format).

## Status

| Capability                                                          | Status |
| ------------------------------------------------------------------- | ------ |
| Read `.paint` (canvas size, layers, pixels, canvas fill color)      | ✅     |
| Write `.paint` that re-opens in MS Paint with layers intact         | ✅     |
| Paint.NET v5 plugin (Open + Save)                                   | ✅     |
| Round-trip preservation of canvas background fill                   | ✅     |
| Round-trip of layer names, visibility, opacity                      | ⏳     |
| Layers smaller than canvas (offset / cropped layer items)           | ⏳     |
| Thumbnails, blend modes, masks                                      | ⏳     |

⏳ items are tracked in the [spec's open questions](PAINT-FORMAT-SPEC.md#34-open-questions-single-sample-reverse-engineering)
and will be filled in as Microsoft documents the format or as samples
exhibiting those features can be collected.

The format is in **Insider preview** — Microsoft may change it before the
stable release. The library pins what it expects and will throw a
`PaintFormatException` (rather than silently misparse) when it sees
something it doesn't recognize.

## Note on the canvas background color

MS Paint's `.paint` format stores a canvas background color as a
document-level property — you can see it behind transparent layer pixels
in MS Paint, but it isn't a layer and isn't directly editable. Paint.NET
has no equivalent: its canvas under the bottom layer is always transparent
(rendered as the standard checkerboard).

Today the plugin handles this by **leaving the background visually
transparent in Paint.NET**, which is the simplest behavior and avoids
introducing a fake layer the user might edit. The color itself is **not
discarded** — the plugin stashes it in Paint.NET's `Document.CustomHeaders`
on load and reads it back on save, so a full round-trip
(MS Paint → Paint.NET → MS Paint) preserves the original background color.

Other plausible behaviors:

- Synthesize a non-editable bottom layer filled with the background color,
  to match what MS Paint shows visually.
- Synthesize an editable bottom layer (and convert it back to a canvas
  property on save by detecting "is the bottom layer a solid color?" — a
  fragile heuristic).
- Surface the background color as a Paint.NET document property accessible
  from a custom dialog.

Each has trade-offs around editability, round-trip fragility, and surprise
factor. If you have a strong preference for one, open an issue and let's
discuss — happy to revisit if the community lands on a clearer answer.

## Install — Paint.NET plugin

1. Download a release ZIP (or build from source — see below).
2. Drop both `MsPaintFile.dll` and `MsPaintFileType.dll` into Paint.NET's
   FileTypes folder, typically:
   ```
   C:\Program Files\paint.net\FileTypes\
   ```
   (You'll need administrator rights to write there.)
3. Restart Paint.NET. `.paint` should now appear in File → Open and File →
   Save As.

## Library usage

The library is pure .NET 8, no native dependencies.

```csharp
using MsPaintFile;

// Read
var doc = PaintDocument.Load("project.paint");
Console.WriteLine($"{doc.Width}x{doc.Height}, {doc.Layers.Count} layers");
foreach (var layer in doc.Layers)
{
    // layer.PixelsRgba is RGBA8, top-down rows, no padding
    // (the on-disk byte order is BGRA; the library swizzles for you)
    Console.WriteLine($"  layer: {layer.Width}x{layer.Height} at ({layer.OffsetX}, {layer.OffsetY})");
}

// Write
var newDoc = new PaintDocument(width: 800, height: 600)
{
    CanvasFill = new RgbaColor(255, 255, 255, 255),
};
newDoc.Layers.Add(new PaintLayer
{
    Width = 800,
    Height = 600,
    PixelsRgba = somePixelBuffer,    // 800 * 600 * 4 bytes
});
newDoc.Save("output.paint");
```

## Format reference

[PAINT-FORMAT-SPEC.md](PAINT-FORMAT-SPEC.md) is the on-disk format
reference. It documents:

- Top-level box layout (`ftyp`, `meta`, `free`, `mdat`).
- Item types (`unci` for layers, `iovl` for the composition) and how they
  reference each other via `iref dimg`.
- Required properties on each item: `cmpC` / `ispe` / `colr` / `cmpd` /
  `uncC` / `pixi`, with the exact byte values MS Paint emits.
- The `iovl` payload (canvas size, fill color, per-layer offsets).
- Pixel byte ordering — note that the on-disk order is **BGRA**, despite
  what the channel registry might suggest. See §3.3.
- Open questions and known unknowns.

Pull requests against the spec are welcome — especially if you have samples
exhibiting features we don't yet support (layer names, hidden layers, etc.).

## Project structure

```
src/MsPaintFile/         The library — read/write .paint, no Paint.NET dependency
src/MsPaintFileType/     The Paint.NET v5 file-type plugin (depends on MsPaintFile)
tests/MsPaintFile.Tests/ xUnit tests, including round-trip + truncation fuzz
samples/                 .paint fixtures used by the integration tests
tools/                   Dev utilities (a reflection-based DLL inspector and
                         a `dump` mode for printing a .paint file's box tree)
PAINT-FORMAT-SPEC.md     The on-disk format reference
```

## Building from source

Requirements:

- .NET 8 SDK (or newer — the library targets `net8.0`, the plugin targets
  `net9.0-windows` to match Paint.NET v5's runtime).
- Paint.NET v5 installed (the plugin project references its DLLs from the
  install directory).

```sh
dotnet build mspaintlib.slnx
dotnet test mspaintlib.slnx
```

The plugin's references resolve via a `PaintDotNetPath` MSBuild property
defined in `Directory.Build.props` (defaulting to
`C:\Program Files\paint.net`). Override per-developer with an environment
variable or `/p:PaintDotNetPath=...` if your install lives elsewhere.

To deploy the freshly built plugin to Paint.NET:

```sh
dotnet build src/MsPaintFileType/MsPaintFileType.csproj -c Release
# Then copy MsPaintFileType.dll and MsPaintFile.dll from
# src/MsPaintFileType/bin/Release/net9.0-windows/ to the FileTypes folder.
```

To inspect the structure of a `.paint` file (handy when reverse-engineering
new variants):

```sh
dotnet run --project tools -- dump path/to/your.paint
```

## Roadmap

- **Fidelity polish** — locate where MS Paint stores layer names, hidden
  layers, and per-layer opacity, and round-trip them through. Requires
  collecting samples that exhibit each.
- **Layers smaller than canvas** — relax the canvas-sized constraint in the
  writer; carry per-layer `ispe` properties through ipco/ipma.
- **NuGet package** for `MsPaintFile` — the library is independently useful
  for anyone reading `.paint` from a build pipeline, server, or script.
- **Plugin release packaging** — a published ZIP with install instructions,
  ideally posted on the Paint.NET forum.

## Contributing

Issues and pull requests welcome. Useful contributions:

- **Sample files**: drop a `.paint` into `samples/` (especially anything
  with renamed/hidden layers, layers smaller than the canvas, or unusual
  channel configurations).
- **Format corrections**: if your sample makes the parser throw a
  `PaintFormatException`, that's a spec gap — please file an issue with
  the file attached.
- **Other host integrations**: GIMP, Krita, paint.net 4.x, etc. The
  `MsPaintFile` library is host-agnostic.

## License

[MIT](LICENSE) — Copyright © 2026 [Setiri LLC](https://setiri.com).

This project is independent and unaffiliated with Microsoft or Paint.NET.
"MS Paint" and "Paint.NET" are trademarks of their respective owners.
