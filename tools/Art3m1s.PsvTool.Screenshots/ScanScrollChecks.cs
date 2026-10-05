using System.Diagnostics;
using Art3m1s.PsvTool.App;
using Art3m1s.PsvTool.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

internal static class ScanScrollChecks
{
    public static void Run(string output)
    {
        string input = Path.Combine(Path.GetTempPath(), "scan-scroll-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(input);
        try
        {
            foreach (string language in new[] { "zh-CN", "en-US" })
                foreach (bool psb in new[] { false, true })
                    foreach (double offset in new[] { 0d, 180d })
                    {
                        var vm = new MainViewModel(settings: new ScreenshotSettingsStore(language, true),
                            scanner: new Scanner(input), psbScanner: new PsbScanner()) { InputDirectory = input, Ratio = "0.75" };
                        MainWindow window = new(vm) { Width = 1180, Height = 1000 };
                        try
                        {
                            window.Show(); Settle(window);
                            var scroll = window.FindControl<ScrollViewer>("MainScroll")!;
                            var button = window.FindControl<Button>(psb ? "ScanPsbButton" : "AutoScanResolutionButton")!;
                            button.Focus(); Settle(window);
                            scroll.Offset = new Vector(0, offset); Settle(window);
                            double before = scroll.Offset.Y;
                            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            Stopwatch wait = Stopwatch.StartNew();
                            while (vm.IsBusy && wait.Elapsed < TimeSpan.FromSeconds(10))
                            { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
                            if (vm.IsBusy) throw new InvalidOperationException("Scan timed out.");
                            Settle(window);
                            if (vm.Ratio != (psb ? "0.75" : "0.5") || !psb && vm.SelectedResolutionIndex != 20)
                                throw new InvalidOperationException("Wrong ratio or preferred WINDOWS resolution after scan.");
                            string name = $"{language}-{(psb ? "psb" : "resolution")}-{offset}";
                            using (var bitmap = window.CaptureRenderedFrame())
                                bitmap?.Save(Path.Combine(output, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                            var log = window.FindControl<TextBox>("LogTextBox")!;
                            var logScroll = log.GetVisualDescendants().OfType<ScrollViewer>().First();
                            Console.WriteLine($"{name}: main {before} -> {scroll.Offset.Y}; log {logScroll.Offset.Y}/{logScroll.Extent.Height - logScroll.Viewport.Height}");
                            if (Math.Abs(scroll.Offset.Y - before) > 1)
                                throw new InvalidOperationException($"{name}: scan moved the main viewport from {before} to {scroll.Offset.Y}.");
                            if (window.FocusManager?.GetFocusedElement() == log)
                                throw new InvalidOperationException($"{name}: scan focused the log.");
                            if (logScroll.Extent.Height > logScroll.Viewport.Height &&
                                Math.Abs(logScroll.Offset.Y - (logScroll.Extent.Height - logScroll.Viewport.Height)) > 1)
                                throw new InvalidOperationException($"{name}: internal log auto-scroll stopped working.");
                        }
                        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
                    }
        }
        finally { Directory.Delete(input, true); }
    }

    private static void Settle(MainWindow window)
    {
        for (int frame = 0; frame < 4; frame++)
        { Dispatcher.UIThread.RunJobs(); using var bitmap = window.CaptureRenderedFrame(); }
    }

    private sealed class Scanner(string input) : IProjectScanner
    {
        public Task<ScanResult> ScanAsync(string inputDirectory, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ScanResult(Enumerable.Range(0, 30).Select(i =>
                new PfsFileInfo(Path.Combine(input, $"root.pfs.{i:000}"), $"root.pfs.{i:000}", '8', 100)).ToArray(),
                true, 1920, 1080, Enumerable.Range(0, 20).Select(i => new IniResolution("root.pfs/system.ini", $"DISPLAY{i}", 1280, 720))
                    .Append(new("root.pfs/system.ini", "WINDOWS", 1920, 1080)).ToArray()));
    }

    private sealed class PsbScanner : IPsbTextureScanner
    {
        public Task<IReadOnlyList<PsbTextureFileInfo>> ScanAsync(string input, PfsNameEncoding encoding = PfsNameEncoding.Auto,
            IProgress<ConversionProgress>? progress = null, CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<PsbTextureFileInfo>>(Enumerable.Range(0, 30).Select(i =>
                new PsbTextureFileInfo("root.pfs.010", $"image/fg/hero{i}.psb", 100,
                    new(true, [new("atlas0", 4096, 2048, "RGBA8")]))).ToArray());
    }
}
