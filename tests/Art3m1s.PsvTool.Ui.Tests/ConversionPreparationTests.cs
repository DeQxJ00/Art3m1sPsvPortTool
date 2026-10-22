using Art3m1s.PsvTool.App;
using Art3m1s.PsvTool.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Art3m1s.PsvTool.Ui.Tests;

public sealed class ConversionPreparationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "art3m1s-prepare-" + Guid.NewGuid().ToString("N"));
    private string Input => Path.Combine(_root, "sample01");
    private string Output => Path.Combine(_root, "result");
    public ConversionPreparationTests() { Directory.CreateDirectory(Input); }
    public void Dispose() { Directory.Delete(_root, true); }

    [Fact]
    public async Task WaitsForScanAndRecommendationsBeforeStartingConversion()
    {
        var scan = new DelayedScanner();
        var converter = new Converter();
        using var console = new StringWriter();
        Task<int> run = Run(scan, converter, console: console);
        try
        {
            await scan.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Null(converter.Options);
            Assert.False(run.IsCompleted);
            Assert.Contains("TEXTURE_SCAN_START", console.ToString());
            Assert.DoesNotContain("CONVERT_START", console.ToString());
            scan.Release.SetResult();
            Assert.Equal(0, await run.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally { scan.Release.TrySetResult(); await run; }
        Assert.True(scan.Completed);
        ConversionOptions options = Assert.IsType<ConversionOptions>(converter.Options);
        Assert.Equal(.75, options.Ratio);
        Assert.Equal(960, options.TargetWidth); Assert.Equal(540, options.TargetHeight);
        var expected = TextureCategoryViewModel.FromScan(scan.Images, .75, false, false).Select(category => category.Rule).ToArray();
        Assert.Equal(expected, options.NativeTextures!.Rules);
        string text = console.ToString();
        Assert.Contains("TEXTURE_CATEGORY", text);
        Assert.True(text.IndexOf("TEXTURE_SCAN_COMPLETE", StringComparison.Ordinal) < text.IndexOf("CONVERT_START", StringComparison.Ordinal));
        Assert.False(File.Exists(Output + ".conversion.lock"));
    }

    [Theory]
    [InlineData("cancel", 130)]
    [InlineData("throw", 1)]
    [InlineData("bad-image", 1)]
    public async Task CancelledOrFailedScanNeverStartsConversion(string failure, int expected)
    {
        var scan = new DelayedScanner { Failure = failure };
        var converter = new Converter();
        using var cancellation = new CancellationTokenSource();
        using var console = new StringWriter();
        Task<int> run = Run(scan, converter, cancellation.Token, console);
        try
        {
            await scan.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (failure == "cancel") cancellation.Cancel();
            else scan.Release.SetResult();
            Assert.Equal(expected, await run.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally { scan.Release.TrySetResult(); await run; }
        Assert.Null(converter.Options);
        Assert.DoesNotContain("CONVERT_START", console.ToString());
        Assert.False(Directory.Exists(Output));
        Assert.False(File.Exists(Output + ".conversion.lock"));
        if (failure == "bad-image")
        {
            string errors = Directory.GetFiles(Path.Combine(_root, "logs"), "*.errors.log", SearchOption.AllDirectories).Single();
            Assert.Contains("broken.png", await File.ReadAllTextAsync(errors));
        }
    }

    [Fact]
    public async Task RealImageScanSelectsSameCategoriesAsUiButton()
    {
        foreach (string category in new[] { "image/bg", "image/fg", "image/ui", "image/mask" })
        {
            string path = Path.Combine(Input, category, "sample.png");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (category == "image/mask")
            {
                using var image = new Image<L8>(16, 16, new L8(128));
                await image.SaveAsPngAsync(path);
            }
            else
            {
                using var image = new Image<Rgb24>(16, 16, new Rgb24(180, 90, 40));
                await image.SaveAsPngAsync(path);
            }
        }
        MainViewModel ui = new(scanner: new Scanner(), settings: new Settings())
        { InputDirectory = Input, OutputDirectory = Output };
        await ui.AutoScanResolutionAsync();
        await ui.ScanTexturesAsync();
        var converter = new Converter();
        Assert.Equal(0, await Run(new TextureScanner(), converter));
        Assert.Equal(ui.TextureCategories.Select(category => category.Rule).ToArray(), converter.Options!.NativeTextures!.Rules);
        Assert.Contains(converter.Options.NativeTextures.Rules!, rule => rule.Category == "image/bg" && rule.Enabled);
        Assert.Contains(converter.Options.NativeTextures.Rules!, rule => rule.Category == "image/ui" && !rule.Enabled);
        Assert.Contains(converter.Options.NativeTextures.Rules!, rule => rule.Category.EndsWith("#gray", StringComparison.Ordinal) && !rule.Enabled);
    }

    private Task<int> Run(ITextureScanner scanner, IConversionService converter, CancellationToken token = default,
        TextWriter? console = null) => ConversionCommandLine.RunAsync(
            ["--convert", Input, Output, "--subset-fonts", "true", "--font-profile", "simplified"], token,
            scanner: new Scanner(), converter: converter, standardOutput: console ?? TextWriter.Null,
            standardError: TextWriter.Null, textureScanner: scanner);

    private sealed class DelayedScanner : ITextureScanner
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Completed { get; private set; }
        public string? Failure { get; init; }
        public IReadOnlyList<TextureImageInfo> Images { get; } =
            [new(null, "image/bg/sample.png", "image/bg", 1280, 720, 100, false, false, false, false)];
        public async Task<IReadOnlyList<TextureImageInfo>> ScanAsync(string input, PfsNameEncoding encoding = PfsNameEncoding.Auto,
            IProgress<ConversionProgress>? progress = null, CancellationToken token = default)
        {
            Started.SetResult();
            await Release.Task.WaitAsync(token);
            if (Failure == "throw") throw new InvalidDataException("Image scan failed.");
            Completed = true;
            return Failure == "bad-image" ? [Images[0] with { Path = "image/bg/broken.png", Error = "Broken image." }] : Images;
        }
    }

    private sealed class Converter : IConversionService
    {
        public ConversionOptions? Options { get; private set; }
        public Task ConvertAsync(ConversionOptions options, IProgress<ConversionProgress>? progress = null,
            CancellationToken cancellationToken = default) { Options = options; return Task.CompletedTask; }
    }

    private sealed class Scanner : IProjectScanner
    {
        public Task<ScanResult> ScanAsync(string inputDirectory, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ScanResult([new(Path.Combine(inputDirectory, "root.pfs"), "root.pfs", '8', 0)],
                true, 1280, 720, [new("system.ini", "WINDOWS", 1280, 720)]));
    }

    private sealed class Settings : ISettingsStore
    {
        public AppSettings Load() => new();
        public void Save(AppSettings settings) { }
    }
}
