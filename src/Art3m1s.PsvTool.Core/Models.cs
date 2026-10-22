namespace Art3m1s.PsvTool.Core;

[Flags]
public enum AssetCategories
{
    None = 0,
    Text = 1,
    Images = 2,
    Animation = 4,
    Video = 8,
    All = Text | Images | Animation | Video
}

public enum PfsNameEncoding
{
    Auto,
    Utf8,
    ShiftJis
}

public enum FontSubsetProfile
{
    SimplifiedChinese,
    Japanese,
    TraditionalChinese
}

public sealed record ConversionOptions(
    string InputDirectory,
    string OutputDirectory,
    double Ratio = ConversionDefaults.Ratio,
    AssetCategories Categories = ConversionDefaults.Categories,
    int MaxParallelism = ConversionDefaults.MaxParallelism,
    PfsNameEncoding NameEncoding = ConversionDefaults.NameEncoding,
    bool OverwriteExisting = false,
    bool SubsetFonts = ConversionDefaults.SubsetFonts,
    FontSubsetProfile FontProfile = ConversionDefaults.FontProfile,
    bool IgnorePfsVideos = ConversionDefaults.IgnorePfsVideos,
    bool ConvertEmotePsbTexturesToDxt5 = ConversionDefaults.ConvertEmotePsbTextures,
    NativeTextureOptions? NativeTextures = null,
    PsbTextureOptions? PsbTextures = null,
    PsbTextureFormat PsbOutputFormat = ConversionDefaults.PsbOutputFormat,
    PsbDxt5Layout Dxt5Layout = ConversionDefaults.Dxt5Layout,
    int? TargetWidth = null,
    int? TargetHeight = null)
{
    public int EffectiveParallelism => MaxParallelism > 0
        ? MaxParallelism
        : Math.Max(1, Environment.ProcessorCount - 1);

    public void Validate()
    {
        if (TargetWidth.HasValue != TargetHeight.HasValue || TargetWidth is <= 0 || TargetHeight is <= 0)
            throw new ArgumentException("Target width and height must both be positive or both omitted.");
        PsbTextures?.Validate();
        _ = PsbTextureFormats.Name(PsbOutputFormat);
        if (!Enum.IsDefined(Dxt5Layout)) throw new ArgumentOutOfRangeException(nameof(Dxt5Layout));
        if (!Directory.Exists(InputDirectory))
            throw new DirectoryNotFoundException(InputDirectory);
        if (Ratio is <= 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(Ratio), "Ratio must be greater than 0 and no greater than 1.");
        if (Path.GetFullPath(InputDirectory).TrimEnd(Path.DirectorySeparatorChar)
            .Equals(Path.GetFullPath(OutputDirectory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Input and output directories must be different.");

        string input = Path.GetFullPath(InputDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string output = Path.GetFullPath(OutputDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (output.StartsWith(input, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Output directory cannot be inside the input directory.");
        if (input.StartsWith(output, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Input directory cannot be inside the output directory.");
    }
}

public sealed record PfsFileInfo(string Path, string FileName, char Version, long Length);

public sealed record IniResolution(string Source, string Section, int Width, int Height);

public sealed record ScanResult(
    IReadOnlyList<PfsFileInfo> Archives,
    bool HasSystemIni,
    int? Width,
    int? Height,
    IReadOnlyList<IniResolution> Resolutions)
{
    public bool HasResolution => Width > 0 && Height > 0;
}

public sealed record ConversionProgress(
    double Percent,
    string Stage,
    string? Archive = null,
    string? Entry = null,
    ConversionDiagnostic? Diagnostic = null,
    string? ResourcePath = null);

public enum ConversionLogLevel { Info, Warning, Error }
public sealed record ConversionDiagnostic(ConversionLogLevel Level, string Code, string Message);

public sealed class ConversionItemException : Exception
{
    public ConversionItemException(string? archive, string? entry, Exception innerException, string? stage = null)
        : base(innerException.Message, innerException)
    {
        Archive = archive;
        Entry = entry;
        Stage = stage;
    }

    public string? Archive { get; }
    public string? Entry { get; }
    public string? Stage { get; }
}

public sealed record PfsEntry(
    byte[] RawName,
    string Path,
    uint Offset,
    uint Size,
    string ExtractedPath);

public sealed record ExtractedArchive(char SourceVersion, IReadOnlyList<PfsEntry> Entries);
