namespace Art3m1s.PsvTool.Core;

public enum PsbTextureFormat
{
    Dxt5,
    Pvrtc2_4,
    Pvrtc2_2
}

public static class PsbTextureFormats
{
    public static string Name(PsbTextureFormat format) => format switch
    {
        PsbTextureFormat.Dxt5 => "DXT5 (BC3)",
        PsbTextureFormat.Pvrtc2_4 => "PVRTC2 4bpp",
        PsbTextureFormat.Pvrtc2_2 => "PVRTC2 2bpp",
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    // Explicit PSB type identifiers: the E-mote loader must recognize these.
    public static string TypeName(PsbTextureFormat format, PsbDxt5Layout layout = PsbDxt5Layout.Linear) => format switch
    {
        PsbTextureFormat.Dxt5 when layout == PsbDxt5Layout.Swizzled => "DXT5_SWIZZLED",
        PsbTextureFormat.Dxt5 => "DXT5",
        PsbTextureFormat.Pvrtc2_4 => "PVRTC2_4BPP",
        PsbTextureFormat.Pvrtc2_2 => "PVRTC2_2BPP",
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    public static NativeTextureFormat NativeFormat(PsbTextureFormat format) => format switch
    {
        PsbTextureFormat.Dxt5 => NativeTextureFormat.Bc3,
        PsbTextureFormat.Pvrtc2_4 => NativeTextureFormat.Pvrtc2_4,
        PsbTextureFormat.Pvrtc2_2 => NativeTextureFormat.Pvrtc2_2,
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    public static PsbTextureFormat? ParseType(string type) => type.ToUpperInvariant() switch
    {
        "DXT5" or "DXT5_SWIZZLED" => PsbTextureFormat.Dxt5,
        "PVRTC2_4BPP" => PsbTextureFormat.Pvrtc2_4,
        "PVRTC2_2BPP" => PsbTextureFormat.Pvrtc2_2,
        _ => null
    };

    public static PsbDxt5Layout ParseDxt5Layout(string type) =>
        type.Equals("DXT5_SWIZZLED", StringComparison.OrdinalIgnoreCase) ? PsbDxt5Layout.Swizzled : PsbDxt5Layout.Linear;
}
