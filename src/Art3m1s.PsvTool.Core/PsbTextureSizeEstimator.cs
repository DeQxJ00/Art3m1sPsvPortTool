namespace Art3m1s.PsvTool.Core;

public sealed record PsbTextureSizeRow(string? Archive, string Path, string Source,
    int Width, int Height, int TargetWidth, int TargetHeight, string SourceFormat, string TargetFormat,
    long SourceBytes, long TargetBytes);

public enum PsbSizeIssueKind { ScanError, NoAtlas, UnsupportedFormat, InvalidSize }
public sealed record PsbSizeIssue(string? Archive, string Path, PsbSizeIssueKind Kind, string? Detail = null);
public sealed record PsbTextureSizeEstimate(int FileCount, int EstimatedFileCount,
    IReadOnlyList<PsbTextureSizeRow> Textures, IReadOnlyList<PsbSizeIssue> Issues)
{
    public long SourceBytes => Textures.Sum(x => x.SourceBytes);
    public long TargetBytes => Textures.Sum(x => x.TargetBytes);
}

/// <summary>Metadata-only payload estimates; excludes PSB tables, GPU allocations and runtime caches.</summary>
public static class PsbTextureSizeEstimator
{
    public static PsbTextureSizeEstimate Estimate(IReadOnlyList<PsbTextureFileInfo> files, double ratio,
        PsbTextureFormat? outputFormat, PsbDxt5Layout layout = PsbDxt5Layout.Swizzled)
    {
        if (!double.IsFinite(ratio) || ratio is <= 0 or > 1) throw new ArgumentOutOfRangeException(nameof(ratio));
        if (outputFormat.HasValue && !Enum.IsDefined(outputFormat.Value)) throw new ArgumentOutOfRangeException(nameof(outputFormat));
        if (!Enum.IsDefined(layout)) throw new ArgumentOutOfRangeException(nameof(layout));
        List<PsbTextureSizeRow> rows = [];
        List<PsbSizeIssue> issues = [];
        int estimated = 0;
        foreach (var file in files)
        {
            if (file.Error is not null || file.Inspection is null)
            { issues.Add(new(file.Archive, file.Path, PsbSizeIssueKind.ScanError, file.Error)); continue; }
            if (!file.Inspection.IsEmoteMotion || file.Inspection.Atlases.Count == 0)
            { issues.Add(new(file.Archive, file.Path, PsbSizeIssueKind.NoAtlas)); continue; }
            // The converter preserves the WHOLE PSB if even one atlas format is unsupported.
            var unsupported = file.Inspection.Atlases.Where(x => !Supported(x.Format)).Select(x => x.Format).Distinct().ToArray();
            if (unsupported.Length > 0)
            { issues.Add(new(file.Archive, file.Path, PsbSizeIssueKind.UnsupportedFormat, string.Join(", ", unsupported))); continue; }
            List<PsbTextureSizeRow> fileRows = [];
            HashSet<(int Index, bool Extra)> resources = [];
            try
            {
                foreach (var atlas in file.Inspection.Atlases)
                {
                    // Shared pixel references are rewritten once, not counted twice.
                    if (atlas.ResourceIndex is { } index && !resources.Add((index, atlas.ExtraResource))) continue;
                    if (atlas.Width <= 0 || atlas.Height <= 0) throw new ArgumentOutOfRangeException(nameof(files));
                    int width = Math.Max(1, (int)(atlas.Width * ratio)), height = Math.Max(1, (int)(atlas.Height * ratio));
                    string targetType = outputFormat.HasValue ? PsbTextureFormats.TypeName(outputFormat.Value, layout) : atlas.Format;
                    fileRows.Add(new(file.Archive, file.Path, atlas.Source, atlas.Width, atlas.Height, width, height,
                        atlas.Format, targetType, PayloadBytes(atlas.Format, atlas.Width, atlas.Height), PayloadBytes(targetType, width, height)));
                }
                rows.AddRange(fileRows); estimated++;
            }
            catch (Exception e) when (e is OverflowException or InvalidDataException or ArgumentOutOfRangeException)
            { issues.Add(new(file.Archive, file.Path, PsbSizeIssueKind.InvalidSize)); }
        }
        return new(files.Count, estimated, rows, issues);
    }

    private static bool Supported(string type) => type.Equals("RGBA8", StringComparison.OrdinalIgnoreCase) || PsbTextureFormats.ParseType(type).HasValue;

    public static long PayloadBytes(string type, int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (type.Equals("RGBA8", StringComparison.OrdinalIgnoreCase)) return checked((long)width * height * 4);
        return PsbTextureFormats.ParseType(type) switch
        {
            PsbTextureFormat.Dxt5 => PsbTextureFormats.ParseDxt5Layout(type) == PsbDxt5Layout.Swizzled
                ? PsbDxt5Storage.SwizzledBytes(width, height) : PsbDxt5Storage.LinearBytes(width, height),
            PsbTextureFormat.Pvrtc2_4 => NativeTextureFormats.PayloadBytes(NativeTextureFormat.Pvrtc2_4, width, height),
            PsbTextureFormat.Pvrtc2_2 => NativeTextureFormats.PayloadBytes(NativeTextureFormat.Pvrtc2_2, width, height),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
    }
}
