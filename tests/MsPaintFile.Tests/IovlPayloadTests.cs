using System.Buffers.Binary;
using FluentAssertions;
using MsPaintFile;
using MsPaintFile.Items;

namespace MsPaintFile.Tests;

public class IovlPayloadTests
{
    [Fact]
    public void Parses_16bit_payload_matching_spec_sample()
    {
        // Mirrors the sample analyzed in the spec: version=0, flags=0,
        // fill=(0xFFFF,0xFFFF,0xFFFF,0xFFFF), 333x123, two inputs at (0,0).
        var p = new MemoryStream();
        p.WriteByte(0); // version
        p.WriteByte(0); // flags
        WriteU16(p, 0xFFFF); WriteU16(p, 0xFFFF); WriteU16(p, 0xFFFF); WriteU16(p, 0xFFFF);
        WriteU16(p, 333); WriteU16(p, 123);
        WriteI16(p, 0); WriteI16(p, 0);  // input 1
        WriteI16(p, 0); WriteI16(p, 0);  // input 2

        var iovl = IovlItem.Parse(p.ToArray(), inputCount: 2);
        iovl.Version.Should().Be(0);
        iovl.OutputWidth.Should().Be(333);
        iovl.OutputHeight.Should().Be(123);
        iovl.Inputs.Should().HaveCount(2);
        iovl.Inputs[0].Should().Be(new IovlInput(0, 0));
        iovl.Inputs[1].Should().Be(new IovlInput(0, 0));
    }

    [Fact]
    public void Parses_32bit_wide_payload_with_signed_negative_offset()
    {
        var p = new MemoryStream();
        p.WriteByte(0);                     // version
        p.WriteByte(0x01);                  // flags = bit 0 → 32-bit dims/offsets
        WriteU16(p, 0); WriteU16(p, 0); WriteU16(p, 0); WriteU16(p, 0);   // fill
        WriteU32(p, 100_000); WriteU32(p, 50_000);                         // output dims
        WriteI32(p, -7);  WriteI32(p, 12345);                              // input 1: negative dx

        var iovl = IovlItem.Parse(p.ToArray(), inputCount: 1);
        iovl.OutputWidth.Should().Be(100_000);
        iovl.OutputHeight.Should().Be(50_000);
        iovl.Inputs[0].Should().Be(new IovlInput(-7, 12345));
    }

    [Fact]
    public void Throws_on_reserved_flag_bits()
    {
        var p = new MemoryStream();
        p.WriteByte(0);
        p.WriteByte(0x80);  // reserved bit
        WriteU16(p, 0); WriteU16(p, 0); WriteU16(p, 0); WriteU16(p, 0);
        WriteU16(p, 1); WriteU16(p, 1);

        Action act = () => IovlItem.Parse(p.ToArray(), inputCount: 0);
        act.Should().Throw<PaintFormatException>().WithMessage("*reserved flag bits*");
    }

    [Fact]
    public void Throws_when_payload_too_short_for_declared_inputs()
    {
        var p = new MemoryStream();
        p.WriteByte(0); p.WriteByte(0);
        WriteU16(p, 0); WriteU16(p, 0); WriteU16(p, 0); WriteU16(p, 0);
        WriteU16(p, 10); WriteU16(p, 10);
        // Promise 3 inputs but write only 1
        WriteI16(p, 0); WriteI16(p, 0);

        Action act = () => IovlItem.Parse(p.ToArray(), inputCount: 3);
        act.Should().Throw<PaintFormatException>().WithMessage("*too short for 3 inputs*");
    }

    private static void WriteU16(Stream s, ushort v) { Span<byte> b = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(b, v); s.Write(b); }
    private static void WriteI16(Stream s, short v) { Span<byte> b = stackalloc byte[2]; BinaryPrimitives.WriteInt16BigEndian(b, v); s.Write(b); }
    private static void WriteU32(Stream s, uint v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, v); s.Write(b); }
    private static void WriteI32(Stream s, int v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(b, v); s.Write(b); }
}
