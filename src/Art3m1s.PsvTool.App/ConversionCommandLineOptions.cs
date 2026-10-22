using System.Globalization;
using Art3m1s.PsvTool.Core;

namespace Art3m1s.PsvTool.App;

internal sealed class ConversionCommandLineOptions
{
    public string Input { get; set; } = "";
    public string Output { get; set; } = "";
    public string? Log { get; set; }
    public double? Ratio { get; set; } = ConversionDefaults.Ratio;
    public (int Width, int Height)? SourceResolution { get; set; }
    public (int Width, int Height) Fit { get; set; } = (ConversionDefaults.FitWidth, ConversionDefaults.FitHeight);
    public int Parallelism { get; set; } = ConversionDefaults.MaxParallelism;
    public PfsNameEncoding Encoding { get; set; } = ConversionDefaults.NameEncoding;
    public AssetCategories Types { get; set; } = ConversionDefaults.Categories;
    public bool NativeTextures { get; set; } = ConversionDefaults.NativeTextures;
    public bool IgnoreBackgroundAlpha { get; set; } = ConversionDefaults.IgnoreBackgroundAlpha;
    public PsbTextureFormat? PsbFormat { get; set; } = ConversionDefaults.ConvertEmotePsbTextures ? ConversionDefaults.PsbOutputFormat : null;
    public PsbDxt5Layout Layout { get; set; } = ConversionDefaults.Dxt5Layout;
    public bool SubsetFonts { get; set; } = ConversionDefaults.SubsetFonts;
    public FontSubsetProfile FontProfile { get; set; } = ConversionDefaults.FontProfile;
    public bool IgnorePfsVideos { get; set; } = ConversionDefaults.IgnorePfsVideos;
    public string Existing { get; set; } = "skip";
    public bool DryRun { get; set; }
    public bool Verbose { get; set; }

    public static ConversionCommandLineOptions Parse(string[] args)
    {
        ConversionCommandLineOptions options = new();
        List<string> positional = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        for (int i = 1; i < args.Length; i++)
        {
            string argument = args[i];
            if (!argument.StartsWith('-')) { positional.Add(argument); continue; }
            int equals = argument.IndexOf('=');
            string key = equals < 0 ? argument : argument[..equals];
            key = key switch { "-i" => "--input", "-o" => "--output", _ => key };
            if (!seen.Add(key)) throw new ArgumentException($"Duplicate option: {key}");
            if (key is "--dry-run" or "--verbose")
            {
                if (equals >= 0) throw new ArgumentException($"{key} does not take a value.");
                if (key == "--dry-run") options.DryRun = true; else options.Verbose = true;
                continue;
            }
            string value = equals >= 0 ? argument[(equals + 1)..]
                : i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i]
                : throw new ArgumentException($"Missing value for {key}.");
            if (value.Length == 0) throw new ArgumentException($"Empty value for {key}.");
            switch (key)
            {
                case "--preset":
                    if (!value.Equals("ui-defaults", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("--preset must be ui-defaults.");
                    // Defaults are applied at construction; explicit options override them regardless of argument order.
                    break;
                case "--input": options.Input = value; break;
                case "--output": options.Output = value; break;
                case "--log": options.Log = value; break;
                case "--ratio":
                    if (value.Equals("auto", StringComparison.OrdinalIgnoreCase)) options.Ratio = null;
                    else if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double ratio) && double.IsFinite(ratio) && ratio is > 0 and <= 1) options.Ratio = ratio;
                    else throw new ArgumentException("--ratio must be auto or a number in (0, 1], using a decimal point.");
                    break;
                case "--source-resolution": options.SourceResolution = Size(value, key); break;
                case "--fit": options.Fit = Size(value, key); break;
                case "--parallel":
                    if (value.Equals("auto", StringComparison.OrdinalIgnoreCase)) options.Parallelism = 0;
                    else if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parallel) && parallel > 0) options.Parallelism = parallel;
                    else throw new ArgumentException("--parallel must be auto or a positive integer.");
                    break;
                case "--encoding":
                    options.Encoding = value.ToLowerInvariant() switch
                    {
                        "auto" => PfsNameEncoding.Auto,
                        "utf-8" or "utf8" => PfsNameEncoding.Utf8,
                        "shift-jis" or "shift_jis" or "sjis" => PfsNameEncoding.ShiftJis,
                        _ => throw new ArgumentException("--encoding must be auto, utf-8 or shift-jis.")
                    }; break;
                case "--types": options.Types = Categories(value); break;
                case "--native-textures": options.NativeTextures = Boolean(value, key); break;
                case "--ignore-bg-alpha": options.IgnoreBackgroundAlpha = Boolean(value, key); break;
                case "--psb-format":
                    options.PsbFormat = value.ToLowerInvariant() switch
                    {
                        "dxt5" or "bc3" => PsbTextureFormat.Dxt5,
                        "pvrtc2-4" => PsbTextureFormat.Pvrtc2_4,
                        "pvrtc2-2" => PsbTextureFormat.Pvrtc2_2,
                        "preserve" => null,
                        _ => throw new ArgumentException("--psb-format must be dxt5, pvrtc2-4, pvrtc2-2 or preserve.")
                    }; break;
                case "--dxt5-layout":
                    options.Layout = value.ToLowerInvariant() switch
                    {
                        "swizzled" => PsbDxt5Layout.Swizzled,
                        "linear" => PsbDxt5Layout.Linear,
                        _ => throw new ArgumentException("--dxt5-layout must be swizzled or linear.")
                    }; break;
                case "--subset-fonts": options.SubsetFonts = Boolean(value, key); break;
                case "--font-profile":
                    options.FontProfile = value.ToLowerInvariant() switch
                    {
                        "simplified" or "simplified-chinese" or "zh-cn" => FontSubsetProfile.SimplifiedChinese,
                        "japanese" or "ja" => FontSubsetProfile.Japanese,
                        "traditional" or "traditional-chinese" or "zh-tw" => FontSubsetProfile.TraditionalChinese,
                        _ => throw new ArgumentException("--font-profile must be simplified, japanese or traditional.")
                    }; break;
                case "--ignore-pfs-videos": options.IgnorePfsVideos = Boolean(value, key); break;
                case "--existing":
                    options.Existing = value.ToLowerInvariant();
                    if (options.Existing is not ("skip" or "error" or "overwrite")) throw new ArgumentException("--existing must be skip, error or overwrite.");
                    break;
                default: throw new ArgumentException($"Unknown option: {key}");
            }
        }
        if (positional.Count > 2) throw new ArgumentException("Expected one game directory and its exact output directory; no checklist or parent directory.");
        if (positional.Count > 0)
        {
            if (seen.Contains("--input")) throw new ArgumentException("Input was provided twice.");
            options.Input = positional[0];
        }
        if (positional.Count > 1)
        {
            if (seen.Contains("--output")) throw new ArgumentException("Output was provided twice.");
            options.Output = positional[1];
        }
        if (string.IsNullOrWhiteSpace(options.Input) || string.IsNullOrWhiteSpace(options.Output))
            throw new ArgumentException("--input and --output are required (or use positional paths).");
        return options;
    }

    private static bool Boolean(string value, string key) => bool.TryParse(value, out bool result)
        ? result : throw new ArgumentException($"{key} must be true or false.");

    private static (int, int) Size(string value, string key)
    {
        string[] parts = value.ToLowerInvariant().Split('x');
        if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int width) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int height) || width <= 0 || height <= 0)
            throw new ArgumentException($"{key} must be positive WIDTHxHEIGHT, e.g. 1920x1080.");
        return (width, height);
    }

    private static AssetCategories Categories(string value)
    {
        if (value.Equals("all", StringComparison.OrdinalIgnoreCase)) return AssetCategories.All;
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase)) return AssetCategories.None;
        AssetCategories result = AssetCategories.None;
        foreach (string part in value.ToLowerInvariant().Split(','))
            result |= part.Trim() switch
            {
                "text" => AssetCategories.Text,
                "images" => AssetCategories.Images,
                "animation" => AssetCategories.Animation,
                "video" => AssetCategories.Video,
                _ => throw new ArgumentException("--types must be all, none or a comma-separated subset of text,images,animation,video.")
            };
        return result;
    }
}
