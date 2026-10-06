using Art3m1s.PsvTool.App;
using Art3m1s.PsvTool.Core;
using Xunit;

namespace Art3m1s.PsvTool.Ui.Tests;

public sealed class PsbUiTests
{
    [Fact]
    public void IndependentScalingResetsOnRestartWhileCompensationPersists()
    {
        var store = new MemoryStore(new());
        var vm = new MainViewModel(settings: store);
        Assert.False(vm.IndependentPsbTextureScaling); Assert.True(vm.PsbRenderCompensation);
        vm.IndependentPsbTextureScaling = true; vm.PsbRenderCompensation = false;
        Assert.True(vm.IndependentPsbTextureScaling); Assert.False(store.Value.PsbRenderCompensation);
        var loaded = new MainViewModel(settings: store);
        Assert.False(loaded.IndependentPsbTextureScaling); Assert.False(loaded.PsbRenderCompensation);
        loaded.IndependentPsbTextureScaling = true;
        loaded.SetLanguage("en-US");
        Assert.False(new MainViewModel(settings: store).IndependentPsbTextureScaling);
        Assert.Contains("independently", loaded.IndependentPsbTextureScalingLabel);
        Assert.Contains("global Ratio ÷ texture Ratio", loaded.PsbRenderCompensationLabel);
        Assert.Contains("compatible GXM", loaded.PsbRenderCompensationHelp);
    }

    [Fact]
    public async Task LegacyEnabledSettingIsIgnoredWithoutLosingRatiosOrOtherPreferences()
    {
        using var folder = new ScanFolder();
        string path = Path.Combine(folder.Path, "settings.json");
        await File.WriteAllTextAsync(path, """
            {"Language":"en-US","IsDark":false,"IndependentPsbTextureScaling":true,
             "PsbRenderCompensation":false,"PsbRatios":[{"Width":4096,"Height":2048,"Ratio":0.25}]}
            """);
        var store = new LocalSettingsStore(path);
        var vm = new MainViewModel(settings: store,
            psbScanner: new PsbScannerStub { Files = [AtlasFile(4096, 2048)] })
        { InputDirectory = folder.Path };
        Assert.False(vm.IndependentPsbTextureScaling); Assert.False(vm.PsbRenderCompensation);
        Assert.Equal("en-US", vm.Language); Assert.False(vm.IsDark);
        await vm.ScanPsbAsync();
        Assert.Equal("0.25", Assert.Single(vm.PsbCategories).Ratio);
        vm.IndependentPsbTextureScaling = true;
        vm.ToggleTheme(); // Saving another preference must not persist the per-run opt-in.
        Assert.DoesNotContain("IndependentPsbTextureScaling", await File.ReadAllTextAsync(path));
        var reopened = new MainViewModel(settings: store);
        Assert.False(reopened.IndependentPsbTextureScaling); Assert.False(reopened.PsbRenderCompensation);
        Assert.Equal(.25, Assert.Single(store.Load().PsbRatios!).Ratio);
    }

    [Fact]
    public async Task CompensationPreviewTracksBothRatiosAndEnableFlags()
    {
        using var folder = new ScanFolder();
        var vm = new MainViewModel(settings: new MemoryStore(new()), psbScanner: new PsbScannerStub { Files = [AtlasFile(4096, 2048)] })
        { InputDirectory = folder.Path, Ratio = "0.5" };
        await vm.ScanPsbAsync();
        var row = Assert.Single(vm.PsbCategories);
        Assert.Contains("未启用", row.RenderCompensation);
        vm.IndependentPsbTextureScaling = true; row.Ratio = "0.25";
        Assert.Contains("2×", row.RenderCompensation);
        vm.Ratio = "0.75"; Assert.Contains("3×", row.RenderCompensation);
        vm.PsbRenderCompensation = false; Assert.Contains("未启用", row.RenderCompensation);
        vm.PsbRenderCompensation = true; vm.ProcessAnimation = false; Assert.Contains("未启用", row.RenderCompensation);
        vm.ProcessAnimation = true; vm.SetLanguage("en-US"); Assert.Contains("Render compensation 3×", row.RenderCompensation);
    }

    [Fact]
    public async Task RatiosStayHiddenUntilScanAndRestoreOnlyDiscoveredDimensions()
    {
        using var folder = new ScanFolder();
        var scanner = new PsbScannerStub { Files = [AtlasFile(4096, 2048), AtlasFile(2048, 4096), AtlasFile(256, 128)] };
        var store = new MemoryStore(new(PsbRatios: [new(4096, 2048, .25), new(2048, 4096, .75), new(64, 64, .375)]));
        var vm = new MainViewModel(settings: store, psbScanner: scanner) { InputDirectory = folder.Path };
        vm.Ratio = "0.75";
        Assert.Empty(vm.PsbCategories); Assert.False(vm.HasPsbCategories);
        vm.ToggleTheme();
        Assert.Equal(3, store.Value.PsbRatios!.Length);
        await vm.ScanPsbAsync();
        Assert.Equal(3, vm.PsbCategories.Count); Assert.True(vm.HasPsbCategories);
        Assert.All(vm.PsbCategories, x => Assert.True(x.HasFiles));
        Assert.Equal("0.5", Assert.Single(vm.PsbCategories, x => x.Width == 256 && x.Height == 128).Ratio);
        Assert.DoesNotContain(vm.PsbCategories, x => x.Width == 64 && x.Height == 64);
        var wide = Assert.Single(vm.PsbCategories, x => x.Width == 4096 && x.Height == 2048);
        var tall = Assert.Single(vm.PsbCategories, x => x.Width == 2048 && x.Height == 4096);
        Assert.Equal("→ 1024 × 512", wide.Preview);
        Assert.Equal("→ 1536 × 3072", tall.Preview);
        wide.SelectedPreset = 2;
        Assert.Equal("0.5", wide.Ratio);
        Assert.Equal(.5, Assert.Single(store.Value.PsbRatios!, x => x.Width == 4096 && x.Height == 2048).Ratio);
        var loaded = new MainViewModel(settings: store, psbScanner: scanner) { InputDirectory = folder.Path };
        Assert.Empty(loaded.PsbCategories);
        await loaded.ScanPsbAsync();
        Assert.Equal("0.5", Assert.Single(loaded.PsbCategories, x => x.Width == 4096 && x.Height == 2048).Ratio);
        Assert.Equal(.375, Assert.Single(store.Value.PsbRatios!, x => x.Width == 64 && x.Height == 64).Ratio);
        vm.SetLanguage("en-US");
        Assert.Equal("Scan PSB textures", vm.ScanPsbLabel);
        Assert.Contains("independent", vm.PsbTitle);
        Assert.Contains("discovered", vm.PsbEmptyHelp);
    }

    [Fact]
    public async Task RescanAndInputChangeRemoveOldRowsWithoutDiscardingSavedRatios()
    {
        using var folder = new ScanFolder();
        var scanner = new PsbScannerStub { Files = [AtlasFile(4096, 2048)] };
        var store = new MemoryStore(new());
        var vm = new MainViewModel(settings: store, psbScanner: scanner) { InputDirectory = folder.Path };
        await vm.ScanPsbAsync();
        Assert.Single(vm.PsbCategories).Ratio = "0.375";
        scanner.Files = [AtlasFile(64, 32), new(null, "broken.psb", 3, null, "Truncated PSB header.")];
        await vm.ScanPsbAsync();
        Assert.Equal("64 × 32", Assert.Single(vm.PsbCategories).Label);
        Assert.Contains("broken.psb", vm.Log);
        Assert.Equal(.375, Assert.Single(store.Value.PsbRatios!, x => x.Width == 4096).Ratio);
        vm.InputDirectory = folder.Path + "-other";
        Assert.Empty(vm.PsbCategories); Assert.False(vm.HasPsbCategories);
        vm.InputDirectory = folder.Path;
        scanner.Files = [AtlasFile(4096, 2048)];
        await vm.ScanPsbAsync();
        Assert.Equal("0.375", Assert.Single(vm.PsbCategories).Ratio);
        scanner.Files = [];
        await vm.ScanPsbAsync();
        Assert.Empty(vm.PsbCategories); Assert.False(vm.HasPsbCategories);
        Assert.Equal(.375, Assert.Single(store.Value.PsbRatios!, x => x.Width == 4096).Ratio);
    }

    [Fact]
    public void AllAtlasDimensionsAreVisibleAndInvalidRatioHasLocalizedFeedback()
    {
        Localizer localizer = new();
        var row = new PsbCategoryViewModel(4096, 2048, .5, localizer);
        row.SetFiles([new("root.pfs.010", "image/hero.psb", 100,
            new(true, [new("atlas0", 4096, 2048, "RGBA8"), new("atlas1", 512, 256, "DXT5")]))]);
        Assert.Equal("4096 × 2048", row.Label);
        Assert.Equal("→ 2048 × 1024", row.Preview);
        Assert.Contains("512 × 256", Assert.Single(row.FileRows));
        Assert.Contains("1 个 PSB · 2 张贴图", row.Count);
        row.Ratio = "NaN";
        Assert.False(row.TryRatio(out _)); Assert.Contains("大于 0", row.Validation);
        localizer.SetLanguage("en-US"); row.Refresh();
        Assert.Contains("greater than 0", row.Validation);
    }

    [Fact]
    public void MemoryComparisonTracksRatioAndLanguageAndExplainsAlignment()
    {
        Localizer localizer = new();
        var row = new PsbCategoryViewModel(4096, 2048, .5, localizer);
        Assert.Contains("RGBA8 8 MiB / DXT5 2 MiB", row.MemoryComparison);
        List<string?> changes = [];
        row.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        row.Ratio = "0.75";
        Assert.Contains(nameof(row.MemoryComparison), changes);
        Assert.Contains("RGBA8 18 MiB / DXT5 8 MiB", row.MemoryComparison);
        Assert.Contains("DXT5 数据 4.5 MiB", row.MemoryComparison);
        localizer.SetLanguage("en-US"); row.Refresh();
        Assert.Contains("allocation estimate", row.MemoryComparison);
        Assert.Contains("DXT5 data 4.5 MiB", row.MemoryComparison);
        row.Ratio = "NaN";
        Assert.Equal("—", row.MemoryComparison);
        Assert.Contains("256 KiB", localizer["PsbDxt5Help"]);
        Assert.Contains("lossily", localizer["PsbDxt5Help"]);
    }

    [Fact]
    public async Task StartPassesIndependentPsbRulesAndBlocksInvalidRatioBeforeConversion()
    {
        string root = Path.Combine(Path.GetTempPath(), "psb-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var converter = new RecordingConverter();
            var vm = new MainViewModel(settings: new MemoryStore(new()), converter: converter,
                psbScanner: new PsbScannerStub { Files = [AtlasFile(4096, 4096)] })
            { InputDirectory = root, OutputDirectory = root + "-output", Ratio = "0.75", IndependentPsbTextureScaling = true };
            await vm.ScanPsbAsync();
            var row = Assert.Single(vm.PsbCategories, x => x.Width == 4096 && x.Height == 4096);
            row.Ratio = "0"; await vm.StartAsync();
            Assert.Null(converter.Options); Assert.Contains("PSB Ratio", vm.Status);
            row.Ratio = "0.25"; await vm.StartAsync();
            Assert.NotNull(converter.Options);
            Assert.Equal(.75, converter.Options.Ratio);
            Assert.Equal(.25, converter.Options.PsbTextures!.RatioFor(4096, 4096));
            Assert.Equal(.5, converter.Options.PsbTextures.RatioFor(4096, 2048));
            Assert.True(converter.Options.PsbTextures.Enabled); Assert.True(converter.Options.PsbTextures.CompensateRendering);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CustomRatiosRoundTripThroughAotSafeJsonWithoutLosingPrecision()
    {
        using var folder = new ScanFolder();
        string path = Path.Combine(Path.GetTempPath(), "psb-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new LocalSettingsStore(path);
            store.Save(new(PsbRatios: [new(4096, 2048, .333333333)]));
            var vm = new MainViewModel(settings: store,
                psbScanner: new PsbScannerStub { Files = [AtlasFile(4096, 2048)] })
            { InputDirectory = folder.Path };
            Assert.Empty(vm.PsbCategories);
            vm.ToggleTheme();
            Assert.Equal(.333333333, Assert.Single(store.Load().PsbRatios!).Ratio);
            await vm.ScanPsbAsync();
            var row = Assert.Single(vm.PsbCategories, x => x.Width == 4096 && x.Height == 2048);
            Assert.True(row.TryRatio(out double ratio)); Assert.Equal(.333333333, ratio);
            vm.ToggleTheme();
            Assert.Equal(.333333333, Assert.Single(store.Load().PsbRatios!, x => x.Width == 4096 && x.Height == 2048).Ratio);
        }
        finally { File.Delete(path); }
    }

    private static PsbTextureFileInfo AtlasFile(int width, int height) => new(null, $"{width}x{height}.psb", 100,
        new(true, [new("atlas0", width, height, "RGBA8")]));

    private sealed class ScanFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "psb-scan-ui-" + Guid.NewGuid().ToString("N"));
        public ScanFolder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }

    private sealed class PsbScannerStub : IPsbTextureScanner
    {
        public IReadOnlyList<PsbTextureFileInfo> Files { get; set; } = [];
        public Task<IReadOnlyList<PsbTextureFileInfo>> ScanAsync(string input, PfsNameEncoding encoding = PfsNameEncoding.Auto,
            IProgress<ConversionProgress>? progress = null, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(Files);
        }
    }

    private sealed class MemoryStore(AppSettings initial) : ISettingsStore
    {
        public AppSettings Value { get; private set; } = initial;
        public AppSettings Load() => Value;
        public void Save(AppSettings settings) => Value = settings;
    }
    private sealed class RecordingConverter : IConversionService
    {
        public ConversionOptions? Options { get; private set; }
        public Task ConvertAsync(ConversionOptions options, IProgress<ConversionProgress>? progress = null, CancellationToken cancellationToken = default)
        { Options = options; return Task.CompletedTask; }
    }
}
