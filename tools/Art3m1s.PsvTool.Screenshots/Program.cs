using Art3m1s.PsvTool.App;
using Art3m1s.PsvTool.Core;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;

AppBuilder.Configure<App>()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .UseSkia()
    .WithInterFont()
    .SetupWithoutStarting();

string output = args.Length == 0 ? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../docs/screenshots")) : Path.GetFullPath(args[0]);
Directory.CreateDirectory(output);
if (args.Contains("--scan-scroll-checks"))
{
    ScanScrollChecks.Run(output);
    return;
}
if (args.Contains("--scroll-benchmark"))
{
    ScrollBenchmark.Run(output);
    return;
}
foreach ((string language, bool dark, string fileName) in new[]
{
    ("zh-CN", true, "ui-zh-CN-dark.png"), ("en-US", true, "ui-en-US-dark.png"),
    ("zh-CN", false, "ui-zh-CN-light.png"), ("en-US", false, "ui-en-US-light.png")
})
{
    Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
    MainWindow window = new(new MainViewModel(settings: new ScreenshotSettingsStore(language, dark))) { Width = 1180, Height = 1000, Position = new PixelPoint(0, 0) };
    window.ViewModel.SetLanguage(language); window.ViewModel.ConfigureScreenshotDemo(); window.Show(); Dispatcher.UIThread.RunJobs();
    if (args.Contains("--textures"))
    {
        foreach (var info in new TextureImageInfo[]
        {
            new(null, "image/bg/room.png", "image/bg", 1920, 1080, 2100000, false, false, false, false),
            new(null, "image/bg/mask.png", "image/bg", 1920, 1080, 310000, true, false, false, false),
            new("root.pfs.001", "image/fg/body.png", "image/fg", 1000, 1600, 1300000, false, true, true, false)
        }) window.ViewModel.TextureCategories.Add(new TextureCategoryViewModel([info], .5, false, language == "en-US"));
        Dispatcher.UIThread.RunJobs();
        window.FindControl<ScrollViewer>("MainScroll")!.Offset = new Vector(0, 430);
        Dispatcher.UIThread.RunJobs();
    }
    if (args.Contains("--psb"))
    {
        Localizer localizer = new(); localizer.SetLanguage(language);
        var row = new PsbCategoryViewModel(4096, 2048, .5, localizer);
        row.SetFiles([new("root.pfs.010", "image/fg/hero.psb", 34000000,
            new(true, [new("atlas0", 4096, 2048, "RGBA8"), new("atlas1", 512, 256, "DXT5")]))]);
        window.ViewModel.PsbCategories.Add(row);
        Dispatcher.UIThread.RunJobs();
        window.FindControl<ScrollViewer>("MainScroll")!.Offset = new Vector(0, 430);
        Dispatcher.UIThread.RunJobs();
    }
    using Avalonia.Media.Imaging.Bitmap bitmap = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("The headless window did not render.");
    bitmap.Save(Path.Combine(output, fileName), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); window.Close(); Dispatcher.UIThread.RunJobs();
}

sealed class ScreenshotSettingsStore : ISettingsStore
{
    private readonly AppSettings _settings;
    public ScreenshotSettingsStore(string language, bool isDark) => _settings = new AppSettings(language, isDark);
    public AppSettings Load() => _settings;
    public void Save(AppSettings settings) { }
}
