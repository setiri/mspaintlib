using MsPaintFile.Container;

namespace MsPaintFile.Properties;

public sealed record UncCComponent(
    ushort ComponentIndex,
    byte BitDepthMinusOne,
    byte ComponentFormat,
    byte ComponentAlignSize);

public sealed record UncC(
    FourCc Profile,
    IReadOnlyList<UncCComponent> Components,
    byte SamplingType,
    byte Interleave,
    byte BlockSize,
    bool ComponentsLittleEndian,
    bool BlockPadLsb,
    bool BlockLittleEndian,
    bool BlockReversed,
    bool PadUnknown,
    uint PixelSize,
    uint RowAlignSize,
    uint TileAlignSize,
    uint NumTileColsMinusOne,
    uint NumTileRowsMinusOne);
