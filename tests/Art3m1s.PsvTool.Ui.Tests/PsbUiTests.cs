using Art3m1s.PsvTool.App;
using Art3m1s.PsvTool.Core;
using Xunit;

namespace Art3m1s.PsvTool.Ui.Tests;

public sealed class PsbUiTests
{
    [Fact]
    public async Task Dxt5LayoutDefaultsToSwizzledPersistsAndReachesConverter()
    {
        using var folder = new ScanFolder();
        var store = new LocalSettingsStore(Path.Combine(folder.Path, "settings.json"));
        var converter = new RecordingConverter();
        var vm = new MainViewModel(settings: store, converter: converter) { InputDirectory = folder.Path, OutputDirectory = folder.Path + "-out" };
        Assert.Equal(0, vm.SelectedPsbDxt5Layout); Assert.True(vm.CanSelectPsbDxt5Layout);
        Assert.Contains("默认", vm.PsbDxt5LayoutChoices[0].Label);
        foreach (int index in new[] { 0, 1, 0 })
        {
            vm.SelectedPsbDxt5Layout = index; await vm.StartAsync();
            Assert.Equal((PsbDxt5Layout)index, converter.Options!.Dxt5Layout);
            Assert.Equal(index, new MainViewModel(settings: store).SelectedPsbDxt5Layout);
        }
        vm.SelectedPsbDxt5Layout = 1;
        vm.SelectedPsbFormat = 1; Assert.False(vm.CanSelectPsbDxt5Layout);
        vm.SelectedPsbFormat = 2; Assert.False(vm.CanSelectPsbDxt5Layout);
        vm.SelectedPsbFormat = 0; Assert.True(vm.CanSelectPsbDxt5Layout);
        Assert.Equal(1, vm.SelectedPsbDxt5Layout);
        vm.ConvertEmotePsbTexturesToDxt5 = false; Assert.False(vm.CanSelectPsbDxt5Layout);
        vm.ConvertEmotePsbTexturesToDxt5 = true; Assert.True(vm.CanSelectPsbDxt5Layout);
        vm.SelectedPsbDxt5Layout = -1; vm.SelectedPsbDxt5Layout = 2;
        Assert.Equal(1, vm.SelectedPsbDxt5Layout);
        vm.SetLanguage("en-US");
        Assert.Contains("default", vm.PsbDxt5LayoutChoices[0].Label);
        Assert.Contains("DXT5_SWIZZLED", vm.PsbDxt5LayoutHelp);
        Assert.Equal(1, vm.SelectedPsbDxt5Layout);
        store.Save(new(Dxt5Layout: (PsbDxt5Layout)999));
        Assert.Equal(0, new MainViewModel(settings: store).SelectedPsbDxt5Layout);
    }

    [Fact]
    public async Task LegacySettingsWithoutLayoutUseSwizzledByDefault()
    {
        using var folder = new ScanFolder();
        string path = Path.Combine(folder.Path, "settings.json");
        await File.WriteAllTextAsync(path, """{"PsbOutputFormat":0,"Language":"zh-CN"}""");
        Assert.Equal(PsbDxt5Layout.Swizzled, new LocalSettingsStore(path).Load().Dxt5Layout);
        Assert.Equal(0, new MainViewModel(settings: new LocalSettingsStore(path)).SelectedPsbDxt5Layout);
    }
    [Fact]
    public async Task PsbFormatDefaultsToBc3PersistsAndReachesConverter()
    {
        using var folder = new ScanFolder();
        var store = new LocalSettingsStore(Path.Combine(folder.Path, "settings.json"));
        var converter = new RecordingConverter();
        var vm = new MainViewModel(settings: store, converter: converter) { InputDirectory = folder.Path, OutputDirectory = folder.Path + "-output" };
        Assert.Equal(0, vm.SelectedPsbFormat);
        Assert.Equal(new[] { "DXT5 (BC3)", "PVRTC2 4bpp", "PVRTC2 2bpp" }, vm.PsbFormatChoices);
        foreach (int index in new[] { 1, 2, 0 })
        {
            vm.SelectedPsbFormat = index;
            Assert.Equal(index, new MainViewModel(settings: store).SelectedPsbFormat);
            await vm.StartAsync();
            Assert.Equal((PsbTextureFormat)index, converter.Options!.PsbOutputFormat);
            Assert.False(converter.Options.PsbTextures!.Enabled);
        }
        vm.SelectedPsbFormat = -1; vm.SelectedPsbFormat = 3;
        Assert.Equal(0, vm.SelectedPsbFormat);
        vm.SetLanguage("en-US");
        Assert.Contains("default DXT5", vm.PsbFormatLabel);
        Assert.Contains("does not support PVRTC2", vm.ConvertEmotePsbTexturesToDxt5Help);
        vm.ConvertEmotePsbTexturesToDxt5 = false; await vm.StartAsync();
        Assert.False(converter.Options!.ConvertEmotePsbTexturesToDxt5);
        store.Save(new(PsbOutputFormat: (PsbTextureFormat)999));
        Assert.Equal(0, new MainViewModel(settings: store).SelectedPsbFormat);
    }
    [Fact]
    public void IndependentScalingCannotBeEnabledWhileCompensationPreferencePersists()
    {
        var store = new MemoryStore(new());
        var vm = new MainViewModel(settings: store);
        Assert.False(vm.IndependentPsbTextureScaling); Assert.False(vm.PsbRenderCompensation);
        vm.IndependentPsbTextureScaling = true; vm.PsbRenderCompensation = false;
        Assert.False(vm.CanUseIndependentPsbTextureScaling); Assert.False(vm.IndependentPsbTextureScaling);
        Assert.False(store.Value.PsbRenderCompensation);
        var loaded = new MainViewModel(settings: store);
        Assert.False(loaded.IndependentPsbTextureScaling); Assert.False(loaded.PsbRenderCompensation);
        loaded.IndependentPsbTextureScaling = true;
        loaded.SetLanguage("en-US");
        Assert.False(new MainViewModel(settings: store).IndependentPsbTextureScaling);
        Assert.Contains("independently", loaded.IndependentPsbTextureScalingLabel);
        Assert.Contains("global Ratio ÷ texture Ratio", loaded.PsbRenderCompensationLabel);
        Assert.Contains("disabled", loaded.IndependentPsbTextureScalingLabel);
        Assert.Contains("Unavailable", loaded.PsbRenderCompensationHelp);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompensationCannotBeCheckedWithoutIndependentScalingEvenWithSavedPreference(bool savedCompensation)
    {
        var store = new MemoryStore(new(PsbRenderCompensation: savedCompensation));
        var vm = new MainViewModel(settings: store);
        Assert.False(vm.IndependentPsbTextureScaling); Assert.False(vm.PsbRenderCompensation);
        vm.PsbRenderCompensation = true;
        Assert.False(vm.PsbRenderCompensation);
        vm.ToggleTheme();
        Assert.False(store.Value.PsbRenderCompensation);
        Assert.False(new MainViewModel(settings: store).PsbRenderCompensation);
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
    public async Task CompensationPreviewStaysInactiveWhenIndependentScalingIsDisabled()
    {
        using var folder = new ScanFolder();
        var vm = new MainViewModel(settings: new MemoryStore(new()), psbScanner: new PsbScannerStub { Files = [AtlasFile(4096, 2048)] })
        { InputDirectory = folder.Path, Ratio = "0.5" };
        await vm.ScanPsbAsync();
        var row = Assert.Single(vm.PsbCategories);
        Assert.Contains("未启用", row.RenderCompensation);
        vm.IndependentPsbTextureScaling = true; row.Ratio = "0.25";
        Assert.False(vm.IndependentPsbTextureScaling); Assert.Contains("未启用", row.RenderCompensation);
        vm.Ratio = "0.75"; Assert.Contains("未启用", row.RenderCompensation);
        vm.PsbRenderCompensation = false; Assert.Contains("未启用", row.RenderCompensation);
        vm.PsbRenderCompensation = true; vm.ProcessAnimation = false; Assert.Contains("未启用", row.RenderCompensation);
        vm.ProcessAnimation = true; vm.SetLanguage("en-US"); Assert.Contains("inactive", row.RenderCompensation);
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
        Assert.Contains("disabled", vm.PsbEmptyHelp);
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
    public async Task StartAlwaysUsesGlobalRatioAndIgnoresDisabledCategoryRules()
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
            Assert.NotNull(converter.Options);
            Assert.Equal(.75, converter.Options.Ratio);
            Assert.False(converter.Options.PsbTextures!.Enabled);
            Assert.False(converter.Options.PsbTextures.CompensateRendering); Assert.Null(converter.Options.PsbTextures.Rules);
            row.Ratio = "0.25"; await vm.StartAsync();
            Assert.Equal(.75, converter.Options.Ratio); Assert.False(converter.Options.PsbTextures.Enabled);
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
