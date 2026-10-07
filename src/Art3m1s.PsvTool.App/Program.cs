using Avalonia;
using Art3m1s.PsvTool.Core;
using Optris.StaticGraphics;
using System.Text;

namespace Art3m1s.PsvTool.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--self-test", StringComparer.Ordinal))
            return RunSelfTestAsync().GetAwaiter().GetResult();
        if (args.Length == 2 && args[0].Equals("--font-self-test", StringComparison.Ordinal))
            return RunFontSelfTestAsync(args[1]).GetAwaiter().GetResult();
        if (args.Length == 2 && args[0].Equals("--psb-self-test", StringComparison.Ordinal))
            return RunPsbSelfTestAsync(args[1]).GetAwaiter().GetResult();
        if (args.Length == 3 && args[0].Equals("--psb-resize-test", StringComparison.Ordinal))
            return RunPsbResizeTestAsync(args[1], args[2]).GetAwaiter().GetResult();
        if (args.Length == 4 && args[0].Equals("--psb-bc3-test", StringComparison.Ordinal))
            return RunPsbBc3TestAsync(args[1], args[2], args[3]).GetAwaiter().GetResult();
        if (args.Length == 5 && args[0].Equals("--psb-format-test", StringComparison.Ordinal))
        {
            PsbTextureFormat? format = args[4].ToLowerInvariant() switch
            {
                "dxt5" => PsbTextureFormat.Dxt5,
                "pvrtc2-4" => PsbTextureFormat.Pvrtc2_4,
                "pvrtc2-2" => PsbTextureFormat.Pvrtc2_2,
                _ => null
            };
            return format.HasValue ? RunPsbBc3TestAsync(args[1], args[2], args[3], format.Value).GetAwaiter().GetResult() : 12;
        }
        if (args.Length == 3 && args[0].Equals("--psb-pfs-self-test", StringComparison.Ordinal))
            return RunPsbPfsSelfTestAsync(args[1], args[2]).GetAwaiter().GetResult();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] })
        .With(new X11PlatformOptions { RenderingMode = [X11RenderingMode.Software] })
        .With(new AvaloniaNativePlatformOptions { RenderingMode = [AvaloniaNativeRenderingMode.Software] })
        .WithOptrisStaticGraphics()
        .WithInterFont()
        .LogToTrace();

    private static async Task<int> RunSelfTestAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "art3m1s-native-selftest-" + Guid.NewGuid().ToString("N"));
        string input = Path.Combine(root, "input"), output = Path.Combine(root, "output"), payload = Path.Combine(root, "payload.bin");
        try
        {
            Directory.CreateDirectory(input);
            await File.WriteAllBytesAsync(payload, [1, 3, 3, 7]);
            PfsCodec codec = new();
            ExtractedArchive archive = new('8', [new PfsEntry(Encoding.UTF8.GetBytes("data/payload.bin"), "data/payload.bin", 0, 4, payload)]);
            await codec.PackPf8Async(archive, Path.Combine(input, "root.pfs"));
            await File.WriteAllTextAsync(Path.Combine(input, "system.ini"), "[WINDOWS]\nCHARSET=UTF-8\nWIDTH=1920\nHEIGHT=1080\n");
            await new ConversionService().ConvertAsync(new ConversionOptions(input, output, Categories: AssetCategories.None));
            if (!File.Exists(Path.Combine(output, "root.pfs")) || !File.ReadAllText(Path.Combine(output, "system.ini")).Contains("[VITA]", StringComparison.Ordinal)) return 2;
            string texturePath = Path.Combine(root, "背景.png");
            using (var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(32, 16,
                new SixLabors.ImageSharp.PixelFormats.Rgba32(40, 120, 210, 128)))
                await SixLabors.ImageSharp.ImageExtensions.SaveAsPngAsync(image, texturePath);
            var info = TextureScanner.InspectBytes(await File.ReadAllBytesAsync(texturePath), "image/bg/test.png");
            var converted = await new NativeTextureProcessor().ConvertAsync(texturePath, info, new(), 1);
            if (!converted.Converted || !File.ReadAllBytes(converted.OutputPath).AsSpan().StartsWith("DDS "u8)) return 12;
            foreach (var format in new[] { NativeTextureFormat.Pvrtc2_4, NativeTextureFormat.Pvrtc2_2 })
            {
                texturePath = Path.Combine(root, format + ".png");
                using (var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(32, 16,
                    new SixLabors.ImageSharp.PixelFormats.Rgba32(40, 120, 210, 128)))
                    await SixLabors.ImageSharp.ImageExtensions.SaveAsPngAsync(image, texturePath);
                info = TextureScanner.InspectBytes(await File.ReadAllBytesAsync(texturePath), "image/fg/test.png");
                converted = await new NativeTextureProcessor().ConvertAsync(texturePath, info,
                    new(Rules: [new(info.GroupKey, true, format)]), 1);
                if (!converted.Converted || new FileInfo(converted.OutputPath).Length !=
                    52 + NativeTextureFormats.PayloadBytes(format, 32, 16)) return 16;
            }
            return 0;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static async Task<int> RunFontSelfTestAsync(string source)
    {
        string target = Path.Combine(Path.GetTempPath(), "art3m1s-native-font-selftest-" + Guid.NewGuid().ToString("N") + Path.GetExtension(source));
        try
        {
            File.Copy(source, target);
            long before = new FileInfo(target).Length;
            bool changed = await new FontSubsetProcessor().SubsetAsync(target,
                FontSubsetProfile.SimplifiedChinese, new HashSet<int> { 'A', '中' });
            return changed && new FileInfo(target).Length < before ? 0 : 3;
        }
        catch { return 4; }
        finally { if (File.Exists(target)) File.Delete(target); }
    }

    private static async Task<int> RunPsbSelfTestAsync(string source)
    {
        try
        {
            PsbInspection result = await new PsbProcessor().InspectAsync(source);
            return result.Version is >= 1 and <= 4 ? 0 : 5;
        }
        catch { return 6; }
    }

    private static async Task<int> RunPsbResizeTestAsync(string source, string ratioText)
    {
        try
        {
            if (!double.TryParse(ratioText, System.Globalization.CultureInfo.InvariantCulture, out double ratio)) return 7;
            await new PsbProcessor().ResizeAsync(source, ratio);
            await new PsbProcessor().InspectAsync(source);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 8;
        }
    }

    private static async Task<int> RunPsbBc3TestAsync(string source, string destination, string ratioText,
        PsbTextureFormat format = PsbTextureFormat.Dxt5)
    {
        try
        {
            if (!double.TryParse(ratioText, System.Globalization.CultureInfo.InvariantCulture, out double ratio)) return 12;
            if (File.Exists(destination)) return 13;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
            File.Copy(source, destination);
            PsbProcessingResult result = await new PsbProcessor().ProcessWithFormatAsync(destination, ratio, ratio, format);
            Console.WriteLine(result.Message);
            await new PsbProcessor().InspectAsync(destination);
            return result.IsEmoteMotion && result.Changed ? 0 : 14;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 15;
        }
    }

    private static async Task<int> RunPsbPfsSelfTestAsync(string source, string ratioText)
    {
        string root = Path.Combine(Path.GetTempPath(), "art3m1s-psb-pfs-selftest-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (!double.TryParse(ratioText, System.Globalization.CultureInfo.InvariantCulture, out double ratio)) return 9;
            string input = Path.Combine(root, "input"), output = Path.Combine(root, "output"), unpack = Path.Combine(root, "unpack");
            Directory.CreateDirectory(input);
            PfsCodec codec = new();
            await codec.PackPf8Async(new ExtractedArchive('8',
            [
                new PfsEntry(Encoding.UTF8.GetBytes("image/fg/sample.psb"), "image/fg/sample.psb", 0,
                    checked((uint)new FileInfo(source).Length), source)
            ]), Path.Combine(input, "root.pfs"));
            await new ConversionService().ConvertAsync(new ConversionOptions(input, output, ratio,
                AssetCategories.Animation));
            ExtractedArchive result = await codec.ExtractAsync(Path.Combine(output, "root.pfs"), unpack);
            string resized = result.Entries.Single().ExtractedPath;
            await new PsbProcessor().InspectAsync(resized);
            return new FileInfo(resized).Length < new FileInfo(source).Length ? 0 : 10;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 11;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
