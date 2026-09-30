using System.Diagnostics;
using System.Text.Json;
using Art3m1s.PsvTool.App;
using Art3m1s.PsvTool.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;

internal static class ScrollBenchmark
{
    public static void Run(string output)
    {
        List<object> results = [];
        foreach (bool populated in new[] { false, true })
        {
            var vm = new MainViewModel(settings: new ScreenshotSettingsStore("zh-CN", true));
            vm.NativeTextures = populated;
            if (populated)
                for (int c = 0; c < 20; c++)
                {
                    string category = c == 0 ? "image/bg" : $"image/category{c}";
                    var images = Enumerable.Range(0, c == 0 ? 4000 : 300).Select(i =>
                        new TextureImageInfo("root.pfs.001", $"{category}/image{i}.png", category,
                            1920, 1080, 123456, false, i % 2 == 0, i % 2 == 0, false)).ToArray();
                    vm.TextureCategories.Add(new(images, .5, false, false));
                }
            var start = Stopwatch.StartNew();
            MainWindow window = new(vm) { Width = 1180, Height = 1000 };
            window.Show(); Dispatcher.UIThread.RunJobs();
            using (var initial = window.CaptureRenderedFrame()) { }
            double setupMs = start.Elapsed.TotalMilliseconds;
            var scroll = window.FindControl<ScrollViewer>("MainScroll")!;
            List<double> frames = [];
            // Warm the renderer before comparing identical software-rendered scroll paths.
            for (int frame = 0; frame < 36; frame++)
            {
                start.Restart();
                double limit = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
                scroll.Offset = new Vector(0, limit * ((frame % 18) / 17d));
                Dispatcher.UIThread.RunJobs();
                using (var bitmap = window.CaptureRenderedFrame()) { }
                if (frame >= 6) frames.Add(start.Elapsed.TotalMilliseconds);
            }
            frames.Sort();
            var row = new { populated, setupMs, medianMs = frames[frames.Count / 2],
                p95Ms = frames[(int)(frames.Count * .95)], maxMs = frames[^1],
                controls = window.GetVisualDescendants().Count(), extent = scroll.Extent.Height };
            results.Add(row); Console.WriteLine(JsonSerializer.Serialize(row));
            if (populated)
            {
                // The largest category must not realize all 4,000 text controls.
                var expander = window.GetVisualDescendants().OfType<Expander>()
                    .First(e => e.DataContext == vm.TextureCategories[0]);
                start.Restart(); expander.IsExpanded = true;
                Dispatcher.UIThread.RunJobs();
                var list = expander.GetVisualDescendants().OfType<ListBox>().Single();
                scroll.Offset = new Vector(0, 700); Dispatcher.UIThread.RunJobs();
                using (var expanded = window.CaptureRenderedFrame())
                    expanded?.Save(Path.Combine(output, "expanded-details.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                int visibleRows = list.GetVisualDescendants().OfType<ListBoxItem>().Count();
                if (list.ItemCount != 4000 || visibleRows == 0 || visibleRows > 40)
                    throw new InvalidOperationException($"File details are not virtualized: {visibleRows}/{list.ItemCount}");
                var detail = new { detailItems = list.ItemCount, realizedRows = visibleRows, expandAndCaptureMs = start.Elapsed.TotalMilliseconds };
                results.Add(detail); Console.WriteLine(JsonSerializer.Serialize(detail));
                list.ScrollIntoView(list.ItemCount - 1); Dispatcher.UIThread.RunJobs();
                using (var last = window.CaptureRenderedFrame()) { }
                var lastRows = list.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
                if (lastRows.Length > 40 || !lastRows.Any(item => item.DataContext?.ToString()?.Contains("image3999.png", StringComparison.Ordinal) == true))
                    throw new InvalidOperationException("Virtualized details did not reach the last file.");
            }
            window.Close(); Dispatcher.UIThread.RunJobs();
        }
        File.WriteAllText(Path.Combine(output, "scroll-benchmark.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    }
}
