using Art3m1s.PsvTool.App;
using Art3m1s.PsvTool.Core;
using Xunit;

namespace Art3m1s.PsvTool.Ui.Tests;

public sealed class UiTests
{
    [Fact]
    public void ChineseAndEnglishHaveIdenticalResourceKeys() =>
        Assert.Equal(Localizer.ChineseKeys.Order(), Localizer.EnglishKeys.Order());

    [Fact]
    public void ChineseIsTheFirstLaunchDefault()
    {
        Localizer localizer = new();
        MainViewModel viewModel = new(localizer, settings: new MemorySettingsStore());
        Assert.Equal("zh-CN", localizer.Language);
        Assert.Equal("图片（PNG）", localizer["Images"]);
        Assert.Equal("字体削减（TTF / OTF）", viewModel.FontSubsetLabel);
        Assert.Equal("动画（OGV / E-mote PSB）", viewModel.AnimationLabel);
        Assert.Contains("art3m1s-core", viewModel.AboutBody);
        Assert.Equal("项目 GitHub", viewModel.ProjectRepositoryLabel);
        Assert.Equal("https://github.com/DeQxJ00/art3m1s_psv_port_tool", MainWindow.RepositoryUrl);
        Assert.True(viewModel.IgnorePfsVideos);
        Assert.True(viewModel.ConvertEmotePsbTexturesToDxt5);
        Assert.Equal("E-mote PSB 纹理格式转换", viewModel.ConvertEmotePsbTexturesToDxt5Label);
        Assert.Equal("自动扫描分辨率", viewModel.AutoScanResolutionLabel);
        Assert.Equal("请只处理你有权修改的游戏资源，并先备份原项目。", viewModel.AssetRightsBackupWarning);
        Assert.Contains("WMV / DAT / MP4 / AVI / MPG / MKV", viewModel.IgnorePfsVideosLabel);
        Assert.DoesNotContain("OGV", viewModel.IgnorePfsVideosLabel);
    }

    [Fact]
    public void LanguageSwitchUpdatesLongLabels()
    {
        Localizer localizer = new(); MainViewModel viewModel = new(localizer, settings: new MemorySettingsStore());
        List<string?> changed = [];
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        viewModel.SetLanguage("en-US");
        Assert.Equal("Video (WMV / DAT / MP4 / AVI / MPG / MKV)", viewModel.VideoLabel);
        Assert.Equal("Font subsetting (TTF / OTF)", viewModel.FontSubsetLabel);
        Assert.Equal("Animation (OGV / E-mote PSB)", viewModel.AnimationLabel);
        Assert.Equal("Ignore video inside PFS (WMV / DAT / MP4 / AVI / MPG / MKV)", viewModel.IgnorePfsVideosLabel);
        Assert.Equal("art3m1s PSV Port Tool", viewModel.Title);
        Assert.Equal("Project GitHub", viewModel.ProjectRepositoryLabel);
        Assert.Equal("Convert E-mote PSB texture format", viewModel.ConvertEmotePsbTexturesToDxt5Label);
        Assert.Equal("Detect resolution", viewModel.AutoScanResolutionLabel);
        Assert.Equal("Only process game assets you are authorized to modify, and back up the original project first.", viewModel.AssetRightsBackupWarning);
        Assert.Contains(nameof(MainViewModel.AssetRightsBackupWarning), changed);
    }

    [Fact]
    public async Task AutoScanResolutionComputesRatioAndManualSelectionOnlyChangesPreview()
    {
        string root = Path.Combine(Path.GetTempPath(), "art3m1s-ui-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            MainViewModel viewModel = new(scanner: new FixedScanner(1920, 1080), settings: new MemorySettingsStore())
            { InputDirectory = root, Ratio = "0.25" };
            await viewModel.AutoScanResolutionAsync();
            Assert.Equal("1920 × 1080", viewModel.OriginalResolution);
            Assert.Equal("960 × 540", viewModel.TargetResolution);
            Assert.Equal("0.5", viewModel.Ratio);
            Assert.Contains("已识别原始分辨率", viewModel.Status);
            Assert.Equal(2, viewModel.ResolutionChoices.Count);
            Assert.Equal(1, viewModel.SelectedResolutionIndex);
            viewModel.SelectedResolutionIndex = 0;
            Assert.Equal("960 × 540", viewModel.OriginalResolution);
            Assert.Equal("480 × 270", viewModel.TargetResolution);
            Assert.Equal("0.5", viewModel.Ratio);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(1280, 720, .75)]
    [InlineData(1920, 1080, .5)]
    [InlineData(2560, 1440, .375)]
    [InlineData(3840, 2160, .25)]
    [InlineData(1280, 1024, .52734375)]
    [InlineData(1080, 1920, .28125)]
    [InlineData(320, 180, 1d)]
    public async Task AutomaticRatioFitsBothPsvDimensionsWithoutUpscaling(int width, int height, double expected)
    {
        string root = Path.Combine(Path.GetTempPath(), "art3m1s-ratio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            MainViewModel vm = new(scanner: new FixedScanner(width, height), settings: new MemorySettingsStore())
            { InputDirectory = root, Ratio = "0.1" };
            await vm.AutoScanResolutionAsync();
            double ratio = double.Parse(vm.Ratio, System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(expected, ratio);
            Assert.True(width * ratio <= 960); Assert.True(height * ratio <= 540);
            Assert.InRange(ratio, double.Epsilon, 1);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DetectionSelectsWindowsEvenWhenAnotherSectionHasIdenticalDimensions()
    {
        string root = Path.Combine(Path.GetTempPath(), "art3m1s-ratio-select-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            MainViewModel vm = new(scanner: new ResultScanner(new([], true, 1920, 1080,
                [new("root.pfs/system.ini", "VITA", 1920, 1080), new("root.pfs/system.ini", "windows", 1920, 1080)])),
                settings: new MemorySettingsStore()) { InputDirectory = root };
            await vm.AutoScanResolutionAsync();
            Assert.Equal(1, vm.SelectedResolutionIndex); Assert.Equal("0.5", vm.Ratio);
            vm = new(scanner: new ResultScanner(new([], true, 800, 600,
                [new("root.pfs/system.ini", "DISPLAY", 800, 600), new("root.pfs.010/system.ini", "WINDOWS", 1920, 1080)])),
                settings: new MemorySettingsStore()) { InputDirectory = root };
            await vm.AutoScanResolutionAsync();
            Assert.Equal(0, vm.SelectedResolutionIndex); Assert.Equal("0.9", vm.Ratio);
            Assert.Equal("800 × 600", vm.OriginalResolution);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task GeneralScanAndMissingResolutionDoNotOverwriteManualRatio()
    {
        string root = Path.Combine(Path.GetTempPath(), "art3m1s-ratio-manual-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            MainViewModel vm = new(scanner: new FixedScanner(1920, 1080), settings: new MemorySettingsStore())
            { InputDirectory = root, Ratio = "0.25" };
            await vm.ScanAsync();
            Assert.Equal("0.25", vm.Ratio);
            vm = new(scanner: new ResultScanner(new([], false, null, null, [])), settings: new MemorySettingsStore())
            { InputDirectory = root, Ratio = "0.375" };
            await vm.AutoScanResolutionAsync();
            Assert.Equal("0.375", vm.Ratio); Assert.Equal("未知", vm.OriginalResolution);
            Assert.False(vm.HasResolutionChoices);
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class ResultScanner(ScanResult result) : IProjectScanner
    {
        public Task<ScanResult> ScanAsync(string inputDirectory, CancellationToken cancellationToken = default)
            => Task.FromResult(result);
    }

    private sealed class FixedScanner(int width, int height) : IProjectScanner
    {
        public Task<ScanResult> ScanAsync(string inputDirectory, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ScanResult([], true, width, height,
                [new IniResolution("root.pfs/system.ini", "VITA", 960, 540),
                    new IniResolution("root.pfs/system.ini", "WINDOWS", width, height)]));
    }

    [Fact]
    public void LanguageAndThemeArePersistedSeparately()
    {
        MemorySettingsStore store = new(); MainViewModel viewModel = new(settings: store);
        viewModel.SetLanguage("en-US"); viewModel.ToggleTheme();
        Assert.Equal("en-US", store.Value.Language);
        Assert.False(store.Value.IsDark);
        viewModel.ConvertEmotePsbTexturesToDxt5 = false;
        Assert.False(store.Value.ConvertEmotePsbTexturesToDxt5);
    }

    [Fact]
    public void OldSettingsWithoutBc3OptionDefaultToEnabled()
    {
        string path = Path.Combine(Path.GetTempPath(), $"art3m1s-settings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{\"Language\":\"en-US\",\"IsDark\":false}");
            AppSettings settings = new LocalSettingsStore(path).Load();
            Assert.Equal("en-US", settings.Language);
            Assert.False(settings.IsDark);
            Assert.True(settings.ConvertEmotePsbTexturesToDxt5);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Bc3OptionRoundTripsThroughLocalSettings()
    {
        string path = Path.Combine(Path.GetTempPath(), $"art3m1s-settings-{Guid.NewGuid():N}.json");
        try
        {
            LocalSettingsStore store = new(path);
            store.Save(new AppSettings("zh-CN", true, false));
            Assert.False(store.Load().ConvertEmotePsbTexturesToDxt5);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ConversionFailureLogShowsExactArchiveFileAndReason()
    {
        string root = Path.Combine(Path.GetTempPath(), "art3m1s-ui-error-" + Guid.NewGuid().ToString("N"));
        string input = Path.Combine(root, "input");
        Directory.CreateDirectory(input);
        try
        {
            MainViewModel viewModel = new(converter: new FailingConversionService(), settings: new MemorySettingsStore())
            {
                InputDirectory = input,
                OutputDirectory = Path.Combine(root, "output")
            };

            await viewModel.StartAsync();

            Assert.Contains("PFS 归档: root.pfs.010", viewModel.Log);
            Assert.Contains("错误文件: image\\bg\\broken.png", viewModel.Log);
            Assert.Contains("错误原因: invalid PNG", viewModel.Log);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class MemorySettingsStore : ISettingsStore
    {
        public AppSettings Value { get; private set; } = new();
        public AppSettings Load() => Value;
        public void Save(AppSettings settings) => Value = settings;
    }

    private sealed class FailingConversionService : IConversionService
    {
        public Task ConvertAsync(ConversionOptions options, IProgress<ConversionProgress>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromException(new ConversionItemException("root.pfs.010", "image\\bg\\broken.png", new InvalidDataException("invalid PNG")));
    }
}
