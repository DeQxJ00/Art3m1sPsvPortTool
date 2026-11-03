using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Art3m1s.PsvTool.Core;

public sealed record ConversionProfile(string? SourcePlatform, int? SourceWidth, int? SourceHeight,
    int? TargetWidth, int? TargetHeight, double Ratio, bool RuntimeScaledCoordinates,
    bool ResourceDirectorySelector, int? LogicalCoordinateBase, IReadOnlyList<string> ResourceDirectoryIds);
public sealed record ConversionFinding(string Code, string? Archive, string Path, int? Line, string Reason);
internal sealed record ConversionManifest(int SchemaVersion, string? ToolVersion, string Categories, ConversionProfile Profile);
internal sealed record ConversionReport(int SchemaVersion, ConversionProfile Profile, string Scope, ConversionFinding[] Findings);
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ConversionManifest))]
[JsonSerializable(typeof(ConversionReport))]
internal partial class ConversionAnalysisJson : JsonSerializerContext;
internal sealed record ResourceReference(string? Archive, string Path, int Line, string Value, bool Dynamic);

// One run-wide index: scripts may live in different archives from their helpers,
// pictures and layout sidecars. Analysis never executes game code.
internal sealed class ConversionAnalysis
{
    internal const string Marker = "art3m1s_conversion.json";
    internal const string Report = "art3m1s_conversion_report.json";
    private sealed record Source(string? Archive, string File, PfsIndexEntry? Entry, byte[]? Key);
    private readonly Dictionary<string, Source> winners = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, string>> outputPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> ambiguousPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentBag<ConversionFinding> findings = [];
    private readonly ConcurrentBag<ResourceReference> references = [];
    public ConversionProfile Profile { get; private set; } = null!;
    public IReadOnlySet<string> ResourcePaths { get; private set; } = null!;
    public bool LocalizedManual { get; private set; }
    private byte[] manifest = [];

    public static async Task<ConversionAnalysis> CreateAsync(ConversionOptions options, ScanResult scan, CancellationToken token)
    {
        var result = new ConversionAnalysis();
        var codec = new PfsCodec();
        foreach (var archive in ArchiveOrdering.Sort(scan.Archives))
        {
            var index = await codec.ReadIndexAsync(archive.Path, options.NameEncoding, token);
            foreach (var entry in index.Entries)
            {
                if (entry.Path.Equals(Marker, StringComparison.OrdinalIgnoreCase)) AlreadyConverted();
                result.Add(entry.Path, new(archive.FileName, archive.Path, entry, index.XorKey));
            }
        }
        var archives = scan.Archives.Select(a => Path.GetFullPath(a.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(options.InputDirectory, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            if (archives.Contains(Path.GetFullPath(file))) continue;
            string path = Path.GetRelativePath(options.InputDirectory, file).Replace('\\', '/');
            if (path.Equals(Marker, StringComparison.OrdinalIgnoreCase)) AlreadyConverted();
            result.Add(path, new(null, file, null, null));
        }
        result.ResourcePaths = result.winners.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string path in result.ambiguousPaths)
        {
            if (options.Categories.HasFlag(AssetCategories.Text) && Path.GetExtension(path).ToLowerInvariant() is
                ".lua" or ".tbl" or ".ast" or ".asb" or ".iet" or ".ini" or ".csv")
                throw new InvalidDataException($"Ambiguous archive precedence for script '{path}'. Resolve duplicate resources or supply a loose override before text conversion.");
            result.findings.Add(new("ARCHIVE_PRIORITY_AMBIGUOUS", null, path, null, "Duplicate resource is present in archives without a verified numeric overlay order."));
        }
        List<string> scripts = [];
        List<(string? Archive, string Path, int Line, int Value)> directoryValues = [];
        // Read only script metadata, not image/audio payloads. A cap is reported,
        // not interpreted as successful classification of the skipped script.
        foreach (var group in result.winners.Where(p => Path.GetExtension(p.Key).ToLowerInvariant() is ".lua" or ".tbl")
            .GroupBy(p => p.Value.File, StringComparer.OrdinalIgnoreCase))
        {
            await using var stream = File.OpenRead(group.Key);
            foreach (var (path, source) in group)
            {
                token.ThrowIfCancellationRequested();
                long size = source.Entry?.Size ?? stream.Length;
                if (size > 16 * 1024 * 1024) { result.findings.Add(new("ANALYSIS_LIMIT", source.Archive, path, null, "Script exceeds the 16 MiB analysis limit; profile detection may be incomplete.")); continue; }
                byte[] bytes = source.Entry is { } entry ? await PfsCodec.ReadIndexedEntryAsync(stream, entry, source.Key, token)
                    : await File.ReadAllBytesAsync(source.File, token);
                string script = EncodedText.Decode(bytes).Text;
                scripts.Add(script);
                foreach (var item in ArtemisTextProcessor.NumericSettingValues(script, "game_size"))
                    directoryValues.Add((source.Archive, path, item.Line, item.Value));
            }
        }
        var detected = ArtemisTextProcessor.DescribeCoordinateProfile(scripts);
        string[] dirs = result.ResourcePaths.Select(p => p.Split('/')).Where(p => p.Length >= 3 &&
                p[0].ToLowerInvariant() is "ui" or "image" or "movie" && p[1].Length > 0 && p[1].All(char.IsAsciiDigit))
            .Select(p => p[1]).Distinct().Order(StringComparer.Ordinal).ToArray();
        string? platform = scan.Resolutions.FirstOrDefault(r => r.Section.Equals("WINDOWS", StringComparison.OrdinalIgnoreCase) &&
            r.Width == scan.Width && r.Height == scan.Height)?.Section ?? scan.Resolutions.FirstOrDefault(r => r.Width == scan.Width && r.Height == scan.Height)?.Section;
        result.Profile = new(platform, scan.Width, scan.Height, options.TargetWidth, options.TargetHeight, options.Ratio,
            detected.Scaled, detected.Directories, detected.Base, dirs);
        result.LocalizedManual = result.ResourcePaths.Any(p => p.Contains("/manual_", StringComparison.OrdinalIgnoreCase)) &&
            !result.ResourcePaths.Any(p => Path.GetFileNameWithoutExtension(p).Equals("manual", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(Path.GetFileName(Path.GetDirectoryName(p)), out _));
        if (detected.ConflictingBases) result.findings.Add(new("LOGICAL_BASE_AMBIGUOUS", null, "", null, "Multiple logical coordinate bases found; no single base is inferred."));
        if (detected.Directories)
        {
            foreach (var item in directoryValues.Where(v => !dirs.Contains(v.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))))
                result.findings.Add(new("RESOURCE_DIRECTORY_UNVERIFIED", item.Archive, item.Path, item.Line,
                    $"Platform directory ID {item.Value} has no matching numeric ui/image/movie directory. It is preserved; verify which platform column is active at runtime."));
            if (directoryValues.Count == 0) result.findings.Add(new("RESOURCE_DIRECTORY_UNVERIFIED", null, "", null,
                "Resource selector is present, but its platform values are not statically known."));
        }
        if (options.Ratio != 1 && options.Categories.HasFlag(AssetCategories.Text) != options.Categories.HasFlag(AssetCategories.Images))
            result.findings.Add(new("PARTIAL_CONVERSION", null, "", null, "Text and image conversion selections differ; verify coordinate and image dimensions together."));
        if (!scan.HasResolution) result.findings.Add(new("SOURCE_SIZE_UNKNOWN", null, "system.ini", null, "Source resolution is unknown; supplied ratio is retained without inferring dimensions."));
        result.manifest = JsonSerializer.SerializeToUtf8Bytes(new ConversionManifest(1, typeof(ConversionAnalysis).Assembly.GetName().Version?.ToString(),
            options.Categories.ToString(), result.Profile), ConversionAnalysisJson.Default.ConversionManifest);
        return result;
        static void AlreadyConverted() => throw new InvalidDataException("Input contains art3m1s_conversion.json and was already converted. Use original resources to avoid repeated scaling.");
    }
    private void Add(string path, Source source)
    {
        path = path.Replace('\\', '/');
        if (source.Archive is null) ambiguousPaths.Remove(path);
        else if (winners.TryGetValue(path, out var old) && !ArchiveOrdering.CanOrder(old.Archive, source.Archive)) ambiguousPaths.Add(path);
        winners[path] = source;
        if (!outputPaths.TryGetValue(source.Archive ?? "", out var paths)) outputPaths[source.Archive ?? ""] = paths = new(StringComparer.OrdinalIgnoreCase);
        paths[path] = path;
    }
    public void RecordRenames(string? archive, IReadOnlyDictionary<string, string> renamed)
    {
        if (!outputPaths.TryGetValue(archive ?? "", out var paths)) return;
        foreach (var (oldPath, newPath) in renamed) paths[oldPath.Replace('\\', '/')] = newPath.Replace('\\', '/');
    }
    public void AddPreserved(string? archive, string path, IEnumerable<int> lines)
    {
        foreach (int line in lines) findings.Add(new("GEOMETRY_PRESERVED", archive, path, line, "Expression or value could not be safely classified/scaled; original content retained."));
    }
    public async Task InspectOutputAsync(string root, string? archive, CancellationToken token)
    {
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            string path = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (!winners.TryGetValue(path, out var owner) || owner.Archive != archive) continue;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is not (".lua" or ".ast" or ".iet" or ".tbl" or ".ini" or ".asb")) continue;
            if (new FileInfo(file).Length > 16 * 1024 * 1024) { findings.Add(new("AUDIT_LIMIT", archive, path, null, "File exceeds the 16 MiB reference audit limit.")); continue; }
            byte[] data = await File.ReadAllBytesAsync(file, token);
            try
            {
                foreach (var item in ArtemisTextProcessor.FindResourceReferences(data, ext, archive, path)) references.Add(item);
            }
            catch (Exception error) when (error is InvalidDataException or EndOfStreamException or OverflowException or System.Text.DecoderFallbackException)
            {
                findings.Add(new("AUDIT_UNSUPPORTED", archive, path, null, error.Message));
            }
        }
    }
    public Task WriteMarkerAsync(string root, CancellationToken token) => File.WriteAllBytesAsync(Path.Combine(root, Marker), manifest, token);
    public async Task FinishAsync(string staging, IProgress<ConversionProgress>? progress, CancellationToken token)
    {
        var paths = outputPaths.Values.SelectMany(p => p.Values).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var r in references)
        {
            token.ThrowIfCancellationRequested();
            string value = r.Value.Replace('\\', '/');
            if (r.Dynamic && Profile.ResourceDirectorySelector)
            {
                string[] candidates = ArtemisTextProcessor.ResolveDirectoryReference(r.Value, Profile.ResourceDirectoryIds);
                if (candidates.Length > 0 && candidates.All(p => !p.Contains('*') && !p.Contains('$') && !p.Contains("..", StringComparison.Ordinal)))
                {
                    if (!candidates.Any(Exists)) findings.Add(new("RESOURCE_MISSING", r.Archive, r.Path, r.Line,
                        "No output resource matches the recognized directory expression: " + r.Value));
                    continue;
                }
            }
            if (r.Dynamic || value.Contains(':') || value.Contains("..", StringComparison.Ordinal) || value.Contains('*') || value.Contains('$'))
            { findings.Add(new("RESOURCE_DYNAMIC", r.Archive, r.Path, r.Line, "Runtime-dependent resource reference was retained and cannot be proven statically.")); continue; }
            if (value.Length == 0) continue;
            // A bare logical image ID requires the engine's magic-path resolver.
            if (!value.Contains('/') && !Path.HasExtension(value))
            { findings.Add(new("RESOURCE_LOGICAL_ID", r.Archive, r.Path, r.Line, "Logical resource ID requires runtime path resolution.")); continue; }
            if (!Exists(value)) findings.Add(new("RESOURCE_MISSING", r.Archive, r.Path, r.Line, "No output resource matches: " + value));
        }
        var ordered = findings.Distinct().OrderBy(f => f.Archive, StringComparer.Ordinal).ThenBy(f => f.Path, StringComparer.Ordinal)
            .ThenBy(f => f.Line).ThenBy(f => f.Code, StringComparer.Ordinal).ToArray();
        foreach (var f in ordered) progress?.Report(new(99, "validation", f.Archive, f.Path,
            new(ConversionLogLevel.Warning, f.Code, (f.Line is int line ? $"line {line}: " : "") + f.Reason)));
        await WriteMarkerAsync(staging, token);
        var report = new ConversionReport(1, Profile, "Literal resource references; dynamic Lua/magic/localized paths require runtime verification.", ordered);
        await File.WriteAllBytesAsync(Path.Combine(staging, Report), JsonSerializer.SerializeToUtf8Bytes(report, ConversionAnalysisJson.Default.ConversionReport), token);
        bool Exists(string path) => paths.Contains(path) ||
            winners.TryGetValue(path, out var owner) && outputPaths.TryGetValue(owner.Archive ?? "", out var renames) &&
                renames.TryGetValue(path, out string? renamed) && paths.Contains(renamed) ||
            !Path.HasExtension(path) && new[] { ".png", ".jpg", ".jpeg", ".dds", ".pvr", ".lua", ".ast", ".asb", ".iet" }.Any(e => paths.Contains(path + e));
    }
}
