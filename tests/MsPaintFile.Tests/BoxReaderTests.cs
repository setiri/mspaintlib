using System.Buffers.Binary;
using FluentAssertions;
using MsPaintFile;
using MsPaintFile.Container;

namespace MsPaintFile.Tests;

public class BoxReaderTests
{
    [Fact]
    public void Reads_two_sibling_boxes_with_32bit_size()
    {
        var data = new MemoryStream();
        WriteBox(data, "ftyp", payload: new byte[] { 1, 2, 3, 4 });
        WriteBox(data, "meta", payload: new byte[] { 9, 8, 7 });
        data.Position = 0;

        var reader = new BoxReader(data);
        reader.TryReadBoxHeader(data.Length, out var first).Should().BeTrue();
        first.Type.Should().Be(FourCc.Of("ftyp"));
        first.PayloadLength.Should().Be(4);

        data.Position = first.PayloadEnd;
        reader.TryReadBoxHeader(data.Length, out var second).Should().BeTrue();
        second.Type.Should().Be(FourCc.Of("meta"));
        second.PayloadLength.Should().Be(3);

        data.Position = second.PayloadEnd;
        reader.TryReadBoxHeader(data.Length, out _).Should().BeFalse();
    }

    [Fact]
    public void Descends_into_nested_container()
    {
        // outer "meta" with two children "hdlr" + "pitm"
        var inner = new MemoryStream();
        WriteBox(inner, "hdlr", payload: new byte[16]);
        WriteBox(inner, "pitm", payload: new byte[] { 0xAA, 0xBB });
        var outer = new MemoryStream();
        WriteBox(outer, "meta", payload: inner.ToArray());
        outer.Position = 0;

        var reader = new BoxReader(outer);
        reader.TryReadBoxHeader(outer.Length, out var meta).Should().BeTrue();
        meta.Type.Should().Be(FourCc.Of("meta"));

        // Walk meta's children
        reader.Position = meta.PayloadStart;
        reader.TryReadBoxHeader(meta.PayloadEnd, out var c1).Should().BeTrue();
        c1.Type.Should().Be(FourCc.Of("hdlr"));
        reader.Position = c1.PayloadEnd;
        reader.TryReadBoxHeader(meta.PayloadEnd, out var c2).Should().BeTrue();
        c2.Type.Should().Be(FourCc.Of("pitm"));
        reader.Position = c2.PayloadEnd;
        reader.TryReadBoxHeader(meta.PayloadEnd, out _).Should().BeFalse();
    }

    [Fact]
    public void Handles_largesize_box()
    {
        // size32==1 means an 8-byte largesize follows the type. Header is 16 bytes.
        var data = new MemoryStream();
        var w = new BinaryWriter(data);
        w.Write(BeU32(1));                                // size = 1 → use largesize
        w.Write(System.Text.Encoding.ASCII.GetBytes("mdat"));
        w.Write(BeU64(16 + 5));                           // largesize: header (16) + payload (5)
        w.Write(new byte[] { 1, 2, 3, 4, 5 });
        data.Position = 0;

        var reader = new BoxReader(data);
        reader.TryReadBoxHeader(data.Length, out var box).Should().BeTrue();
        box.Type.Should().Be(FourCc.Of("mdat"));
        box.PayloadStart.Should().Be(16);
        box.PayloadLength.Should().Be(5);
    }

    [Fact]
    public void Handles_size_zero_extends_to_container_end()
    {
        // size32==0 means "extend to end of container/file"
        var data = new MemoryStream();
        var w = new BinaryWriter(data);
        w.Write(BeU32(0));
        w.Write(System.Text.Encoding.ASCII.GetBytes("free"));
        w.Write(new byte[20]);
        data.Position = 0;

        var reader = new BoxReader(data);
        reader.TryReadBoxHeader(data.Length, out var box).Should().BeTrue();
        box.Type.Should().Be(FourCc.Of("free"));
        box.PayloadLength.Should().Be(20);
        box.PayloadEnd.Should().Be(data.Length);
    }

    [Fact]
    public void Throws_when_box_extends_past_container_end()
    {
        var data = new MemoryStream();
        var w = new BinaryWriter(data);
        w.Write(BeU32(100));     // claims 100 bytes
        w.Write(System.Text.Encoding.ASCII.GetBytes("ftyp"));
        w.Write(new byte[5]);    // but only 13 bytes total
        data.Position = 0;

        var reader = new BoxReader(data);
        Action act = () => reader.TryReadBoxHeader(data.Length, out _);
        act.Should().Throw<PaintFormatException>().WithMessage("*past container end*");
    }

    [Fact]
    public void Throws_on_truncated_header()
    {
        var data = new MemoryStream(new byte[] { 0, 0, 0, 8, (byte)'f', (byte)'t' }); // only 6 bytes, need 8
        var reader = new BoxReader(data);
        Action act = () => reader.TryReadBoxHeader(data.Length, out _);
        act.Should().Throw<PaintFormatException>().WithMessage("*Truncated*");
    }

    [Fact]
    public void Throws_on_size_smaller_than_header()
    {
        var data = new MemoryStream();
        var w = new BinaryWriter(data);
        w.Write(BeU32(4));           // size 4 < 8 (header minimum)
        w.Write(System.Text.Encoding.ASCII.GetBytes("xxxx"));
        data.Position = 0;

        var reader = new BoxReader(data);
        Action act = () => reader.TryReadBoxHeader(data.Length, out _);
        act.Should().Throw<PaintFormatException>().WithMessage("*smaller than minimum header*");
    }

    [Fact]
    public void Read_primitives_round_trip_big_endian()
    {
        var data = new MemoryStream();
        var w = new BinaryWriter(data);
        w.Write(BeU16(0x1234));
        w.Write(BeI16(unchecked((short)0xFFFE)));
        w.Write(BeU32(0xDEADBEEF));
        w.Write(BeI32(-1));
        w.Write(new byte[] { 0, 0xAB, 0xCD });   // u24
        data.Position = 0;

        var reader = new BoxReader(data);
        reader.ReadU16().Should().Be(0x1234);
        reader.ReadI16().Should().Be(-2);
        reader.ReadU32().Should().Be(0xDEADBEEF);
        reader.ReadI32().Should().Be(-1);
        reader.ReadU24().Should().Be(0xABCDu);
    }

    [Fact]
    public void Reads_null_terminated_ascii()
    {
        var data = new MemoryStream(new byte[] { (byte)'h', (byte)'i', 0, (byte)'x' });
        var reader = new BoxReader(data);
        reader.ReadNullTerminatedAscii(data.Length).Should().Be("hi");
        reader.Position.Should().Be(3);
    }

    [Fact]
    public void Throws_when_null_terminator_missing_within_boundary()
    {
        var data = new MemoryStream(new byte[] { (byte)'h', (byte)'i' });
        var reader = new BoxReader(data);
        Action act = () => reader.ReadNullTerminatedAscii(data.Length);
        act.Should().Throw<PaintFormatException>();
    }

    private static void WriteBox(Stream s, string fourcc, byte[] payload)
    {
        var w = new BinaryWriter(s);
        uint size = checked((uint)(8 + payload.Length));
        w.Write(BeU32(size));
        w.Write(System.Text.Encoding.ASCII.GetBytes(fourcc));
        w.Write(payload);
    }

    private static byte[] BeU16(ushort v) { var b = new byte[2]; BinaryPrimitives.WriteUInt16BigEndian(b, v); return b; }
    private static byte[] BeI16(short v) { var b = new byte[2]; BinaryPrimitives.WriteInt16BigEndian(b, v); return b; }
    private static byte[] BeU32(uint v) { var b = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, v); return b; }
    private static byte[] BeI32(int v) { var b = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(b, v); return b; }
    private static byte[] BeU64(ulong v) { var b = new byte[8]; BinaryPrimitives.WriteUInt64BigEndian(b, v); return b; }
}
