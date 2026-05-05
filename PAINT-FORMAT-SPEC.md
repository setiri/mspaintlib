# `.paint` Format Library + Paint.NET Plugin — Engineering Spec

**Status:** Draft v0.2 — corrected pixel byte order (§3.3) after validating against a second sample. v0.1 incorrectly asserted RGBA on disk; the actual order is determined by `cmpd`+`uncC` and is BGRA in observed samples. The format is in Insider preview and may change before stable release; see *Versioning & Risk*.

---

## 1. Background

MS Paint's `.paint` project file is a **HEIF / MIAF container** (ISOBMFF). It is **not** a proprietary format — it composes existing ISO standards:

| Layer | Standard |
|---|---|
| Container | ISO/IEC 14496-12 — ISO Base Media File Format |
| HEIF profile | ISO/IEC 23008-12 — image items, derivations, references |
| MIAF profile | ISO/IEC 23000-22 — application format constraints |
| Pixel storage | ISO/IEC 23001-17 — uncompressed video/images in ISOBMFF (`unci` item type, `cmpC`/`uncC`/`cmpd`/`pixi` properties) |

Layers are stored as `unci` (uncompressed image) items. Pixel data is RGBA8 sRGB, run through raw DEFLATE (`cmpC` algorithm `defl`). The composition is a single `iovl` (ImageOverlay) item, which is the file's primary item and references each layer via an `iref dimg` ("derived image") link.

This means a sufficiently complete HEIF reader can already parse the file in principle. In practice (as of libheif 1.17), `unci` and `iovl` support is still maturing, so we will write a focused reader rather than depend on a third-party HEIF library.

---

## 2. Goals & Non-Goals

**Goals**
- A standalone .NET library (`MsPaintFile`) that reads and writes `.paint` files round-trip, with no native dependencies.
- A Paint.NET v5 file-type plugin that uses the library to import `.paint` files as multi-layer `Document` instances and (Phase 2) export them.
- Preserve the on-disk byte layout produced by MS Paint as closely as possible — files written by our library should re-open cleanly in MS Paint.

**Non-goals (initial release)**
- Full HEIF/HEIC compatibility (reading arbitrary HEIFs from cameras, etc.). We support the specific subset MS Paint emits.
- AVIF, HEVC, or any compressed image item types. Layers are uncompressed `unci` only.
- Animation, multi-track, or video-style ISOBMFF features.
- Anything Microsoft has not yet shipped (e.g. blend modes, masks).

---

## 3. Format Reference

### 3.1 Top-level structure (observed)

```
ftyp        major=mif1, minor=0, compat=[mif1, gcmi, isoa, miaf]
meta
  ├─ hdlr   handler=pict
  ├─ pitm   primary_item_id = <iovl item id>
  ├─ iinf   N item info entries (one per layer + one for the iovl)
  │    └─ infe v2  type=unci   for each layer
  │    └─ infe v2  type=iovl   for the composition
  ├─ iloc   v1 (offset_size=4, length_size=4, base_offset_size=0)
  ├─ iprp
  │    ├─ ipco   shared properties:
  │    │    ├─ cmpC      compression_algorithm = "defl" (raw DEFLATE)
  │    │    ├─ ispe      width, height
  │    │    ├─ colr nclx primaries=1 (BT.709/sRGB), transfer=13 (sRGB), matrix=0, full_range
  │    │    ├─ cmpd      component count + types (4=R, 5=G, 6=B, 7=Alpha)
  │    │    ├─ uncC      profile="gene", interleave/sampling per ISO/IEC 23001-17
  │    │    └─ pixi      4 channels × 8 bits
  │    └─ ipma   per-item property associations (mandatory bit set on essentials)
  ├─ iref
  │    └─ dimg  from_item = iovl, to_items = [layer1, layer2, ...]   (in z-order, bottom-to-top)
  └─ idat   inline data for the iovl item
free        padding (variable size)
mdat        layer pixel data (concatenated DEFLATE streams)
```

### 3.2 `iovl` (ImageOverlay) payload

Stored inline in `idat`. Layout per ISO/IEC 23008-12 §6.6.2:

```
u8  version
u8  flags                    // bit 0 set => 32-bit dimensions/offsets, else 16-bit
u16 canvas_fill_value[4]     // RGBA, 0xFFFF = max for that channel
u16 (or u32) output_width
u16 (or u32) output_height
for each input image (z-order from iref dimg):
    s16 (or s32) horizontal_offset
    s16 (or s32) vertical_offset
```

In our sample: `version=0, flags=0, fill=(0xFFFF,0xFFFF,0xFFFF,0xFFFF), output=333×123`, both layers at `(0, 0)`.

### 3.3 Layer pixel data (`unci` items)

Layout of each item's bytes (after deflate):

```
4 bytes per pixel, packed pixel-interleaved, top-down rows, no row padding.
length = width * height * 4
```

The byte order within each pixel is **declared by `cmpd`+`uncC`, not fixed**. In our sample the order on disk is **BGRA** (uncC component 0 → cmpd[2]=B, component 1 → cmpd[1]=G, component 2 → cmpd[0]=R, component 3 → cmpd[3]=A), even though `cmpd` lists the channels in R,G,B,A order. A correct reader must consult `uncC.component[i].component_index` to map each input byte position to its `cmpd` channel, then permute to whatever order it wants to expose. Do not hard-code RGBA or BGRA based on the `cmpd` listing alone.

Compression: `cmpC` with algorithm `defl` means **raw DEFLATE** (RFC 1951), not the zlib wrapper. In .NET use `DeflateStream` — not `ZLibStream`.

### 3.4 Open questions (single-sample reverse engineering)

These are unknowns that real samples must answer before we ship Phase 1:

1. **Layer names.** Our sample stores no per-layer name. Candidates: `udta`/`udes` user-data box, an extra item property, a top-level extension box, or `infe.item_name` (currently empty). Need samples with layers renamed in MS Paint UI to confirm.
2. **Per-layer visibility / opacity.** Likely encoded either via an extension property or via the iovl flags (none of which are set in our sample).
3. **Blend modes.** Our sample uses straight overlay (effectively "normal" at full opacity). Microsoft's UI does not currently expose blend modes, so this may simply not exist yet.
4. **Layers smaller than canvas.** Per spec, each `unci` item can have its own `ispe`. We've only seen layers sized to the canvas. The reader must not assume canvas-sized layers.
5. **Pixel byte ordering.** The `cmpd`/`uncC`/`pixi` triple declares the byte order. Our sample stores **BGRA** on disk (see §3.3). A correct reader builds a swizzle table from `uncC.component_index` → `cmpd` channel and converts to its preferred output order. Reject (or extend) anything outside 8-bit R/G/B/Alpha until we have samples to support it.
6. **Thumbnail item.** None present. Microsoft may add one — readers should tolerate non-primary `unci` items not referenced by the iovl.
7. **`gcmi` and `isoa` brand semantics.** Not in the public ISO brand registry as of this writing. Treat as informational; do not gate on them.

---

## 4. .NET Library — `MsPaintFile`

### 4.1 Project layout

```
src/
  MsPaintFile/
    MsPaintFile.csproj          # net8.0; netstandard2.1 multi-target if needed for plugin
    PaintDocument.cs            # public top-level type
    PaintLayer.cs               # public layer type
    Container/
      BoxReader.cs              # streaming ISOBMFF box parser
      BoxWriter.cs              # box serializer
      Boxes/                    # one file per box type we handle
    Items/
      UnciItem.cs
      IovlItem.cs
    Properties/
      Cmpc.cs, Ispe.cs, Colr.cs, Cmpd.cs, UncC.cs, Pixi.cs
    Compression/
      DeflateCodec.cs
tests/
  MsPaintFile.Tests/            # round-trip + golden-file tests
samples/
  *.paint                       # checked-in fixtures
```

Target framework: `net8.0` for the lib, with `netstandard2.0` as a secondary target to maximize Paint.NET plugin compatibility (Paint.NET 5.x runs on .NET 8/9 but plugins usually target a lower bar). Final TFM list locked once we test against a real Paint.NET install.

No native dependencies. Pure managed.

### 4.2 Public API

```csharp
namespace MsPaintFile;

public sealed class PaintDocument
{
    public int Width { get; }
    public int Height { get; }
    public IList<PaintLayer> Layers { get; }   // bottom-to-top z-order
    public RgbaColor CanvasFill { get; set; }  // default opaque white

    public PaintDocument(int width, int height);

    public static PaintDocument Load(Stream stream);
    public static PaintDocument Load(string path);
    public void Save(Stream stream);
    public void Save(string path);
}

public sealed class PaintLayer
{
    public string Name { get; set; } = "";              // best-effort; may be empty
    public int OffsetX { get; set; }                    // top-left of layer on canvas
    public int OffsetY { get; set; }
    public int Width { get; set; }                      // layer pixel width
    public int Height { get; set; }
    public byte[] PixelsRgba { get; set; } = [];        // length = Width * Height * 4
    public bool IsVisible { get; set; } = true;         // surfaced if/when format supports it
    public byte Opacity { get; set; } = 255;            // surfaced if/when format supports it
}

public readonly record struct RgbaColor(byte R, byte G, byte B, byte A);
```

### 4.3 Read pipeline

1. Walk top-level boxes, locate `ftyp` and validate brand `mif1` is in compat list.
2. Walk `meta` and build an item table: `id → (type, properties[], extents[])`.
3. Find primary item (`pitm`); require type `iovl`. Reject otherwise with a descriptive exception.
4. Parse the iovl payload (resolved via `iloc` against `idat` or `mdat`) to get canvas dimensions, fill, and per-input offsets.
5. Resolve `iref dimg` from the iovl to get layer item IDs in z-order.
6. For each layer item, validate properties (`ispe`, `cmpC=defl`, `pixi`, RGBA8, sRGB), read the extent bytes from `mdat`, raw-DEFLATE inflate, and store as `PaintLayer.PixelsRgba`.

### 4.4 Write pipeline

1. Build item table: one `unci` per layer, one `iovl` for the composition. Assign IDs starting at 1.
2. DEFLATE-compress each layer's RGBA buffer (raw, no zlib header).
3. Lay out boxes in the order MS Paint uses (`ftyp`, `meta`, `free`, `mdat`) with `iloc` extents pointing into `mdat`.
4. Two-pass write: first pass computes box sizes and offsets, second pass writes bytes. (Avoids needing a `free` patch-up.)

### 4.5 Testing

- **Golden file:** the `Untitled.paint` we analyzed, plus a few additional samples we collect.
- **Round-trip:** load → save → load → assert all fields and pixel buffers byte-equal.
- **MS Paint round-trip (manual gate):** files written by `Save` must reopen cleanly in MS Paint with all layers intact. Track this in a release checklist.
- **Box-level fuzzer:** randomized truncation / corruption of input bytes; parser must throw `PaintFormatException` rather than crash or AV.

---

## 5. Paint.NET Plugin — `MsPaintFileType`

### 5.1 Project layout

```
src/MsPaintFileType/
  MsPaintFileType.csproj       # net8.0-windows; references MsPaintFile + PaintDotNet.* assemblies
  PluginSupportInfo.cs         # IPluginSupportInfo metadata
  MsPaintFileTypeFactory.cs    # IFileTypeFactory
  MsPaintFileType.cs           # FileType subclass
```

References (from a Paint.NET install — usually `C:\Program Files\paint.net\`):
`PaintDotNet.Base.dll`, `PaintDotNet.Core.dll`, `PaintDotNet.Data.dll`, `PaintDotNet.Fundamentals.dll`, `PaintDotNet.PropertySystem.dll`. Keep these as `<Reference HintPath="...">` with `Private=false` so they don't get copied into the output.

Output: `MsPaintFileType.dll` dropped into Paint.NET's `FileTypes` folder.

### 5.2 Registration

```csharp
public sealed class MsPaintFileTypeFactory : IFileTypeFactory
{
    public FileType[] GetFileTypeInstances() => new FileType[] { new MsPaintFileType() };
}

public sealed class MsPaintFileType : FileType
{
    public MsPaintFileType()
        : base(
            name: "MS Paint Project",
            options: new FileTypeOptions
            {
                LoadExtensions = new[] { ".paint" },
                SaveExtensions = new[] { ".paint" },     // Phase 2; remove for read-only initial release
                SupportsLayers = true,
            })
    { }
    // ...
}
```

### 5.3 Load (`OnLoad`)

Map `PaintDocument` → Paint.NET `Document`:

```csharp
protected override Document OnLoad(Stream input)
{
    var doc = MsPaintFile.PaintDocument.Load(input);
    var pdnDoc = new Document(doc.Width, doc.Height);

    foreach (var layer in doc.Layers)
    {
        var bitmapLayer = Layer.CreateBackgroundLayer(doc.Width, doc.Height); // or new BitmapLayer
        bitmapLayer.Name = string.IsNullOrEmpty(layer.Name)
            ? $"Layer {pdnDoc.Layers.Count + 1}"
            : layer.Name;
        bitmapLayer.Visible = layer.IsVisible;
        bitmapLayer.Opacity = layer.Opacity;
        // Blit RGBA bytes into bitmapLayer.Surface, accounting for OffsetX/OffsetY.
        WriteRgbaIntoSurface(bitmapLayer.Surface, layer);
        pdnDoc.Layers.Add(bitmapLayer);
    }
    return pdnDoc;
}
```

A few details worth nailing down at implementation time:
- Paint.NET's surface is BGRA premultiplied? — confirm from `PaintDotNet.Imaging` / `Surface` docs and add a swap+(de)premultiply step if so. (Per recent Paint.NET v5 changes this is typically straight BGRA.)
- For layers smaller than canvas, fill the surface with transparent and blit only the layer's rect.
- The first added layer becomes the background in Paint.NET. Order matters — we add bottom-to-top, matching iovl input order.

### 5.4 Save (`OnSave` — Phase 2)

```csharp
protected override void OnSave(
    Document input, Stream output, SaveConfigToken token,
    Surface scratchSurface, ProgressEventHandler progressCallback)
{
    var doc = new MsPaintFile.PaintDocument(input.Width, input.Height);
    foreach (BitmapLayer layer in input.Layers)
    {
        doc.Layers.Add(new PaintLayer
        {
            Name = layer.Name ?? "",
            Width = input.Width,
            Height = input.Height,
            PixelsRgba = ExtractRgbaFromSurface(layer.Surface),
            IsVisible = layer.Visible,
            Opacity = layer.Opacity,
        });
    }
    doc.Save(output);
}
```

Open question for save: do we cap layer count or warn the user? MS Paint's UI limit is unknown; we should test what it accepts.

### 5.5 No save dialog (initial)

We don't need a `SaveConfigWidget` — there are no user-tunable options. If we add per-layer compression level later, surface a single combo box.

---

## 6. Phased plan

**Phase 0 — spec lock (this doc)**  
Collect 5–10 sample `.paint` files covering edge cases (renamed layers, hidden layers, layers smaller than canvas, very tall layer counts). Confirm or extend §3.4.

**Phase 1 — read-only library + plugin**  
- `MsPaintFile.PaintDocument.Load` end-to-end.
- Paint.NET plugin registers `.paint` for *Open* but not *Save*.
- Manual test: open all samples in Paint.NET, eyeball-compare against MS Paint.

**Phase 2 — write support**  
- `PaintDocument.Save` produces files MS Paint reopens.
- Plugin enables Save dialog.
- Round-trip CI test: open a `.paint`, re-save with our writer, diff against original (allow `mdat` byte differences from deflate determinism).

**Phase 3 — fidelity polish**  
Once Microsoft documents the format or we collect enough samples: layer names, visibility, opacity, blend modes (if any), thumbnails.

**Phase 4 — distribution**  
- NuGet for `MsPaintFile` (the library is independently useful — anyone reading `.paint` from a script, build pipeline, server, etc.).
- GitHub release zip for the plugin DLL with install instructions; optionally a publish thread on the Paint.NET forum.

---

## 7. Versioning & risk

- The format is shipping in **Insider preview only** (Paint version 11.2508+, Canary/Dev channels) as of this spec's date. Microsoft may change it before GA. Pin a `FormatVersion` byte at the top of `PaintDocument` and gate behavior on it.
- Microsoft has not published a spec. If/when they do, reconcile this document against it and treat the published spec as authoritative.
- **License caution:** do not pull in libheif (LGPL with HEVC patent encumbrances on some configurations). Our parser is clean-room from ISO standards, which is fine.
- **Distribution caution:** the .paint format may carry MIAF/HEIF brand expectations. Files our library writes should still round-trip through MS Paint as the source of truth.

---

## 8. Open decisions for review

1. Lib namespace name: `MsPaintFile` vs. `Microsoft.Paint.Project` vs. neutral (`PaintProject`). Microsoft trademark concerns argue for the third.
2. TFM strategy: net8.0 only, or multi-target down to netstandard2.0 for max plugin reach?
3. Should the library expose the raw HEIF box tree as well, for users who want to inspect/edit beyond layers? My instinct: no — keep the surface tight and add a separate `MsPaintFile.LowLevel` namespace later if needed.
4. Repository structure: monorepo (`lib + plugin + samples`) or two repos? Monorepo is simpler given the plugin is the only consumer right now.
