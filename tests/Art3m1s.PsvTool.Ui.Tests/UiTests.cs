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
        Assert.Equal("E-mote PSB 纹理转 DXT5（BC3）", viewModel.ConvertEmotePsbTexturesToDxt5Label);
        Assert.Equal("自动扫描分辨率", viewModel.AutoScanResolutionLabel);
        Assert.Contains("WMV / DAT / MP4 / AVI / MPG / MKV", viewModel.IgnorePfsVideosLabel);
        Assert.DoesNotContain("OGV", viewModel.IgnorePfsVideosLabel);
    }

    [Fact]
    public void LanguageSwitchUpdatesLongLabels()
    {
        Localizer localizer = new(); MainViewModel viewModel = new(localizer, settings: new MemorySettingsStore());
        localizer.SetLanguage("en-US");
        Assert.Equal("Video (WMV / DAT / MP4 / AVI / MPG / MKV)", viewModel.VideoLabel);
        Assert.Equal("Font subsetting (TTF / OTF)", viewModel.FontSubsetLabel);
        Assert.Equal("Animation (OGV / E-mote PSB)", viewModel.AnimationLabel);
        Assert.Equal("Ignore video inside PFS (WMV / DAT / MP4 / AVI / MPG / MKV)", viewModel.IgnorePfsVideosLabel);
        Assert.Equal("art3m1s PSV Port Tool", viewModel.Title);
        Assert.Equal("Project GitHub", viewModel.ProjectRepositoryLabel);
        Assert.Equal("Convert E-mote PSB textures to DXT5 (BC3)", viewModel.ConvertEmotePsbTexturesToDxt5Label);
        Assert.Equal("Detect resolution", viewModel.AutoScanResolutionLabel);
    }

    [Fact]
    public async Task AutoScanResolutionUpdatesPreviewWithoutChangingRatio()
    {
        string root = Path.Combine(Path.GetTempPath(), "art3m1s-ui-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            MainViewModel viewModel = new(scanner: new FixedScanner(1920, 1080), settings: new MemorySettingsStore())
            { InputDirectory = root };
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
