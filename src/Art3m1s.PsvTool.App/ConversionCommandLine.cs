using Art3m1s.PsvTool.Core;

namespace Art3m1s.PsvTool.App;

internal static class ConversionCommandLine
{
    public static string Help => $$"""
        art3m1s PSV command-line conversion / 命令行转换
        Only modify authorized resources; back up originals first.
        请只处理你有权修改的资源，并先备份原项目。

        Usage:
          Art3m1s.PsvTool.App --convert --input GAME_DIR --output OUTPUT_DIR [options]
          Art3m1s.PsvTool.App --convert GAME_DIR OUTPUT_DIR [options]

        One invocation converts ONE game, not a parent directory of games.
        Input must contain a PFS archive directly; output is the exact destination.
        To batch multiple games, call this command repeatedly from your own script.
        --batch-convert is an alias with the same single-game behavior.
        GUI settings are not loaded. Quote paths containing spaces.
        The ui-defaults preset is implicit; individual options override shared UI defaults.
        Boolean options require true or false.

          -i, --input PATH             Required game root directory containing PFS
          -o, --output PATH            Required exact output directory; no nesting added
          --preset ui-defaults         Shared fresh-UI defaults (also used when omitted)
          --ratio auto|NUMBER          Default {{ConversionDefaults.Ratio.ToString(System.Globalization.CultureInfo.InvariantCulture)}}; auto fits --fit, NUMBER > 0 and <= 1
          --source-resolution WxH      Override this game's detected source size
          --fit WxH                    Auto-ratio bounds, default 960x540 (no upscale)
          --types LIST                 all (default), none, or comma-separated
                                       text,images,animation,video
          --parallel auto|N            Workers within this game; default CPU cores - 1
          --encoding auto|utf-8|shift-jis  PFS filename encoding; default auto
          --native-textures true|false  Recommended image categories; default true
          --ignore-bg-alpha true|false  Default false
          --psb-format FORMAT          dxt5 (default), pvrtc2-4, pvrtc2-2, preserve
          --dxt5-layout LAYOUT         swizzled (default) or linear; images and PSB
          --subset-fonts true|false     Default {{ConversionDefaults.SubsetFonts.ToString().ToLowerInvariant()}} (independent of --types)
          --font-profile PROFILE       simplified (default), japanese, traditional
          --ignore-pfs-videos true|false  Default true; excludes OGV
          --existing POLICY            skip (default), error, overwrite
          --log PATH                   Full UTF-8 log; default OUTPUT_PARENT/logs/GAME/GAME.log (append)
          --dry-run                    Scan/preview only; no files are written
          --verbose                    Every progress event and exception stack
          -h, --help                   Show help without conversion
          --version                    Print application version

        No parent-directory traversal, project checklist or internal batch queue.
        Overwrite replaces the entire output directory without prompting.
        Ctrl+C cancels conversion. Existing output is preserved on failure/cancellation.
        Exit codes: 0 success/skip/preview, 1 conversion failure, 2 invalid setup, 130 cancelled.
        Windows PowerShell: pipe to Out-Host to wait and display console output.
        See README.md / README.en.md for single-game and script batch examples.
        """;

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default,
        IProjectScanner? scanner = null, IConversionService? converter = null,
        TextWriter? standardOutput = null, TextWriter? standardError = null, string? fallbackLogDirectory = null)
    {
        TextWriter output = standardOutput ?? Console.Out, error = standardError ?? Console.Error;
        if (args.Skip(1).Any(x => x is "--help" or "-h")) { output.WriteLine(Help); return 0; }
        ConversionRunLog? runLog = null;
        FileStream? conversionLock = null;
        string? sourceHint = null;
        bool dryRun = args.Any(x => x == "--dry-run" || x.StartsWith("--dry-run=", StringComparison.Ordinal));
        int resultCode = 2;
        string outcome = "setup-failed";
        void Log(string message) => runLog?.Info("STATUS", message);
        int Finish(int code, string status)
        {
            resultCode = code == 0 && runLog?.HasWriteFailure == true ? 2 : code;
            outcome = resultCode == 2 && code == 0 ? "completed-with-log-errors" : status;
            return resultCode;
        }
        try
        {
            ConversionCommandLineOptions settings = ConversionCommandLineOptions.Parse(args);
            sourceHint = settings.Input;
            string source = FullPath(settings.Input), destination = FullPath(settings.Output);
            if (!Directory.Exists(source)) throw new ArgumentException("Input game directory does not exist: " + source);
            if (Contains(source, destination) || Contains(destination, source))
                throw new ArgumentException("Input and output directories must not overlap.");
            if (Path.GetDirectoryName(destination) is null) throw new ArgumentException("Output must not be a filesystem root.");
            string? logPath = settings.Log is null ? null : FullPath(settings.Log);
            string lockPath = destination + ".conversion.lock";
            foreach (string candidate in new[] { source, destination, logPath, lockPath }.OfType<string>()) RejectLinkedAncestors(candidate);
            if (logPath != null && (Contains(source, logPath) || Contains(destination, logPath) || PathEquals(logPath, lockPath)))
                throw new ArgumentException("Log must be outside input/output directories and differ from the lock path.");
            cancellationToken.ThrowIfCancellationRequested();
            if (!settings.DryRun)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                // Delete through the owned handle, never by path after releasing the lock.
                // .NET 10 also checks inode identity for this combination on Unix.
                conversionLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                    FileShare.None, bufferSize: 1, FileOptions.DeleteOnClose);
            }
            runLog = new ConversionRunLog(source, destination, logPath, !settings.DryRun, settings.Verbose, output, error);
            Log($"START input={source}; output={destination}; dry-run={settings.DryRun}");
            Log($"OPTIONS preset=ui-defaults; types={settings.Types}; native-textures={settings.NativeTextures}; psb-format={settings.PsbFormat?.ToString() ?? "preserve"}; layout={settings.Layout}; subset-fonts={settings.SubsetFonts}; font-profile={settings.FontProfile}; ignore-pfs-videos={settings.IgnorePfsVideos}; existing={settings.Existing}; parallel={settings.Parallelism}; encoding={settings.Encoding}; ignore-bg-alpha={settings.IgnoreBackgroundAlpha}");
            scanner ??= new ProjectScanner();
            converter ??= new ConversionService();
            try
            {
                RejectDirectoryLinks(source, cancellationToken);
                runLog.Report(new(0, "scan", Entry: "Project archive and resolution scan"));
                ScanResult scan = await scanner.ScanAsync(source, cancellationToken);
                Log($"SCAN archives={scan.Archives.Count}; files={string.Join(", ", scan.Archives.Select(x => x.FileName))}");
                if (!scan.Archives.Any(archive => PathEquals(Path.GetDirectoryName(FullPath(archive.Path))!, source)))
                    throw new InvalidDataException("Input must be a single game root containing a PFS archive directly, not a parent directory of games.");
                bool exists = Directory.Exists(destination) || File.Exists(destination);
                if (exists && settings.Existing == "skip") { Log("SKIP existing output: " + destination); return Finish(0, "skipped"); }
                if (exists && settings.Existing == "error") throw new IOException("Output already exists: " + destination);
                var preferred = scan.Resolutions.FirstOrDefault(x => x.Width > 0 && x.Height > 0);
                preferred = scan.Resolutions.FirstOrDefault(x => x.Source == preferred?.Source &&
                    x.Section.Equals("WINDOWS", StringComparison.OrdinalIgnoreCase) && x.Width > 0 && x.Height > 0) ?? preferred;
                int? width = settings.SourceResolution?.Width ?? preferred?.Width ?? scan.Width;
                int? height = settings.SourceResolution?.Height ?? preferred?.Height ?? scan.Height;
                bool hasSize = width > 0 && height > 0;
                if (!hasSize && settings.Ratio is null)
                    throw new InvalidDataException("Cannot detect resolution. Set --source-resolution WIDTHxHEIGHT or --ratio NUMBER.");
                double ratio = settings.Ratio ?? Math.Min(1d, Math.Min((double)settings.Fit.Width / width!.Value, (double)settings.Fit.Height / height!.Value));
                int? targetWidth = hasSize ? Math.Max(1, (int)(width!.Value * ratio)) : null;
                int? targetHeight = hasSize ? Math.Max(1, (int)(height!.Value * ratio)) : null;
                Log(FormattableString.Invariant($"RESOLUTION {width}x{height}; ratio={ratio:R}; target={targetWidth}x{targetHeight}"));
                if (settings.DryRun) { Log("PREVIEW " + source); return Finish(0, "preview"); }
                NativeTextureRule[] rules = [];
                if (settings.NativeTextures && settings.Types.HasFlag(AssetCategories.Images))
                {
                    var images = await new TextureScanner().ScanAsync(source, settings.Encoding,
                        runLog, cancellationToken);
                    rules = images.GroupBy(x => x.GroupKey).OrderBy(x => x.Key, StringComparer.Ordinal)
                        .Select(group => new TextureCategoryViewModel(group.ToArray(), ratio, settings.IgnoreBackgroundAlpha, true).Rule)
                        .Select(rule => settings.Layout == PsbDxt5Layout.Linear && rule.Format == NativeTextureFormat.Auto
                            ? rule with { Format = NativeTextureFormat.AutoLinear } : rule).ToArray();
                    Log($"CLASSIFIED images={images.Count}; categories={rules.Length}");
                }
                ConversionOptions options = new(source, destination, ratio, settings.Types,
                    MaxParallelism: settings.Parallelism, NameEncoding: settings.Encoding,
                    OverwriteExisting: settings.Existing == "overwrite",
                    SubsetFonts: settings.SubsetFonts, FontProfile: settings.FontProfile,
                    IgnorePfsVideos: settings.IgnorePfsVideos,
                    ConvertEmotePsbTexturesToDxt5: settings.PsbFormat.HasValue,
                    NativeTextures: new NativeTextureOptions(settings.NativeTextures, settings.IgnoreBackgroundAlpha, rules),
                    PsbTextures: new PsbTextureOptions(Enabled: ConversionDefaults.IndependentPsbScaling, CompensateRendering: ConversionDefaults.PsbRenderCompensation),
                    PsbOutputFormat: settings.PsbFormat ?? PsbTextureFormat.Dxt5, Dxt5Layout: settings.Layout,
                    TargetWidth: targetWidth, TargetHeight: targetHeight);
                await converter.ConvertAsync(options, runLog, cancellationToken);
                Log("DONE output=" + destination);
                return Finish(0, "success");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                runLog.Exception(ex);
                return Finish(1, "failed");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Log("CANCELLED; no partial result is published.");
            return Finish(130, "cancelled");
        }
        catch (Exception ex)
        {
            error.WriteLine("SETUP ERROR: " + ex.Message + Environment.NewLine + "Use --convert --help for usage.");
            if (runLog == null && !dryRun)
            {
                try
                {
                    sourceHint ??= InputHint(args);
                    runLog = ConversionRunLog.Fallback(sourceHint, output, error, fallbackLogDirectory);
                }
                catch (Exception logError) when (logError is IOException or UnauthorizedAccessException or ArgumentException)
                { error.WriteLine("LOG_FALLBACK_FAILED: " + logError.Message); }
            }
            runLog?.Exception(ex, "setup");
            return Finish(2, "setup-failed");
        }
        finally
        {
            try { runLog?.Complete(outcome, resultCode); }
            finally
            {
                try { runLog?.Dispose(); }
                finally { conversionLock?.Dispose(); }
            }
        }
    }

    private static string FullPath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private static bool PathEquals(string left, string right) => left.Equals(right,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static bool Contains(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        return relative == "." || (!Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }
    private static void RejectLinkedAncestors(string path)
    {
        for (string? current = path; current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Symbolic links/junctions are not supported in CLI paths: " + current);
    }
    private static void RejectDirectoryLinks(string root, CancellationToken cancellationToken)
    {
        Stack<string> pending = new([root]);
        while (pending.TryPop(out string? directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Symbolic links/junctions are not supported in source directories: " + directory);
            foreach (string child in Directory.EnumerateDirectories(directory)) pending.Push(child);
            foreach (string file in Directory.EnumerateFiles(directory))
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("Symbolic links are not supported in source files: " + file);
        }
    }

    private static string? InputHint(string[] args)
    {
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i].StartsWith("--input=", StringComparison.Ordinal)) return args[i][8..];
            if (args[i] is "--input" or "-i" && i + 1 < args.Length) return args[i + 1];
        }
        return args.Length > 1 && !args[1].StartsWith('-') ? args[1] : null;
    }
}
