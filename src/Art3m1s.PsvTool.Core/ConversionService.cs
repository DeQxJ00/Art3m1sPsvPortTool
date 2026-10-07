using System.Collections.Concurrent;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using System.Text;

namespace Art3m1s.PsvTool.Core;

public interface IConversionService
{
    Task ConvertAsync(
        ConversionOptions options,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class ConversionService : IConversionService
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".ini", ".tbl", ".ipt", ".ast", ".lua" };
    private static readonly HashSet<string> AnimationExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".ogv" };
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".wmv", ".dat", ".mp4", ".avi", ".mpg", ".mkv" };

    private readonly IProjectScanner _scanner;
    private readonly IPfsCodec _pfs;
    private readonly IPngProcessor _png;
    private readonly ITextProcessor _text;
    private readonly IVitaIniProcessor _vita;
    private readonly IFfmpegProcessor _ffmpeg;
    private readonly IFontSubsetProcessor _fonts;
    private readonly IPsbProcessor _psb;

    public ConversionService(
        IProjectScanner? scanner = null,
        IPfsCodec? pfs = null,
        IPngProcessor? png = null,
        ITextProcessor? text = null,
        IVitaIniProcessor? vita = null,
        IFfmpegProcessor? ffmpeg = null,
        IFontSubsetProcessor? fonts = null,
        IPsbProcessor? psb = null)
    {
        _scanner = scanner ?? new ProjectScanner();
        _pfs = pfs ?? new PfsCodec();
        _png = png ?? new PngProcessor();
        _text = text ?? new ArtemisTextProcessor();
        _vita = vita ?? new VitaIniProcessor();
        _ffmpeg = ffmpeg ?? new FfmpegProcessor();
        _fonts = fonts ?? new FontSubsetProcessor();
        _psb = psb ?? new PsbProcessor();
    }

    public async Task ConvertAsync(
        ConversionOptions options,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options.Validate();
        if (File.Exists(Path.Combine(options.InputDirectory, PsbRenderManifest.FileName)))
            throw new InvalidDataException("Input already contains art3m1s_psb_render.json. Please convert the original game resources, not an already converted output.");
        ScanResult scan = await _scanner.ScanAsync(options.InputDirectory, cancellationToken);
        if (scan.Archives.Count == 0)
            throw new InvalidDataException("No valid PFS archives were found.");
        if (Directory.Exists(options.OutputDirectory) && !options.OverwriteExisting)
            throw new IOException("Output directory already exists.");

        IReadOnlyList<TextureImageInfo> textureImages = options.NativeTextures?.Enabled == true && options.Categories.HasFlag(AssetCategories.Images)
            ? await new TextureScanner().ScanAsync(options.InputDirectory, options.NameEncoding,
                progress == null ? null : new MappedProgress(progress, 0, 0.1), cancellationToken) : [];
        if (options.NativeTextures?.Enabled == true && options.Categories.HasFlag(AssetCategories.Images) && progress != null)
            progress = new MappedProgress(progress, 10, 0.9);
        var textureLookup = textureImages.ToDictionary(x => (x.Archive ?? "") + "|" + x.Path, StringComparer.OrdinalIgnoreCase);
        string outputFull = Path.GetFullPath(options.OutputDirectory).TrimEnd(Path.DirectorySeparatorChar);
        string parent = Path.GetDirectoryName(outputFull) ?? throw new InvalidOperationException("Output has no parent directory.");
        Directory.CreateDirectory(parent);
        string staging = Path.Combine(parent, $".{Path.GetFileName(outputFull)}.art3m1s-{Guid.NewGuid():N}");
        string backup = Path.Combine(parent, $".{Path.GetFileName(outputFull)}.backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        bool committed = false;
        ConcurrentBag<PsbRenderModel> psbRenderModels = [];

        try
        {
            progress?.Report(new ConversionProgress(0, "copy"));
            await CopyLooseFilesAsync(options.InputDirectory, staging, scan.Archives, cancellationToken);

            int total = scan.Archives.Count + 1;
            for (int index = 0; index < scan.Archives.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PfsFileInfo archive = scan.Archives[index];
                progress?.Report(new ConversionProgress(100d * index / total, "extract", archive.FileName));
                await ConvertArchiveAsync(archive, staging, options, progress, index, total, textureLookup, psbRenderModels, cancellationToken);
            }

            progress?.Report(new ConversionProgress(100d * scan.Archives.Count / total, "loose"));
            await ProcessTreeAsync(staging, options, progress, cancellationToken, textureLookup, psbRenderModels, archiveName: null, skipPfs: true, preserveDat: false,
                progressStart: 100d * scan.Archives.Count / total, progressSpan: 100d / total);
            if (!psbRenderModels.IsEmpty)
                await new PsbRenderManifest(PsbRenderManifest.CurrentVersion, options.Ratio, psbRenderModels.OrderBy(x => x.Archive, StringComparer.Ordinal)
                    .ThenBy(x => x.Path, StringComparer.Ordinal).ToArray()).WriteAsync(staging, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            CommitDirectory(staging, outputFull, backup, options.OverwriteExisting);
            committed = true;
            progress?.Report(new ConversionProgress(100, "complete"));
        }
        finally
        {
            SafeDeleteTaskDirectory(staging, parent, suppressIoErrors: !committed);
            SafeDeleteTaskDirectory(backup, parent, suppressIoErrors: !committed);
        }
    }

    private async Task ConvertArchiveAsync(
        PfsFileInfo archive,
        string staging,
        ConversionOptions options,
        IProgress<ConversionProgress>? progress,
        int archiveIndex,
        int totalUnits,
        IReadOnlyDictionary<string, TextureImageInfo> textureLookup,
        ConcurrentBag<PsbRenderModel> psbRenderModels,
        CancellationToken cancellationToken)
    {
        string workParent = Path.Combine(staging, ".art3m1s-work");
        string work = Path.Combine(workParent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        bool completed = false;
        try
        {
            ExtractedArchive extracted = await _pfs.ExtractAsync(archive.Path, work, options.NameEncoding, cancellationToken);
            var renamed = await ProcessTreeAsync(work, options, progress, cancellationToken, textureLookup, psbRenderModels, archive.FileName, skipPfs: false, preserveDat: true,
                progressStart: 100d * (archiveIndex + 0.05) / totalUnits,
                progressSpan: 100d * 0.85 / totalUnits);
            string destination = Path.Combine(staging, archive.FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            string temporary = destination + ".packing";
            progress?.Report(new ConversionProgress(100d * (archiveIndex + 0.9) / totalUnits, "pack", archive.FileName));
            // Only the ASCII extension changes; keep original Shift-JIS/UTF-8 name bytes.
            var entries = extracted.Entries.Select(e =>
            {
                if (!renamed.TryGetValue(e.Path, out string? newPath)) return e;
                int dot = Array.LastIndexOf(e.RawName, (byte)'.');
                byte[] extension = Encoding.ASCII.GetBytes(Path.GetExtension(newPath));
                byte[] raw = [.. e.RawName.AsSpan(0, dot).ToArray(), .. extension];
                return e with { RawName = raw, Path = newPath, ExtractedPath = Path.Combine(work, newPath.Replace('/', Path.DirectorySeparatorChar)) };
            }).ToArray();
            await _pfs.PackPf8Async(extracted with { Entries = entries }, temporary, cancellationToken);
            File.Move(temporary, destination, true);
            completed = true;
        }
        catch (OperationCanceledException) { throw; }
        catch (ConversionItemException) { throw; }
        catch (Exception exception)
        {
            throw new ConversionItemException(archive.FileName, null, exception);
        }
        finally
        {
            SafeDeleteTaskDirectory(work, workParent, suppressIoErrors: !completed);
            try
            {
                if (Directory.Exists(workParent) && !Directory.EnumerateFileSystemEntries(workParent).Any())
                    Directory.Delete(workParent);
            }
            catch (Exception exception) when (!completed && exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> ProcessTreeAsync(
        string root,
        ConversionOptions options,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, TextureImageInfo> textureLookup,
        ConcurrentBag<PsbRenderModel> psbRenderModels,
        string? archiveName,
        bool skipPfs,
        bool preserveDat,
        double progressStart,
        double progressSpan)
    {
        string[] files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !skipPfs || !IsPfsFileName(Path.GetFileName(path)))
            .ToArray();
        IReadOnlySet<int> usedCodePoints = options.SubsetFonts ? await CollectUsedCodePointsAsync(files, cancellationToken) : new HashSet<int>();
        using SemaphoreSlim videoSlots = new(2);
        using SemaphoreSlim animationSlots = new(1);
        using SemaphoreSlim psbSlots = new(1);
        int completed = 0;
        ConcurrentDictionary<string, string> renamed = new(StringComparer.OrdinalIgnoreCase);
        int converted = 0, kept = 0; long beforeBytes = 0, afterBytes = 0;
        await Parallel.ForEachAsync(files, new ParallelOptions
        {
            MaxDegreeOfParallelism = options.EffectiveParallelism,
            CancellationToken = cancellationToken
        }, async (path, token) =>
        {
            string extension = Path.GetExtension(path);
            string relativePath = Path.GetRelativePath(root, path);
            string progressEntry = relativePath;
            try
            {
                if (TextExtensions.Contains(extension) && options.Categories.HasFlag(AssetCategories.Text))
                {
                    if (_text is ArtemisTextProcessor artemis)
                    {
                        IReadOnlyList<int> preserved = await artemis.ProcessWithDiagnosticsAsync(path, options.Ratio, token);
                        if (preserved.Count > 0)
                            progressEntry = $"{relativePath} · text: preserved dynamic expressions at lines {string.Join(",", preserved.Take(8))}" +
                                (preserved.Count > 8 ? $" (+{preserved.Count - 8})" : "");
                    }
                    else await _text.ProcessAsync(path, options.Ratio, token);
                }
                else if (TextureScanner.IsImage(path) && options.Categories.HasFlag(AssetCategories.Images))
                {
                    string normalized = relativePath.Replace('\\', '/');
                    textureLookup.TryGetValue((archiveName ?? "") + "|" + normalized, out var info);
                    bool readable = info?.Error == null;
                    if (readable && extension.Equals(".png", StringComparison.OrdinalIgnoreCase)) await _png.ResizeAsync(path, options.Ratio, token);
                    else if (readable && options.NativeTextures?.Enabled == true && extension.ToLowerInvariant() is ".jpg" or ".jpeg")
                    {
                        using Image image = await Image.LoadAsync(path, token);
                        image.Mutate(x => x.Resize(Math.Max(1, (int)(image.Width * options.Ratio)), Math.Max(1, (int)(image.Height * options.Ratio))));
                        await image.SaveAsync(path, token);
                    }
                    if (options.NativeTextures?.Enabled == true && info != null)
                    {
                        var result = await new NativeTextureProcessor().ConvertAsync(path, info, options.NativeTextures, options.Ratio, token);
                        if (result.Converted)
                        {
                            renamed[normalized] = Path.GetRelativePath(root, result.OutputPath).Replace('\\', '/');
                            Interlocked.Increment(ref converted);
                        }
                        else Interlocked.Increment(ref kept);
                        Interlocked.Add(ref beforeBytes, result.Before); Interlocked.Add(ref afterBytes, result.After);
                        progressEntry += $" · {result.Message} · {result.Before:N0} → {result.After:N0} B";
                    }
                }
                else if (options.SubsetFonts && (extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase) ||
                                                 extension.Equals(".otf", StringComparison.OrdinalIgnoreCase)))
                    await _fonts.SubsetAsync(path, options.FontProfile, usedCodePoints, token);
                else if (extension.Equals(".psb", StringComparison.OrdinalIgnoreCase) &&
                         (options.Categories.HasFlag(AssetCategories.Animation) || options.ConvertEmotePsbTexturesToDxt5))
                {
                    await psbSlots.WaitAsync(token);
                    try
                    {
                        double psbRatio = options.Categories.HasFlag(AssetCategories.Animation) ? options.Ratio : 1;
                        if (options.Categories.HasFlag(AssetCategories.Animation) && options.PsbTextures?.Enabled == true)
                        {
                            try
                            {
                                PsbTextureInspection inspection = await PsbTextureScanner.InspectFileAsync(path, token);
                                psbRatio = options.PsbTextures.RatioFor(inspection.Width, inspection.Height);
                            }
                            catch (Exception error) when (error is InvalidDataException or NotSupportedException or
                                                          IndexOutOfRangeException or OverflowException or ArgumentException)
                            {
                                // Let the processor's existing validation/preservation path
                                // handle unsupported PSBs rather than changing that behavior.
                                psbRatio = 0.5;
                            }
                        }
                        double geometryRatio = options.Categories.HasFlag(AssetCategories.Animation) ? options.Ratio : 1;
                        PsbProcessingResult result = await _psb.ProcessWithFormatAsync(path, psbRatio, geometryRatio,
                            options.ConvertEmotePsbTexturesToDxt5 ? options.PsbOutputFormat : null, token);
                        if (options.Categories.HasFlag(AssetCategories.Animation) && options.PsbTextures is { Enabled: true, CompensateRendering: true }
                            && result.IsEmoteMotion && result.Changed)
                            psbRenderModels.Add(new(relativePath.Replace('\\', '/'), archiveName,
                                await PsbRenderManifest.FingerprintFileAsync(path, token),
                                psbRatio, geometryRatio / psbRatio));
                        progressEntry = $"{relativePath} · Texture Ratio {psbRatio:0.###} · Geometry Ratio {geometryRatio:0.###} · {result.Message}";
                    }
                    finally { psbSlots.Release(); }
                }
                else if (archiveName is not null && options.IgnorePfsVideos && VideoExtensions.Contains(extension))
                {
                    // Some Artemis titles store a second, engine-protected video stream inside
                    // PFS entries. The default is deliberately byte-preserving so FFmpeg never
                    // mistakes those files for ordinary media.
                }
                else if (preserveDat && extension.Equals(".dat", StringComparison.OrdinalIgnoreCase))
                {
                    // DAT files inside PFS archives can be arbitrary game data (for example
                    // font caches). Preserve every archived DAT verbatim; only loose DAT files
                    // are probed and converted when they contain a video stream.
                }
                else if ((AnimationExtensions.Contains(extension) && options.Categories.HasFlag(AssetCategories.Animation)) ||
                         (VideoExtensions.Contains(extension) && options.Categories.HasFlag(AssetCategories.Video)))
                {
                    SemaphoreSlim slots = AnimationExtensions.Contains(extension) ? animationSlots : videoSlots;
                    await slots.WaitAsync(token);
                    try
                    {
                        await _ffmpeg.ResizeAsync(path, options.Ratio,
                            convertToH264Mp4: archiveName is null && VideoExtensions.Contains(extension),
                            cancellationToken: token);
                    }
                    finally { slots.Release(); }
                }

                if (Path.GetFileName(path).Equals("system.ini", StringComparison.OrdinalIgnoreCase))
                    await _vita.EnsureVitaSectionAsync(path, token);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                throw new ConversionItemException(archiveName, relativePath, exception);
            }

            int done = Interlocked.Increment(ref completed);
            double fraction = files.Length == 0 ? 1 : (double)done / files.Length;
            progress?.Report(new ConversionProgress(progressStart + progressSpan * fraction, "resource", archiveName, progressEntry));
        });
        if (options.NativeTextures?.Enabled == true)
            progress?.Report(new(progressStart + progressSpan, "resource", archiveName,
                $"PSV textures: converted={converted}, kept={kept}, resized source={beforeBytes:N0} B, output={afterBytes:N0} B"));
        return renamed;
    }

    private static async Task<IReadOnlySet<int>> CollectUsedCodePointsAsync(IEnumerable<string> files, CancellationToken cancellationToken)
    {
        HashSet<int> result = [];
        foreach (string path in files.Where(path => TextExtensions.Contains(Path.GetExtension(path))))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken);
                string text = EncodedText.Decode(bytes).Text;
                foreach (System.Text.Rune rune in text.EnumerateRunes())
                    if (rune.Value is < 0xF0000 or > 0xF00FF) result.Add(rune.Value);
            }
            catch (DecoderFallbackException) { }
        }
        return result;
    }

    private static async Task CopyLooseFilesAsync(
        string input,
        string output,
        IReadOnlyList<PfsFileInfo> archives,
        CancellationToken cancellationToken)
    {
        HashSet<string> archivePaths = archives.Select(item => Path.GetFullPath(item.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string directory in Directory.EnumerateDirectories(input, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(output, Path.GetRelativePath(input, directory)));
        foreach (string file in Directory.EnumerateFiles(input, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (archivePaths.Contains(Path.GetFullPath(file))) continue;
            string target = Path.Combine(output, Path.GetRelativePath(input, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using FileStream source = new(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
            await using FileStream destination = new(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, true);
            await source.CopyToAsync(destination, cancellationToken);
        }
    }

    private static void CommitDirectory(string staging, string output, string backup, bool overwrite)
    {
        bool hadOutput = Directory.Exists(output);
        if (hadOutput)
        {
            if (!overwrite) throw new IOException("Output directory already exists.");
            Directory.Move(output, backup);
        }
        try
        {
            Directory.Move(staging, output);
            if (hadOutput) Directory.Delete(backup, true);
        }
        catch
        {
            if (!Directory.Exists(output) && Directory.Exists(backup)) Directory.Move(backup, output);
            throw;
        }
    }

    private sealed class MappedProgress(IProgress<ConversionProgress> target, double start, double scale) : IProgress<ConversionProgress>
    {
        public void Report(ConversionProgress value) => target.Report(value with { Percent = start + value.Percent * scale });
    }

    private static bool IsPfsFileName(string name)
    {
        int marker = name.LastIndexOf(".pfs", StringComparison.OrdinalIgnoreCase);
        if (marker <= 0) return false;
        string suffix = name[(marker + 4)..];
        return suffix.Length == 0 || (suffix.Length > 1 && suffix[0] == '.' && !suffix[1..].Contains('.'));
    }

    private static void SafeDeleteTaskDirectory(string path, string expectedParent, bool suppressIoErrors = false)
    {
        if (!Directory.Exists(path)) return;
        string parent = Path.GetFullPath(expectedParent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string target = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(path).Contains("art3m1s", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(expectedParent).Equals(".art3m1s-work", StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing to remove an unexpected temporary directory.");
        try { Directory.Delete(path, true); }
        catch (Exception exception) when (suppressIoErrors && exception is IOException or UnauthorizedAccessException) { }
    }
}
