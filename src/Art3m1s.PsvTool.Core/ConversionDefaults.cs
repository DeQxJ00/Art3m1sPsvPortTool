namespace Art3m1s.PsvTool.Core;

/// <summary>Fresh-configuration conversion defaults shared by the UI and command line.</summary>
public static class ConversionDefaults
{
    public const double Ratio = 0.5;
    public const AssetCategories Categories = AssetCategories.All;
    public const int MaxParallelism = 0;
    public const PfsNameEncoding NameEncoding = PfsNameEncoding.Auto;
    public const bool SubsetFonts = false;
    public const FontSubsetProfile FontProfile = FontSubsetProfile.SimplifiedChinese;
    public const bool NativeTextures = true;
    public const bool IgnoreBackgroundAlpha = false;
    // Zero selects conservative automatic path-role classification; positive values are absolute directory depths.
    public const int TextureCategoryDepth = 0;
    public const bool IgnorePfsVideos = true;
    public const bool ConvertEmotePsbTextures = true;
    public const PsbTextureFormat PsbOutputFormat = PsbTextureFormat.Dxt5;
    public const PsbDxt5Layout Dxt5Layout = PsbDxt5Layout.Swizzled;
    public const NativeTextureFormat ImageFormat = NativeTextureFormat.Auto;
    public const bool IndependentPsbScaling = false;
    public const bool PsbRenderCompensation = false;
    public const int FitWidth = 960;
    public const int FitHeight = 540;
}
