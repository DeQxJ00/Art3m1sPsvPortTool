using PVRTexLib;

namespace Art3m1s.PsvTool.Core;

// ASTC inputs are linear, headerless 16-byte blocks. A bit-rate alone is not a
// general block-size identifier: only the verified ASTC_8BPP alias means 4x4.
internal readonly record struct PsbAstcFormat(int BlockWidth, int BlockHeight, PVRTexLibPixelFormat PixelFormat)
{
    public static PsbAstcFormat? ParseType(string type) => type.ToUpperInvariant() switch
    {
        "ASTC_8BPP" or "ASTC_4X4" => new(4, 4, PVRTexLibPixelFormat.ASTC_4x4),
        "ASTC_5X4" => new(5, 4, PVRTexLibPixelFormat.ASTC_5x4),
        "ASTC_5X5" => new(5, 5, PVRTexLibPixelFormat.ASTC_5x5),
        "ASTC_6X5" => new(6, 5, PVRTexLibPixelFormat.ASTC_6x5),
        "ASTC_6X6" => new(6, 6, PVRTexLibPixelFormat.ASTC_6x6),
        "ASTC_8X5" => new(8, 5, PVRTexLibPixelFormat.ASTC_8x5),
        "ASTC_8X6" => new(8, 6, PVRTexLibPixelFormat.ASTC_8x6),
        "ASTC_8X8" => new(8, 8, PVRTexLibPixelFormat.ASTC_8x8),
        "ASTC_10X5" => new(10, 5, PVRTexLibPixelFormat.ASTC_10x5),
        "ASTC_10X6" => new(10, 6, PVRTexLibPixelFormat.ASTC_10x6),
        "ASTC_10X8" => new(10, 8, PVRTexLibPixelFormat.ASTC_10x8),
        "ASTC_10X10" => new(10, 10, PVRTexLibPixelFormat.ASTC_10x10),
        "ASTC_12X10" => new(12, 10, PVRTexLibPixelFormat.ASTC_12x10),
        "ASTC_12X12" => new(12, 12, PVRTexLibPixelFormat.ASTC_12x12),
        _ => null
    };

    public long PayloadBytes(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        return checked(((width + BlockWidth - 1L) / BlockWidth) * ((height + BlockHeight - 1L) / BlockHeight) * 16);
    }
}
