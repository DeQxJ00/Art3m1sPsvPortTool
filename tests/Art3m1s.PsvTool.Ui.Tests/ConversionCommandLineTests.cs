using System.Diagnostics;
using System.Text;
using Art3m1s.PsvTool.App;
using Art3m1s.PsvTool.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Art3m1s.PsvTool.Ui.Tests;

public sealed class ConversionCommandLineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "art3m1s-cli-test-" + Guid.NewGuid().ToString("N"));
    private string Input => Path.Combine(_root, "sample01");
    private string Output => Path.Combine(_root, "converted");
    public ConversionCommandLineTests() { Directory.CreateDirectory(Input); }
    public void Dispose() { Directory.Delete(_root, recursive: true); }

    private string[] Args(params string[] extra) => ["--convert", "--input", Input, "--output", Output,
        "--subset-fonts", "true", "--font-profile", "simplified", .. extra];

    [Theory]
    [InlineData("--convert")]
    [InlineData("--batch-convert")]
    public void PositionalPathsAndDefaults(string command)
    {
        var options = ConversionCommandLineOptions.Parse([command, "input", "output"]);
        Assert.Equal("input", options.Input);
        Assert.Equal("output", options.Output);
        Assert.Null(options.Ratio);
        Assert.Equal((960, 540), options.Fit);
        Assert.True(options.NativeTextures);
        Assert.False(options.SubsetFonts);
        Assert.True(options.IgnorePfsVideos);
        Assert.Equal(PsbDxt5Layout.Swizzled, options.Layout);
        Assert.Equal("skip", options.Existing);
    }

    [Fact]
    public void NamedOptionsMapAllSettings()
    {
        var options = ConversionCommandLineOptions.Parse(["--convert", "-i", "input", "-o", "output",
            "--ratio=0.75", "--source-resolution", "1280x720", "--fit", "960x544", "--parallel", "3",
            "--encoding", "shift-jis", "--types", "text,animation", "--native-textures", "false",
            "--ignore-bg-alpha", "true", "--psb-format", "pvrtc2-2", "--dxt5-layout", "linear",
            "--subset-fonts", "false", "--font-profile", "traditional", "--ignore-pfs-videos", "false",
            "--existing", "overwrite", "--log", "custom.log", "--dry-run", "--verbose", "--preset", "ui-defaults"]);
        Assert.Equal(.75, options.Ratio);
        Assert.Equal((1280, 720), options.SourceResolution);
        Assert.Equal((960, 544), options.Fit);
        Assert.Equal(3, options.Parallelism);
        Assert.Equal(PfsNameEncoding.ShiftJis, options.Encoding);
        Assert.Equal(AssetCategories.Text | AssetCategories.Animation, options.Types);
        Assert.False(options.NativeTextures);
        Assert.True(options.IgnoreBackgroundAlpha);
        Assert.Equal(PsbTextureFormat.Pvrtc2_2, options.PsbFormat);
        Assert.Equal(PsbDxt5Layout.Linear, options.Layout);
        Assert.False(options.SubsetFonts);
        Assert.Equal(FontSubsetProfile.TraditionalChinese, options.FontProfile);
        Assert.False(options.IgnorePfsVideos);
        Assert.Equal("overwrite", options.Existing);
        Assert.Equal("custom.log", options.Log);
        Assert.True(options.DryRun);
        Assert.True(options.Verbose);
    }

    [Theory]
    [InlineData("--ratio", "0")]
    [InlineData("--ratio", "1.1")]
    [InlineData("--ratio", "NaN")]
    [InlineData("--ratio", "Infinity")]
    [InlineData("--ratio", "0,5")]
    [InlineData("--parallel", "0")]
    [InlineData("--parallel", "-1")]
    [InlineData("--source-resolution", "0x720")]
    [InlineData("--fit", "960")]
    [InlineData("--types", "text,unknown")]
    [InlineData("--encoding", "unknown")]
    [InlineData("--psb-format", "unknown")]
    [InlineData("--dxt5-layout", "unknown")]
    [InlineData("--subset-fonts", "yes")]
    [InlineData("--font-profile", "unknown")]
    [InlineData("--existing", "delete")]
    [InlineData("--unknown", "value")]
    [InlineData("--preset", "unknown")]
    [InlineData("--todo", "todo.md")]
    [InlineData("--continue-on-error", "true")]
    public void InvalidOrRemovedOptionsAreRejected(string key, string value) =>
        Assert.Throws<ArgumentException>(() => ConversionCommandLineOptions.Parse(["--convert", "in", "out", key, value]));

    [Fact]
    public void MissingDuplicateOrExtraArgumentsAreRejected()
    {
        foreach (string[] args in new string[][] {
            ["--convert"],
            ["--convert", "in", "out", "--ratio"],
            ["--convert", "in", "out", "--input", "other"],
            ["--convert", "in", "out", "--ratio", "0.5", "--ratio", "0.75"],
            ["--convert", "in", "out", "--dry-run=true"],
            ["--convert", "in", "out", "todo.md"] })
            Assert.Throws<ArgumentException>(() => ConversionCommandLineOptions.Parse(args));
    }

    [Fact]
    public async Task HelpRequiresNoPathsAndWritesNoFiles()
    {
        StringWriter output = new();
        Assert.Equal(0, await ConversionCommandLine.RunAsync(["--convert", "--help"], standardOutput: output));
        Assert.Contains("ONE game", output.ToString());
        Assert.DoesNotContain("--todo", output.ToString());
        Assert.False(Directory.Exists(Output));
    }

    [Theory]
    [InlineData("--convert")]
    [InlineData("--batch-convert")]
    public async Task OneInvocationUsesExactGameAndOutputPaths(string command)
    {
        Directory.CreateDirectory(Path.Combine(Input, "movie"));
        Directory.CreateDirectory(Path.Combine(Input, "system"));
        FakeConverter converter = new();
        string[] args = Args();
        args[0] = command;
        Assert.Equal(0, await Run(args, converter));
        ConversionOptions call = Assert.Single(converter.Calls);
        Assert.Equal(Input, call.InputDirectory);
        Assert.Equal(Output, call.OutputDirectory);
        Assert.False(Directory.Exists(Path.Combine(Output, "sample01")));
        Assert.False(File.Exists(Output + ".conversion.lock"));
    }

    [Theory]
    [InlineData("skip", 0, false)]
    [InlineData("error", 1, false)]
    [InlineData("overwrite", 0, true)]
    public async Task ExistingOutputPolicies(string policy, int exitCode, bool called)
    {
        Directory.CreateDirectory(Output);
        File.WriteAllText(Path.Combine(Output, "keep.txt"), "original");
        FakeConverter converter = new();
        Assert.Equal(exitCode, await Run(Args("--existing", policy), converter));
        Assert.Equal(called ? 1 : 0, converter.Calls.Count);
        if (called) Assert.True(converter.Calls[0].OverwriteExisting);
        Assert.Equal("original", File.ReadAllText(Path.Combine(Output, "keep.txt")));
        Assert.False(File.Exists(Output + ".conversion.lock"));
    }

    [Fact]
    public async Task DryRunUsesWindowsResolutionAndWritesNothing()
    {
        FakeConverter converter = new();
        StringWriter output = new();
        Assert.Equal(0, await ConversionCommandLine.RunAsync(Args("--dry-run"), scanner: new FakeScanner(),
            converter: converter, standardOutput: output, standardError: new StringWriter()));
        Assert.Contains("ratio=0.5; target=960x540", output.ToString());
        Assert.Empty(converter.Calls);
        Assert.False(Directory.Exists(Output));
        Assert.False(File.Exists(Output + ".conversion.log"));
        Assert.False(File.Exists(Output + ".conversion.lock"));
    }

    [Fact]
    public async Task OptionsForwardedWithoutIndependentPsbRatio()
    {
        FakeConverter converter = new();
        Assert.Equal(0, await Run(Args("--ratio", "0.75", "--source-resolution", "1280x720",
            "--parallel", "2", "--encoding", "utf-8", "--types", "text,animation", "--psb-format", "preserve",
            "--dxt5-layout", "linear", "--ignore-pfs-videos", "false"), converter));
        ConversionOptions call = Assert.Single(converter.Calls);
        Assert.Equal(.75, call.Ratio);
        Assert.Equal(960, call.TargetWidth); Assert.Equal(540, call.TargetHeight);
        Assert.Equal(2, call.MaxParallelism);
        Assert.Equal(PfsNameEncoding.Utf8, call.NameEncoding);
        Assert.Equal(AssetCategories.Text | AssetCategories.Animation, call.Categories);
        Assert.False(call.ConvertEmotePsbTexturesToDxt5);
        Assert.False(call.IgnorePfsVideos);
        Assert.Equal(PsbDxt5Layout.Linear, call.Dxt5Layout);
        Assert.False(call.PsbTextures!.Enabled);
    }

    [Fact]
    public async Task AutoRatioRequiresResolutionButManualRatioDoesNot()
    {
        FakeScanner scanner = new() { Result = new([new("", "root.pfs", '8', 0)], false, null, null, []) };
        FakeConverter converter = new();
        Assert.Equal(1, await ConversionCommandLine.RunAsync(Args(), scanner: scanner, converter: converter, standardOutput: new StringWriter(), standardError: new StringWriter()));
        Assert.Empty(converter.Calls);
        Assert.Equal(0, await ConversionCommandLine.RunAsync(Args("--ratio", "0.5"), scanner: scanner, converter: converter, standardOutput: new StringWriter(), standardError: new StringWriter()));
        Assert.Null(Assert.Single(converter.Calls).TargetWidth);
    }

    [Theory]
    [InlineData(1280, 720, .75)]
    [InlineData(1920, 1080, .5)]
    [InlineData(2560, 1440, .375)]
    [InlineData(3840, 2160, .25)]
    [InlineData(1280, 1024, .52734375)]
    [InlineData(320, 180, 1d)]
    public async Task DefaultRatioMatchesUiAutoScanAcrossResolutions(int width, int height, double ratio)
    {
        var scanner = new FakeScanner { Result = new([new("", "root.pfs", '8', 0)], true, 640, 360,
            [new("root.pfs/system.ini", "VITA", 640, 360),
             new("root.pfs/system.ini", "windows", width, height),
             new("root.pfs.010/system.ini", "WINDOWS", 3840, 2160)]) };
        MainViewModel ui = new(scanner: scanner, settings: new DefaultSettingsStore()) { InputDirectory = Input };
        await ui.AutoScanResolutionAsync();
        var converter = new FakeConverter();
        Assert.Equal(0, await ConversionCommandLine.RunAsync(Args(), scanner: scanner, converter: converter,
            standardOutput: TextWriter.Null, standardError: TextWriter.Null));
        ConversionOptions call = Assert.Single(converter.Calls);
        Assert.Equal(1, ui.SelectedResolutionIndex);
        Assert.Equal(ratio, double.Parse(ui.Ratio, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(ratio, call.Ratio);
        Assert.Equal((int)(width * ratio), call.TargetWidth); Assert.Equal((int)(height * ratio), call.TargetHeight);
    }

    [Fact]
    public async Task DefaultSelectionDoesNotChooseWindowsFromALaterIni()
    {
        var scanner = new FakeScanner { Result = new([new("", "root.pfs", '8', 0)], true, 1024, 768,
            [new("root.pfs/system.ini", "DISPLAY", 1024, 768),
             new("root.pfs.010/system.ini", "WINDOWS", 1920, 1080)]) };
        var converter = new FakeConverter();
        Assert.Equal(0, await ConversionCommandLine.RunAsync(Args(), scanner: scanner, converter: converter,
            standardOutput: TextWriter.Null, standardError: TextWriter.Null));
        var call = Assert.Single(converter.Calls);
        Assert.Equal(.703125, call.Ratio); Assert.Equal(720, call.TargetWidth); Assert.Equal(540, call.TargetHeight);
    }

    [Fact]
    public async Task ExplicitRatioStillOverridesAutomaticScan()
    {
        var converter = new FakeConverter();
        Assert.Equal(0, await Run(Args("--source-resolution", "1280x720", "--ratio", "0.5"), converter));
        var call = Assert.Single(converter.Calls);
        Assert.Equal(.5, call.Ratio); Assert.Equal(640, call.TargetWidth); Assert.Equal(360, call.TargetHeight);
    }

    [Fact]
    public void FreshUiAndCommandLineShareNonResolutionDefaults()
    {
        MainViewModel ui = new(settings: new DefaultSettingsStore());
        var cli = ConversionCommandLineOptions.Parse(["--convert", Input, Output]);
        Assert.Null(cli.Ratio);
        Assert.Equal(ui.ProcessText, cli.Types.HasFlag(AssetCategories.Text));
        Assert.Equal(ui.ProcessImages, cli.Types.HasFlag(AssetCategories.Images));
        Assert.Equal(ui.ProcessAnimation, cli.Types.HasFlag(AssetCategories.Animation));
        Assert.Equal(ui.ProcessVideo, cli.Types.HasFlag(AssetCategories.Video));
        Assert.Equal(ui.SelectedParallel, cli.Parallelism);
        Assert.Equal((PfsNameEncoding)ui.SelectedEncoding, cli.Encoding);
        Assert.Equal(ui.NativeTextures, cli.NativeTextures);
        Assert.Equal(ui.IgnoreBackgroundAlpha, cli.IgnoreBackgroundAlpha);
        Assert.Equal(ui.IgnorePfsVideos, cli.IgnorePfsVideos);
        Assert.Equal(ui.ConvertEmotePsbTexturesToDxt5, cli.PsbFormat.HasValue);
        Assert.Equal((PsbTextureFormat)ui.SelectedPsbFormat, cli.PsbFormat);
        Assert.Equal((PsbDxt5Layout)ui.SelectedPsbDxt5Layout, cli.Layout);
        Assert.Equal(ui.SubsetFonts, cli.SubsetFonts);
        Assert.Equal((FontSubsetProfile)ui.FontProfile, cli.FontProfile);
        Assert.False(ui.IndependentPsbTextureScaling);
        Assert.False(ui.PsbRenderCompensation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DefaultsWithSimplifiedFontSubsettingMatchUiConversion(bool explicitPreset)
    {
        FakeScanner scanner = new() { Result = new([new("", "root.pfs", '8', 0)], true, 1280, 720,
            [new("system.ini", "WINDOWS", 1280, 720)]) };
        FakeConverter uiConverter = new(), cliConverter = new();
        MainViewModel ui = new(scanner: scanner, converter: uiConverter, settings: new DefaultSettingsStore())
        { InputDirectory = Input, OutputDirectory = Output, SubsetFonts = true, FontProfile = (int)FontSubsetProfile.SimplifiedChinese };
        await ui.AutoScanResolutionAsync();
        await ui.StartAsync();
        ConversionOptions expected = Assert.Single(uiConverter.Calls);
        string destination = Path.Combine(_root, "cli-result");
        string[] args = ["--convert", Input, destination, "--subset-fonts", "true", "--font-profile", "simplified",
            .. explicitPreset ? new[] { "--preset", "ui-defaults" } : Array.Empty<string>()];
        Assert.Equal(0, await ConversionCommandLine.RunAsync(args, scanner: scanner, converter: cliConverter,
            standardOutput: new StringWriter(), standardError: new StringWriter()));
        ConversionOptions actual = Assert.Single(cliConverter.Calls);
        Assert.Equal(expected with { OutputDirectory = destination, NativeTextures = null, PsbTextures = null },
            actual with { NativeTextures = null, PsbTextures = null });
        Assert.Equal(expected.NativeTextures!.Enabled, actual.NativeTextures!.Enabled);
        Assert.Equal(expected.NativeTextures.IgnoreBackgroundAlpha, actual.NativeTextures.IgnoreBackgroundAlpha);
        Assert.Equal(expected.NativeTextures.Rules, actual.NativeTextures.Rules);
        Assert.Equal(expected.PsbTextures, actual.PsbTextures);
        Assert.Equal(.75, actual.Ratio);
        Assert.Equal(960, actual.TargetWidth); Assert.Equal(540, actual.TargetHeight);
        Assert.True(actual.SubsetFonts);
        Assert.Equal(FontSubsetProfile.SimplifiedChinese, actual.FontProfile);
    }

    [Fact]
    public async Task DefaultPresetConvertsTexturesAndSubsetsFontsWithoutChangingSource()
    {
        await CreatePfs(Input, 1280, 720);
        string? font = await CreateDefaultTestAssets();
        byte[] originalPfs = await File.ReadAllBytesAsync(Path.Combine(Input, "root.pfs"));
        byte[] originalPng = await File.ReadAllBytesAsync(Path.Combine(Input, "image", "bg", "sample.png"));
        byte[]? originalFont = font is null ? null : await File.ReadAllBytesAsync(font);
        StringWriter error = new();
        Assert.Equal(0, await ConversionCommandLine.RunAsync(Args("--preset", "ui-defaults"), standardOutput: new StringWriter(), standardError: error));
        Assert.Equal("", error.ToString());
        await AssertDefaultOutput(font, originalFont, .75);
        Assert.Equal(originalPfs, await File.ReadAllBytesAsync(Path.Combine(Input, "root.pfs")));
        Assert.Equal(originalPng, await File.ReadAllBytesAsync(Path.Combine(Input, "image", "bg", "sample.png")));
        if (font != null) Assert.Equal(originalFont, await File.ReadAllBytesAsync(font));
    }

    [Fact]
    public async Task FailureIdentifiesArchiveAndEntryInConsoleAndLog()
    {
        FakeConverter converter = new() { Failure = new ConversionItemException("root.pfs.010", "script/sample.ast", new InvalidDataException("test failure")) };
        StringWriter error = new();
        Assert.Equal(1, await ConversionCommandLine.RunAsync(Args(), scanner: new FakeScanner(), converter: converter,
            standardOutput: new StringWriter(), standardError: error));
        Assert.Single(converter.Calls);
        Assert.Contains("archive=\"root.pfs.010\"; file=\"script/sample.ast\"", error.ToString());
        string logDirectory = Path.Combine(_root, "logs", "sample01");
        Assert.Contains("test failure", await File.ReadAllTextAsync(Directory.GetFiles(logDirectory, "*.errors.log").Single()));
        Assert.False(File.Exists(Output + ".conversion.lock"));
    }

    [Fact]
    public async Task CancellationReturns130WithoutPublishingOutput()
    {
        using CancellationTokenSource cancellation = new();
        FakeConverter converter = new() { Cancel = cancellation };
        Assert.Equal(130, await ConversionCommandLine.RunAsync(Args(), cancellation.Token,
            new FakeScanner(), converter, new StringWriter(), new StringWriter()));
        Assert.Single(converter.Calls);
        Assert.False(Directory.Exists(Output));
        Assert.False(File.Exists(Output + ".conversion.lock"));
    }

    [Fact]
    public async Task SetupRejectsOverlappingPathsAndUnsafeLogs()
    {
        Assert.Equal(2, await Run(["--convert", Input, Path.Combine(Input, "output")], new()));
        Assert.Equal(2, await Run(["--convert", Input, _root], new()));
        Assert.Equal(2, await Run(Args("--log", Path.Combine(Input, "log.txt")), new()));
        Assert.Equal(2, await Run(Args("--log", Path.Combine(Output, "log.txt")), new()));
        Assert.Equal(2, await Run(Args("--log", Output + ".conversion.lock"), new()));
        Assert.False(Directory.Exists(Output));
    }

    [Fact]
    public async Task ParentDirectoryIsNotTreatedAsABatch()
    {
        string child = Path.Combine(Input, "sample02");
        Directory.CreateDirectory(child);
        await CreatePfs(child);
        StringWriter error = new();
        Assert.Equal(1, await ConversionCommandLine.RunAsync(Args(), standardOutput: new StringWriter(), standardError: error));
        Assert.Contains("single game root", error.ToString());
        Assert.False(Directory.Exists(Output));
    }

    [Fact]
    public async Task RealConversionResizesPngRepacksPfsAndKeepsInputUnchanged()
    {
        await CreatePfs(Input);
        string archive = Path.Combine(Input, "root.pfs");
        byte[] original = await File.ReadAllBytesAsync(archive);
        using (Image<Rgba32> png = new(16, 8)) await png.SaveAsPngAsync(Path.Combine(Input, "sample.png"));
        StringWriter error = new();
        Assert.Equal(0, await ConversionCommandLine.RunAsync(Args(), standardOutput: new StringWriter(), standardError: error));
        Assert.Equal("", error.ToString());
        Assert.Equal(original, await File.ReadAllBytesAsync(archive));
        using Image result = await Image.LoadAsync(Path.Combine(Output, "sample.png"));
        Assert.Equal(8, result.Width); Assert.Equal(4, result.Height);
        string unpack = Path.Combine(_root, "unpacked");
        await new PfsCodec().ExtractAsync(Path.Combine(Output, "root.pfs"), unpack);
        string ini = await File.ReadAllTextAsync(Path.Combine(unpack, "system.ini"));
        Assert.Contains("960", ini); Assert.Contains("540", ini); Assert.Contains("[VITA]", ini);
        Assert.False(Directory.Exists(Path.Combine(Output, "sample01")));
    }

    [Fact]
    public async Task PublishedExecutableSingleGameSmokeTestWhenConfigured()
    {
        string? executable = Environment.GetEnvironmentVariable("ART3M1S_BATCH_EXE");
        if (string.IsNullOrWhiteSpace(executable)) return;
        Assert.True(File.Exists(executable));
        var help = await Launch("--help");
        Assert.Equal(0, help.Code); Assert.Contains("--convert", help.Output);
        Assert.Contains("ONE game", help.Output);
        Assert.Equal(2, (await Launch("--convert", "--ratio", "0")).Code);
        Assert.Equal(2, (await Launch("--unknown")).Code);
        await CreatePfs(Input, 1280, 720);
        string? font = await CreateDefaultTestAssets();
        byte[]? originalFont = font is null ? null : await File.ReadAllBytesAsync(font);
        byte[] original = await File.ReadAllBytesAsync(Path.Combine(Input, "root.pfs"));
        var preview = await Launch(Args("--dry-run"));
        Assert.Equal(0, preview.Code); Assert.Contains("ratio=0.75; target=960x540", preview.Output);
        Assert.False(Directory.Exists(Output));
        string lockPath = Output + ".conversion.lock";
        using (var owner = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Equal(2, (await Launch(Args())).Code);
            Assert.True(File.Exists(lockPath));
            Assert.False(Directory.Exists(Output));
        }
        var converted = await Launch(Args());
        Assert.True(converted.Code == 0, converted.Error);
        Assert.Contains("DONE output=", converted.Output);
        int scanFinished = converted.Output.IndexOf("TEXTURE_SCAN_COMPLETE", StringComparison.Ordinal);
        Assert.True(scanFinished >= 0);
        Assert.True(scanFinished < converted.Output.IndexOf("CONVERT_START", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(Output, "root.pfs")));
        Assert.False(Directory.Exists(Path.Combine(Output, "sample01")));
        Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(Input, "root.pfs")));
        await AssertDefaultOutput(font, originalFont, .75);
        Assert.False(File.Exists(lockPath));
        string successfulErrors = Path.Combine(_root, "logs", "sample01", "sample01.errors.log");
        Assert.True(File.Exists(successfulErrors));
        string successfulDetails = await File.ReadAllTextAsync(successfulErrors);
        Assert.Contains("code=RUN_START", successfulDetails); Assert.Contains("code=RUN_END", successfulDetails);
        Assert.Contains("outcome=success; exit-code=0", successfulDetails);
        Assert.Contains("warning-events=0; error-events=0", successfulDetails);
        Assert.DoesNotContain("code=PROGRESS", successfulDetails);

        // Independent invocations can choose unrelated paths and different ratios.
        string secondInput = Path.Combine(_root, "other-source", "sample02");
        string secondOutput = Path.Combine(_root, "other-output", "result");
        Directory.CreateDirectory(secondInput);
        await CreatePfs(secondInput);
        var second = await Launch("--batch-convert", secondInput, secondOutput, "--ratio", "0.75",
            "--subset-fonts", "true", "--font-profile", "simplified");
        Assert.True(second.Code == 0, second.Error);
        Assert.Contains("ratio=0.75; target=1440x810", second.Output);
        Assert.True(File.Exists(Path.Combine(secondOutput, "root.pfs")));
        Assert.False(Directory.Exists(Path.Combine(secondOutput, "sample02")));
        Assert.False(File.Exists(secondOutput + ".conversion.lock"));

        string failedInput = Path.Combine(_root, "sample03"), failedOutput = Path.Combine(_root, "failed-result");
        Directory.CreateDirectory(failedInput);
        await CreatePfs(failedInput);
        await File.WriteAllBytesAsync(Path.Combine(failedInput, "sample01.png"), [1, 2]);
        await File.WriteAllBytesAsync(Path.Combine(failedInput, "sample02.png"), [3, 4]);
        var failed = await Launch("--convert", failedInput, failedOutput, "--subset-fonts", "true", "--font-profile", "simplified");
        Assert.Equal(1, failed.Code);
        Assert.DoesNotContain("CONVERT_START", failed.Output);
        string errors = Directory.GetFiles(Path.Combine(_root, "logs", "sample03"), "*.errors.log").Single();
        string details = await File.ReadAllTextAsync(errors);
        Assert.Contains("IMAGE_SCAN_FAILED", details);
        Assert.Contains("sample01.png", details); Assert.Contains("sample02.png", details);
        Assert.Contains("stage=\"texture-scan\"", details);
        Assert.False(Directory.Exists(failedOutput));
        Assert.False(File.Exists(failedOutput + ".conversion.lock"));

        async Task<(int Code, string Output, string Error)> Launch(params string[] arguments)
        {
            ProcessStartInfo info = new(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in arguments) info.ArgumentList.Add(argument);
            using Process process = Process.Start(info)!;
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(60));
            try { await process.WaitForExitAsync(timeout.Token); }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            return (process.ExitCode, await stdout, await stderr);
        }
    }

    private async Task<string?> CreateDefaultTestAssets()
    {
        string background = Path.Combine(Input, "image", "bg");
        Directory.CreateDirectory(background);
        using (Image<Rgb24> png = new(64, 32, new Rgb24(160, 80, 40)))
            await png.SaveAsPngAsync(Path.Combine(background, "sample.png"));
        string? source = new[] { @"C:\Windows\Fonts\arial.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/System/Library/Fonts/Supplemental/Arial.ttf" }.FirstOrDefault(File.Exists);
        if (source is null) return null;
        string font = Path.Combine(Input, "sample.ttf");
        File.Copy(source, font);
        return font;
    }

    private async Task AssertDefaultOutput(string? font, byte[]? originalFont, double ratio = .5)
    {
        byte[] texture = await File.ReadAllBytesAsync(Path.Combine(Output, "image", "bg", "sample.dds"));
        Assert.Equal("DXT1", Encoding.ASCII.GetString(texture, 84, 4));
        Assert.Equal("GXMSW", Encoding.ASCII.GetString(texture, 32, 5));
        Assert.Equal((int)(64 * ratio), BitConverter.ToInt32(texture, 16));
        Assert.Equal((int)(32 * ratio), BitConverter.ToInt32(texture, 12));
        if (font != null)
        {
            byte[] subset = await File.ReadAllBytesAsync(Path.Combine(Output, "sample.ttf"));
            Assert.True(subset.Length < originalFont!.Length);
            byte[] expected = await SubsetExpected(Path.Combine(_root, "expected.ttf"));
            Assert.Equal(expected, subset);
        }

        async Task<byte[]> SubsetExpected(string path)
        {
            await File.WriteAllBytesAsync(path, originalFont!);
            await new FontSubsetProcessor().SubsetAsync(path, FontSubsetProfile.SimplifiedChinese, new HashSet<int>());
            return await File.ReadAllBytesAsync(path);
        }
    }

    private async Task CreatePfs(string project, int width = 1920, int height = 1080)
    {
        string payload = Path.Combine(_root, "system.ini");
        await File.WriteAllTextAsync(payload, $"[WINDOWS]\nWIDTH={width}\nHEIGHT={height}\nCHARSET=UTF-8\n");
        await new PfsCodec().PackPf8Async(new ExtractedArchive('8', [new PfsEntry(Encoding.UTF8.GetBytes("system.ini"), "system.ini", 0, (uint)new FileInfo(payload).Length, payload)]), Path.Combine(project, "root.pfs"));
    }
    private Task<int> Run(string[] args, FakeConverter converter) => ConversionCommandLine.RunAsync(args,
        scanner: new FakeScanner(), converter: converter, standardOutput: new StringWriter(), standardError: new StringWriter(), fallbackLogDirectory: Path.Combine(_root, "fallback"));

    private sealed class DefaultSettingsStore : ISettingsStore
    {
        public AppSettings Load() => new();
        public void Save(AppSettings settings) { }
    }

    private sealed class FakeScanner : IProjectScanner
    {
        public ScanResult Result { get; set; } = new([new("", "root.pfs", '8', 0)], true, 1280, 720,
            [new("system.ini", "DEFAULT", 1280, 720), new("system.ini", "WINDOWS", 1920, 1080), new("other.ini", "WINDOWS", 3840, 2160)]);
        public Task<ScanResult> ScanAsync(string inputDirectory, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result with { Archives = Result.Archives.Select(x => x with { Path = Path.Combine(inputDirectory, x.FileName) }).ToArray() });
    }
    private sealed class FakeConverter : IConversionService
    {
        public List<ConversionOptions> Calls { get; } = [];
        public Exception? Failure { get; init; }
        public CancellationTokenSource? Cancel { get; init; }
        public Task ConvertAsync(ConversionOptions options, IProgress<ConversionProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Calls.Add(options);
            if (Failure != null) throw Failure;
            Cancel?.Cancel(); cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(options.OutputDirectory);
            return Task.CompletedTask;
        }
    }
}
