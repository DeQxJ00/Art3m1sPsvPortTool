using System.Diagnostics;
using Art3m1s.PsvTool.App;
using Art3m1s.PsvTool.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

internal static class PsbSizeChecks
{
    public static void Run(string output)
    {
        string input = Path.Combine(Path.GetTempPath(), "psb-size-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(input);
        try
        {
            foreach (string language in new[] { "zh-CN", "en-US" })
                foreach (bool dark in new[] { true, false })
                    foreach (bool small in new[] { false, true })
                    {
                        Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                        var vm = new MainViewModel(settings: new ScreenshotSettingsStore(language, dark), psbScanner: new Scanner())
                        { InputDirectory = input, Ratio = "0.75" };
                        MainWindow window = new(vm) { Width = small ? 800 : 1180, Height = small ? 720 : 1000 };
                        try
                        {
                            window.Show(); Settle(window);
                            var scroll = window.FindControl<ScrollViewer>("MainScroll")!;
                            var button = window.FindControl<Button>("CalculatePsbSizeButton")!;
                            var card = button.GetVisualAncestors().OfType<Border>().First(x => x.Classes.Contains("card"));
                            var point = card.TranslatePoint(default, (Visual)scroll.Content!);
                            scroll.Offset = new Vector(0, point?.Y ?? 0); Settle(window);
                            double before = scroll.Offset.Y;
                            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            if (!vm.IsBusy || button.IsEffectivelyEnabled) throw new InvalidOperationException("Size scan must disable its button while busy.");
                            Stopwatch wait = Stopwatch.StartNew();
                            while (vm.IsBusy && wait.Elapsed < TimeSpan.FromSeconds(10))
                            { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
                            Settle(window);
                            if (vm.IsBusy || !vm.HasPsbSizeEstimate || !vm.PsbSizeSummary.Contains("17,039,360 B", StringComparison.Ordinal))
                                throw new InvalidOperationException("Size scan failed or missed an atlas.");
                            if (Math.Abs(scroll.Offset.Y - before) > 1) throw new InvalidOperationException("Size scan moved the main viewport.");
                            if (vm.PsbCategories.Count != 0 || vm.CanUseIndependentPsbTextureScaling)
                                throw new InvalidOperationException("Size estimates must not enable independent PSB controls.");
                            var format = window.FindControl<ComboBox>("PsbFormatComboBox")!;
                            format.SelectedIndex = 2; Settle(window);
                            if (!vm.PsbSizeSummary.Contains("2,396,160 B", StringComparison.Ordinal))
                                throw new InvalidOperationException("Format changes did not update size.");
                            format.SelectedIndex = 0; vm.SelectedPsbDxt5Layout = 1; Settle(window);
                            if (!vm.PsbSizeSummary.Contains("9,584,640 B", StringComparison.Ordinal))
                                throw new InvalidOperationException("Layout changes did not update size.");
                            vm.SelectedPsbDxt5Layout = 0;
                            vm.SetLanguage(language == "en-US" ? "zh-CN" : "en-US"); Settle(window);
                            vm.SetLanguage(language); Settle(window);
                            if (!button.Content!.ToString()!.Contains(language == "en-US" ? "Estimate" : "计算", StringComparison.Ordinal))
                                throw new InvalidOperationException("Size button did not change language.");
                            var bounds = button.TranslatePoint(default, card)!.Value;
                            if (bounds.X < 0 || bounds.X + button.Bounds.Width > card.Bounds.Width)
                                throw new InvalidOperationException("Size button overflows its card.");
                            string name = $"psb-size-{language}-{(dark ? "dark" : "light")}{(small ? "-small" : "")}";
                            using var bitmap = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame.");
                            bitmap.Save(Path.Combine(output, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                            Console.WriteLine($"{name}: metadata scan, live estimates and viewport checks passed.");
                        }
                        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
                    }
        }
        finally { Directory.Delete(input, true); }
    }

    private static void Settle(MainWindow window)
    {
        for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); }
    }

    private sealed class Scanner : IPsbTextureScanner
    {
        public async Task<IReadOnlyList<PsbTextureFileInfo>> ScanAsync(string input, PfsNameEncoding encoding = PfsNameEncoding.Auto,
            IProgress<ConversionProgress>? progress = null, CancellationToken token = default)
        {
            await Task.Delay(20, token);
            return [new("root.pfs.010", "image/fg/hero.psb", 100,
                new(true, [new("atlas0", 4096, 2048, "RGBA8", 0), new("atlas1", 512, 256, "RGBA8", 1)])),
                new(null, "image/fg/hero2.psb", 100,
                new(true, [new("atlas0", 4096, 2048, "RGBA8", 0), new("atlas1", 512, 256, "RGBA8", 1)]))];
        }
    }
}
