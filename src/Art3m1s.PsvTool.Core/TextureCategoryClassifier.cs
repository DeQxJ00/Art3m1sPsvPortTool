namespace Art3m1s.PsvTool.Core;

/// <summary>Classifies image paths without changing resource names or inferring image content.</summary>
public static class TextureCategoryClassifier
{
    public const int MaximumManualDepth = 8;
    private static readonly HashSet<string> Backgrounds = new(StringComparer.Ordinal)
        { "bg", "bgimage", "background", "backgrounds" };
    private static readonly HashSet<string> ColorRoles = new(StringComparer.Ordinal)
        { "bg", "bgimage", "background", "backgrounds", "fg", "fgimage", "character", "characters", "cg", "event", "ev", "evimage" };
    private static readonly HashSet<string> ProtectedRoles = new(StringComparer.Ordinal)
        { "ui", "gui", "system", "menu", "font", "fonts", "fonttext", "mask", "masks", "rule", "rules",
          "button", "buttons", "icon", "icons", "cursor", "glyph", "thumb", "thumbnail", "thumbnails" };
    private static readonly HashSet<string> Wrappers = new(StringComparer.Ordinal)
        { "image", "images", "graphics", "assets", "data", "_data", "resource", "resources", "res",
          "hd", "sd", "uhd", "fhd", "fullhd", "high", "low", "2k", "4k", "720p", "1080p",
          "pc", "windows", "android", "ios", "vita", "wasm", "switch", "ps4",
          "cn", "zh", "zh-cn", "zh_cn", "en", "eng", "ja", "jp", "ko", "kr", "tw", "zh-tw", "zh_tw" };

    public static bool IsValidDepth(int depth) => depth is >= 0 and <= MaximumManualDepth;

    public static string CategoryFor(string path, int depth = ConversionDefaults.TextureCategoryDepth)
    {
        if (!IsValidDepth(depth)) throw new ArgumentOutOfRangeException(nameof(depth));
        string[] parts = path.Replace('\\', '/').TrimStart('/').ToLowerInvariant()
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        int directories = Math.Max(0, parts.Length - 1);
        if (directories == 0) return "(root)";
        if (depth > 0) return string.Join('/', parts.Take(Math.Min(depth, directories)));

        // Protected descendants take priority over an ancestor named BG/FG/CG.
        // For example, image/bg/ui must not inherit the parent's color recommendation.
        for (int i = 0; i < directories; i++)
            if (ProtectedRoles.Contains(parts[i])) return string.Join('/', parts.Take(i + 1));
        for (int i = 0; i < directories; i++)
        {
            if (ColorRoles.Contains(parts[i])) return string.Join('/', parts.Take(i + 1));
            // Only known packaging/platform/resolution/language wrappers are transparent.
            // Unknown parents keep the legacy grouping and conservative recommendation.
            if (!Wrappers.Contains(parts[i])) break;
        }
        int fallback = parts[0] is "image" or "images" or "graphics" ? Math.Min(2, directories) : 1;
        return string.Join('/', parts.Take(fallback));
    }

    public static bool IsConservative(string category)
    {
        string[] parts = category.ToLowerInvariant().Split('/');
        return parts.Any(ProtectedRoles.Contains) || !parts.Any(ColorRoles.Contains);
    }

    public static bool IsBackground(string category) =>
        !IsConservative(category) && category.ToLowerInvariant().Split('/').Any(Backgrounds.Contains);
}
