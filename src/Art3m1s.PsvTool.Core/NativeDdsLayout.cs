using System.Buffers.Binary;

namespace Art3m1s.PsvTool.Core;

// Private PSV extension, not a standard DDS layout. No version byte or extra
// header is added. Unmarked DXT1/DXT5 remains ordinary row-major BC1/BC3.
public static class NativeDdsLayout
{
    // DDS magic (4 bytes) + DDS_HEADER fields before dwReserved1 (28 bytes).
    public const int GxmSwizzledMarkerOffset = 32;

    public static bool IsGxmSwizzled(ReadOnlySpan<byte> data) =>
        data.Length >= 128 && data.StartsWith("DDS "u8)
        && BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) == 124
        && BinaryPrimitives.ReadUInt32LittleEndian(data[76..]) == 32
        && (BinaryPrimitives.ReadUInt32LittleEndian(data[80..]) & 4) != 0
        && (data.Slice(84, 4).SequenceEqual("DXT1"u8) || data.Slice(84, 4).SequenceEqual("DXT5"u8))
        && data.Slice(GxmSwizzledMarkerOffset, 5).SequenceEqual("GXMSW"u8);
}
