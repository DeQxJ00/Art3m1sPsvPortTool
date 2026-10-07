using Art3m1s.PsvTool.App;
using Art3m1s.PsvTool.Core;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

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
    var independent = window.FindControl<CheckBox>("IndependentPsbScalingCheckBox")!;
    var compensation = window.FindControl<CheckBox>("PsbRenderCompensationCheckBox")!;
    var scanPsb = window.FindControl<Button>("ScanPsbButton")!;
    var psbTitle = window.FindControl<TextBlock>("PsbSectionTitle")!;
    var psbCard = window.FindControl<Border>("PsbSectionCard")!;
    if (psbCard.IsEnabled || independent.IsEnabled || independent.IsChecked == true || compensation.IsEnabled || compensation.IsChecked == true || scanPsb.IsEnabled || psbTitle.IsEnabled)
        throw new InvalidOperationException("PSB scanning, independent Ratio controls and title must remain disabled.");
    if (!compensation.GetVisualAncestors().Contains(psbCard))
        throw new InvalidOperationException("PSB render compensation must be inside the disabled PSB card.");
    if (Grid.GetRow(psbCard) != 4)
        throw new InvalidOperationException("The disabled PSB card must be the last section.");
    var psbFormat = window.FindControl<ComboBox>("PsbFormatComboBox")!;
    if (!psbFormat.IsEffectivelyEnabled || psbFormat.SelectedIndex != 0 || psbFormat.Items.Count != 3)
        throw new InvalidOperationException("PSB format selection must remain active and default to DXT5 (BC3).");
    foreach (int index in new[] { 1, 2, 0 })
    {
        psbFormat.SelectedIndex = index; Dispatcher.UIThread.RunJobs();
        if (window.ViewModel.SelectedPsbFormat != index)
            throw new InvalidOperationException("PSB format selection does not update the ViewModel.");
    }
    window.ViewModel.ConvertEmotePsbTexturesToDxt5 = false; Dispatcher.UIThread.RunJobs();
    if (psbFormat.IsEffectivelyEnabled) throw new InvalidOperationException("Disabling PSB conversion must disable its format selector.");
    window.ViewModel.ConvertEmotePsbTexturesToDxt5 = true; Dispatcher.UIThread.RunJobs();
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
        var psbScroll = window.FindControl<ScrollViewer>("MainScroll")!;
        psbScroll.Offset = new Vector(0, Math.Max(0, psbScroll.Extent.Height - psbScroll.Viewport.Height));
        Dispatcher.UIThread.RunJobs();
    }
    if (args.Contains("--psb-formats"))
    {
        var scroll = window.FindControl<ScrollViewer>("MainScroll")!;
        var advancedCard = psbFormat.GetVisualAncestors().OfType<Border>().First(card => card.Classes.Contains("card"));
        var point = advancedCard.TranslatePoint(default, (Visual)scroll.Content!);
        scroll.Offset = new Vector(0, point?.Y ?? 0); Dispatcher.UIThread.RunJobs();
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
