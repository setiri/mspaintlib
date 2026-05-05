using System.Buffers.Binary;
using MsPaintFile.Compression;
using MsPaintFile.Container;

namespace MsPaintFile.Items;

/// <summary>
/// Writes a <see cref="PaintDocument"/> to a stream in MS Paint's `.paint` (HEIF/MIAF + unci/iovl)
/// format. Layout mirrors what MS Paint Insider emits, so written files re-open in MS Paint.
/// </summary>
internal static class PaintDocumentWriter
{
    public static void Write(PaintDocument doc, Stream output)
    {
        if (doc.Width <= 0 || doc.Height <= 0)
            throw new InvalidOperationException($"PaintDocument dimensions must be positive (got {doc.Width}x{doc.Height}).");
        if (doc.Layers.Count == 0)
            throw new InvalidOperationException("PaintDocument must have at least one layer.");

        int n = doc.Layers.Count;
        uint iovlId = (uint)(n + 1);

        // Step 1: compress each layer's BGRA-ordered bytes (the on-disk order MS Paint uses).
        var compressedLayers = new byte[n][];
        for (int i = 0; i < n; i++)
        {
            var layer = doc.Layers[i];
            ValidateLayer(layer, doc.Width, doc.Height, i);
            var bgra = SwizzleRgbaToBgra(layer.PixelsRgba);
            compressedLayers[i] = DeflateCodec.Deflate(bgra);
        }

        // Step 2: build iovl payload (inline data for the iovl item, stored in idat).
        byte[] iovlPayload = BuildIovlPayload(doc);

        // Step 3: write the file. Plan is:
        //   ftyp (constant size)  → meta (variable, written to MemoryStream first to know size)
        //   → free (8-byte empty) → mdat (header + concatenated layer compressed bytes)
        // We patch iloc extent_offsets after we know the absolute mdat data offset.

        var metaBuffer = new MemoryStream();
        var ilocExtentOffsetPositions = new long[n];   // positions inside metaBuffer of the layer u32 offsets
        WriteMetaBox(metaBuffer, doc, n, iovlId, compressedLayers, iovlPayload, ilocExtentOffsetPositions);
        byte[] metaBytes = metaBuffer.ToArray();

        long ftypSize = ComputeFtypSize();
        long freeSize = 8;                 // 8-byte empty 'free' header (size + type, no payload)
        long mdatHeaderSize = 8;
        long mdatDataOffset = ftypSize + metaBytes.Length + freeSize + mdatHeaderSize;

        // Patch iloc extent offsets (currently relative-to-mdat-data) into absolute file offsets.
        long relative = 0;
        for (int i = 0; i < n; i++)
        {
            long absolute = mdatDataOffset + relative;
            if (absolute > uint.MaxValue)
                throw new InvalidOperationException($"Layer {i} offset {absolute} exceeds 32-bit; iloc requires 64-bit offset_size.");
            BinaryPrimitives.WriteUInt32BigEndian(metaBytes.AsSpan(checked((int)ilocExtentOffsetPositions[i]), 4), checked((uint)absolute));
            relative += compressedLayers[i].Length;
        }
        long mdatDataSize = relative;

        // Step 4: emit to output.
        var w = new BoxWriter(output);
        WriteFtyp(w);
        w.WriteBytes(metaBytes);
        using (w.BeginBox("free")) { /* empty padding */ }
        using (w.BeginBox("mdat"))
        {
            for (int i = 0; i < n; i++) w.WriteBytes(compressedLayers[i]);
        }
    }

    private static void ValidateLayer(PaintLayer layer, int canvasW, int canvasH, int index)
    {
        if (layer.Width != canvasW || layer.Height != canvasH || layer.OffsetX != 0 || layer.OffsetY != 0)
            throw new NotSupportedException(
                $"Layer {index} is {layer.Width}x{layer.Height} at ({layer.OffsetX},{layer.OffsetY}); writer currently requires canvas-sized layers at (0,0).");
        long expected = (long)layer.Width * layer.Height * 4;
        if (layer.PixelsRgba.LongLength != expected)
            throw new InvalidOperationException(
                $"Layer {index} pixel buffer is {layer.PixelsRgba.LongLength} bytes; expected {expected}.");
    }

    private static byte[] SwizzleRgbaToBgra(byte[] rgba)
    {
        var bgra = new byte[rgba.Length];
        for (int i = 0; i + 4 <= rgba.Length; i += 4)
        {
            bgra[i]     = rgba[i + 2];
            bgra[i + 1] = rgba[i + 1];
            bgra[i + 2] = rgba[i];
            bgra[i + 3] = rgba[i + 3];
        }
        return bgra;
    }

    private static byte[] BuildIovlPayload(PaintDocument doc)
    {
        // Always write 16-bit dims/offsets (flags=0), matching MS Paint for canvases up to 65535x65535.
        if (doc.Width > ushort.MaxValue || doc.Height > ushort.MaxValue)
            throw new NotSupportedException($"Canvas {doc.Width}x{doc.Height} exceeds 16-bit; 32-bit iovl mode not yet implemented.");

        int n = doc.Layers.Count;
        int len = 2 + 8 + 4 + n * 4;        // ver+flags + 4*u16 fill + 2*u16 dims + N*(2*i16) offsets
        var buf = new byte[len];
        int p = 0;
        buf[p++] = 0;                       // version
        buf[p++] = 0;                       // flags (16-bit mode)
        // Fill: expand 8-bit to 16-bit (0xFF → 0xFFFF).
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(p), Expand8To16(doc.CanvasFill.R)); p += 2;
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(p), Expand8To16(doc.CanvasFill.G)); p += 2;
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(p), Expand8To16(doc.CanvasFill.B)); p += 2;
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(p), Expand8To16(doc.CanvasFill.A)); p += 2;
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(p), (ushort)doc.Width);  p += 2;
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(p), (ushort)doc.Height); p += 2;
        for (int i = 0; i < n; i++)
        {
            var layer = doc.Layers[i];
            if (layer.OffsetX is < short.MinValue or > short.MaxValue || layer.OffsetY is < short.MinValue or > short.MaxValue)
                throw new NotSupportedException($"Layer {i} offset ({layer.OffsetX},{layer.OffsetY}) exceeds 16-bit; 32-bit iovl mode not yet implemented.");
            BinaryPrimitives.WriteInt16BigEndian(buf.AsSpan(p), (short)layer.OffsetX); p += 2;
            BinaryPrimitives.WriteInt16BigEndian(buf.AsSpan(p), (short)layer.OffsetY); p += 2;
        }
        return buf;
    }

    private static ushort Expand8To16(byte v) => (ushort)((v << 8) | v);

    private static long ComputeFtypSize() => 8 + 4 + 4 + 4 * 4;   // box header + major + minor + 4 compat brands

    private static void WriteFtyp(BoxWriter w)
    {
        using (w.BeginBox("ftyp"))
        {
            w.WriteFourCc(FourCc.Of("mif1"));     // major
            w.WriteU32(0);                         // minor
            w.WriteFourCc(FourCc.Of("mif1"));
            w.WriteFourCc(FourCc.Of("gcmi"));
            w.WriteFourCc(FourCc.Of("isoa"));
            w.WriteFourCc(FourCc.Of("miaf"));
        }
    }

    private static void WriteMetaBox(
        Stream metaStream,
        PaintDocument doc,
        int n,
        uint iovlId,
        byte[][] compressedLayers,
        byte[] iovlPayload,
        long[] ilocExtentOffsetPositions)
    {
        var w = new BoxWriter(metaStream);
        using (w.BeginBox("meta"))
        {
            w.WriteFullBoxHeader(0, 0);
            WriteHdlrBox(w);
            WritePitmBox(w, iovlId);
            WriteIinfBox(w, n, iovlId);
            WriteIlocBox(w, n, iovlId, compressedLayers, iovlPayload, ilocExtentOffsetPositions);
            WriteIprpBox(w, n, iovlId, doc.Width, doc.Height);
            WriteIrefBox(w, n, iovlId);
            WriteIdatBox(w, iovlPayload);
        }
    }

    private static void WriteHdlrBox(BoxWriter w)
    {
        using (w.BeginBox("hdlr"))
        {
            w.WriteFullBoxHeader(0, 0);
            w.WriteU32(0);                    // pre_defined
            w.WriteFourCc(FourCc.Of("pict"));  // handler_type
            w.WriteU32(0); w.WriteU32(0); w.WriteU32(0);    // 3 reserved u32
            w.WriteU8(0);                     // null-terminator for empty name
        }
    }

    private static void WritePitmBox(BoxWriter w, uint primaryItemId)
    {
        using (w.BeginBox("pitm"))
        {
            w.WriteFullBoxHeader(0, 0);
            w.WriteU16((ushort)primaryItemId);  // v0 → u16 id
        }
    }

    private static void WriteIinfBox(BoxWriter w, int n, uint iovlId)
    {
        using (w.BeginBox("iinf"))
        {
            w.WriteFullBoxHeader(0, 0);
            w.WriteU16((ushort)(n + 1));   // entry_count
            for (uint i = 1; i <= n; i++) WriteInfeBox(w, i, FourCc.Of("unci"));
            WriteInfeBox(w, iovlId, FourCc.Of("iovl"));
        }
    }

    private static void WriteInfeBox(BoxWriter w, uint itemId, FourCc itemType)
    {
        using (w.BeginBox("infe"))
        {
            w.WriteFullBoxHeader(2, 0);     // v2 → u16 id
            w.WriteU16((ushort)itemId);
            w.WriteU16(0);                  // protection_index
            w.WriteFourCc(itemType);
            w.WriteU8(0);                   // null-terminated empty item_name
        }
    }

    private static void WriteIlocBox(
        BoxWriter w,
        int n,
        uint iovlId,
        byte[][] compressedLayers,
        byte[] iovlPayload,
        long[] ilocExtentOffsetPositions)
    {
        using (w.BeginBox("iloc"))
        {
            w.WriteFullBoxHeader(1, 0);
            w.WriteU8((4 << 4) | 4);       // offset_size=4, length_size=4
            w.WriteU8((0 << 4) | 0);       // base_offset_size=0, index_size=0
            w.WriteU16((ushort)(n + 1));   // item_count

            // Layer items first (cm=0, extent in mdat).
            long extentOffsetCursor = 0;
            for (uint i = 0; i < n; i++)
            {
                w.WriteU16((ushort)(i + 1));   // item_id
                w.WriteU16(0);                  // 12 reserved bits + cm=0 (file)
                w.WriteU16(0);                  // data_reference_index
                // base_offset_size=0 → no base_offset bytes
                w.WriteU16(1);                  // extent_count

                // Capture the position of extent_offset for later patching.
                ilocExtentOffsetPositions[i] = w.Position;
                w.WriteU32((uint)extentOffsetCursor);   // placeholder: relative offset
                w.WriteU32((uint)compressedLayers[i].Length);
                extentOffsetCursor += compressedLayers[i].Length;
            }

            // iovl item: cm=1 (idat), extent_offset is into idat (always 0 since we put one item there).
            w.WriteU16((ushort)iovlId);
            w.WriteU16(1);                       // cm=1 (idat)
            w.WriteU16(0);                       // data_reference_index
            w.WriteU16(1);                       // extent_count
            w.WriteU32(0);                       // extent_offset within idat
            w.WriteU32((uint)iovlPayload.Length);
        }
    }

    private static void WriteIprpBox(BoxWriter w, int n, uint iovlId, int canvasW, int canvasH)
    {
        // Properties (1-based ipma indices):
        //   [1] cmpC, [2] ispe, [3] colr, [4] cmpd, [5] uncC, [6] pixi
        using (w.BeginBox("iprp"))
        {
            using (w.BeginBox("ipco"))
            {
                WriteCmpcProperty(w);
                WriteIspeProperty(w, canvasW, canvasH);
                WriteColrProperty(w);
                WriteCmpdProperty(w);
                WriteUncCProperty(w);
                WritePixiProperty(w);
            }
            WriteIpmaBox(w, n, iovlId);
        }
    }

    private static void WriteCmpcProperty(BoxWriter w)
    {
        using (w.BeginBox("cmpC"))
        {
            w.WriteFullBoxHeader(0, 0);
            w.WriteFourCc(FourCc.Of("defl"));
            w.WriteU8(0);                   // compressed_unit_type (0 = whole image)
        }
    }

    private static void WriteIspeProperty(BoxWriter w, int width, int height)
    {
        using (w.BeginBox("ispe"))
        {
            w.WriteFullBoxHeader(0, 0);
            w.WriteU32((uint)width);
            w.WriteU32((uint)height);
        }
    }

    private static void WriteColrProperty(BoxWriter w)
    {
        using (w.BeginBox("colr"))
        {
            w.WriteFourCc(FourCc.Of("nclx"));
            w.WriteU16(1);     // primaries: BT.709 / sRGB
            w.WriteU16(13);    // transfer: sRGB
            w.WriteU16(0);     // matrix: identity (RGB)
            w.WriteU8(0x80);   // full_range bit
        }
    }

    private static void WriteCmpdProperty(BoxWriter w)
    {
        using (w.BeginBox("cmpd"))
        {
            w.WriteU32(4);     // component_count
            w.WriteU16(4);     // R
            w.WriteU16(5);     // G
            w.WriteU16(6);     // B
            w.WriteU16(7);     // A
        }
    }

    private static void WriteUncCProperty(BoxWriter w)
    {
        // v0 with profile 'gene', BGRA on disk:
        //   component[0].component_index = 2 (cmpd[2] = B)
        //   component[1].component_index = 1 (cmpd[1] = G)
        //   component[2].component_index = 0 (cmpd[0] = R)
        //   component[3].component_index = 3 (cmpd[3] = A)
        using (w.BeginBox("uncC"))
        {
            w.WriteFullBoxHeader(0, 0);
            w.WriteFourCc(FourCc.Of("gene"));
            w.WriteU32(4);     // component_count
            int[] order = [2, 1, 0, 3];
            for (int i = 0; i < 4; i++)
            {
                w.WriteU16((ushort)order[i]);  // component_index → cmpd
                w.WriteU8(7);                   // bit_depth_minus_one (8 bit)
                w.WriteU8(0);                   // component_format (unsigned int)
                w.WriteU8(0);                   // component_align_size
            }
            w.WriteU8(0);      // sampling_type (0 = no sub-sampling)
            w.WriteU8(1);      // interleave_type (1 = pixel-interleaved)
            w.WriteU8(0);      // block_size
            w.WriteU8(0);      // flag bits (all 0)
            w.WriteU32(0);     // pixel_size
            w.WriteU32(0);     // row_align_size
            w.WriteU32(0);     // tile_align_size
            w.WriteU32(0);     // num_tile_cols_minus_one
            w.WriteU32(0);     // num_tile_rows_minus_one
        }
    }

    private static void WritePixiProperty(BoxWriter w)
    {
        using (w.BeginBox("pixi"))
        {
            w.WriteFullBoxHeader(0, 0);
            w.WriteU8(4);                       // num_channels
            w.WriteU8(8); w.WriteU8(8); w.WriteU8(8); w.WriteU8(8);
        }
    }

    private static void WriteIpmaBox(BoxWriter w, int n, uint iovlId)
    {
        using (w.BeginBox("ipma"))
        {
            w.WriteFullBoxHeader(0, 0);                  // v0, flags=0 (8-bit indices)
            w.WriteU32((uint)(n + 1));                    // entry_count

            // Layers: [1,2,3,4,5,6] all essential
            byte[] layerProps = [0x81, 0x82, 0x83, 0x84, 0x85, 0x86];
            for (uint i = 1; i <= n; i++)
            {
                w.WriteU16((ushort)i);                    // item_id (v0 → u16)
                w.WriteU8(6);                              // association_count
                foreach (var p in layerProps) w.WriteU8(p);
            }

            // iovl: [2, 6] = ispe + pixi (matches MS Paint's pattern)
            w.WriteU16((ushort)iovlId);
            w.WriteU8(2);
            w.WriteU8(0x82);   // ispe, essential
            w.WriteU8(0x86);   // pixi, essential
        }
    }

    private static void WriteIrefBox(BoxWriter w, int n, uint iovlId)
    {
        using (w.BeginBox("iref"))
        {
            w.WriteFullBoxHeader(0, 0);                  // v0 → u16 ids
            using (w.BeginBox("dimg"))
            {
                w.WriteU16((ushort)iovlId);              // from_item
                w.WriteU16((ushort)n);                    // reference_count
                for (uint i = 1; i <= n; i++) w.WriteU16((ushort)i);
            }
        }
    }

    private static void WriteIdatBox(BoxWriter w, byte[] iovlPayload)
    {
        using (w.BeginBox("idat"))
        {
            w.WriteBytes(iovlPayload);
        }
    }
}
