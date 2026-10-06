using System.Text.Json;
using Art3m1s.PsvTool.Core;
using Xunit;

namespace Art3m1s.PsvTool.Core.Tests;

public sealed class PsbRenderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "psb-render-" + Guid.NewGuid().ToString("N"));
    public PsbRenderTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData(.25, 16, 2)]
    [InlineData(.5, 32, 1)]
    [InlineData(1, 64, .5)]
    public async Task GeometryUsesGlobalRatioAndSamplingUsesTextureRatio(double textureRatio, int atlasWidth, double scale)
    {
        string input = Path.Combine(_root, "input"), output = Path.Combine(_root, "output");
        Directory.CreateDirectory(input);
        byte[] original = PsbFixture.Create((64, 32), (32, 16));
        await File.WriteAllBytesAsync(Path.Combine(input, "hero.psb"), original);
        await new PfsCodec().PackPf8Async(new('8', []), Path.Combine(input, "root.pfs"));
        await new ConversionService().ConvertAsync(new(input, output, Ratio: .5, Categories: AssetCategories.Animation,
            ConvertEmotePsbTexturesToDxt5: false, PsbTextures: new([new(64, 32, textureRatio)], Enabled: true)));
        byte[] converted = await File.ReadAllBytesAsync(Path.Combine(output, "hero.psb"));
        double? Number(params string[] path) => PsbProcessor.InspectNumericMetadata(converted, path);
        Assert.Equal(atlasWidth, Number("source", "atlas0", "texture", "width"));
        Assert.Equal(32 * textureRatio, Number("source", "atlas0", "icon", "piece0", "width"));
        Assert.Equal(8, Number("source", "atlas0", "icon", "piece0", "originX"));
        Assert.Equal(640, Number("screenSize", "width")); Assert.Equal(360, Number("screenSize", "height"));
        string[] frame = ["object", "hero", "motion", "idle", "layer", "0", "frameList", "0", "content"];
        Assert.Equal(50, Number([.. frame, "coord", "0"])); Assert.Equal(20, Number([.. frame, "coord", "1"]));
        Assert.Equal(25, Number([.. frame, "bounds", "right"]));
        string manifestJson = await File.ReadAllTextAsync(Path.Combine(output, PsbRenderManifest.FileName));
        var manifest = JsonSerializer.Deserialize(manifestJson,
            PsbRenderJsonContext.Default.PsbRenderManifest)!;
        var model = Assert.Single(manifest.Models);
        Assert.Equal("hero.psb", model.Path); Assert.Null(model.Archive);
        Assert.Equal(2, manifest.Version); Assert.Equal(.5, manifest.GeometryRatio);
        Assert.Equal(textureRatio, model.TextureRatio); Assert.Equal(scale, model.RenderScale);
        Assert.Equal(PsbRenderManifest.FingerprintBytes(converted), model.Fingerprint);
        // Even a multi-atlas model emits one flat record, with no per-icon/atlas data.
        using (JsonDocument json = JsonDocument.Parse(manifestJson))
        {
            Assert.Equal(new[] { "version", "geometryRatio", "models" }, json.RootElement.EnumerateObject().Select(x => x.Name));
            Assert.Equal(new[] { "path", "archive", "fingerprint", "textureRatio", "renderScale" },
                json.RootElement.GetProperty("models")[0].EnumerateObject().Select(x => x.Name));
        }
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(manifestJson) < 512);
        Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(input, "hero.psb")));
        await Assert.ThrowsAsync<InvalidDataException>(() => new ConversionService().ConvertAsync(new(output, Path.Combine(_root, "repeat"))));

        // Optional, deterministic cross-language fixture export for the GXM engine test.
        if (Environment.GetEnvironmentVariable("ART3M1S_PORT_TEST_DIR") is { Length: > 0 } export)
        {
            string destination = Path.Combine(export, textureRatio.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            Directory.CreateDirectory(destination);
            File.Copy(Path.Combine(output, "hero.psb"), Path.Combine(destination, "hero.psb"), true);
            File.Copy(Path.Combine(output, PsbRenderManifest.FileName), Path.Combine(destination, PsbRenderManifest.FileName), true);
        }
    }

    [Fact]
    public async Task ArchiveAndLooseManifestEntriesMatchFinalResources()
    {
        string input = Path.Combine(_root, "input"), output = Path.Combine(_root, "output"); Directory.CreateDirectory(input);
        string source = Path.Combine(_root, "source.psb"); await File.WriteAllBytesAsync(source, PsbFixture.Create((65, 33)));
        var codec = new PfsCodec();
        await codec.PackPf8Async(new('8', [new("image/hero.psb"u8.ToArray(), "image/hero.psb", 0, 0, source)]), Path.Combine(input, "root.pfs.010"));
        await new ConversionService().ConvertAsync(new(input, output, Ratio: .5, Categories: AssetCategories.Animation,
            ConvertEmotePsbTexturesToDxt5: false, PsbTextures: new([new(65, 33, .3)], Enabled: true)));
        var unpacked = await codec.ExtractAsync(Path.Combine(output, "root.pfs.010"), Path.Combine(_root, "unpack"));
        var entry = Assert.Single(unpacked.Entries);
        var manifest = JsonSerializer.Deserialize(await File.ReadAllTextAsync(Path.Combine(output, PsbRenderManifest.FileName)),
            PsbRenderJsonContext.Default.PsbRenderManifest)!;
        var model = Assert.Single(manifest.Models);
        Assert.Equal("root.pfs.010", model.Archive); Assert.Equal("image/hero.psb", model.Path);
        Assert.Equal(PsbRenderManifest.FingerprintBytes(await File.ReadAllBytesAsync(entry.ExtractedPath)), model.Fingerprint);
        Assert.Equal(2, manifest.Version); Assert.Equal(.5, manifest.GeometryRatio);
        Assert.Equal(.3, model.TextureRatio); Assert.Equal(.5 / .3, model.RenderScale);
        Assert.Equal(19, (await PsbTextureScanner.InspectFileAsync(entry.ExtractedPath)).Width);
    }

    [Fact]
    public void FingerprintAgreesWithEngineKnownVector() => Assert.Equal("a430d84680aabd0b", PsbRenderManifest.FingerprintBytes("hello"u8));

    [Fact]
    public async Task IndependentDownsamplingKeepsPositiveTinySamplingRectangles()
    {
        string path = Path.Combine(_root, "tiny.psb"); await File.WriteAllBytesAsync(path, PsbFixture.Create((2, 2)));
        var result = await new PsbProcessor().ProcessWithRatiosAsync(path, .25, 1, false);
        Assert.True(result.IsEmoteMotion); Assert.True(result.Changed);
        Assert.Equal(1, PsbProcessor.InspectNumericMetadata(await File.ReadAllBytesAsync(path), "source", "atlas0", "icon", "piece0", "width"));
    }

    [Theory]
    [InlineData(false, true, true, 32, false)]
    [InlineData(true, false, true, 16, false)]
    [InlineData(true, true, true, 16, true)]
    [InlineData(true, true, false, 64, false)]
    public async Task CompensationOnlyAppliesToEnabledIndependentAnimation(bool independent, bool compensate,
        bool animation, int width, bool hasConfig)
    {
        string input = Path.Combine(_root, "input"), output = Path.Combine(_root, "output"); Directory.CreateDirectory(input);
        await File.WriteAllBytesAsync(Path.Combine(input, "hero.psb"), PsbFixture.Create((64, 32)));
        await new PfsCodec().PackPf8Async(new('8', []), Path.Combine(input, "root.pfs"));
        await new ConversionService().ConvertAsync(new(input, output, Ratio: .5,
            Categories: animation ? AssetCategories.Animation : AssetCategories.None,
            PsbTextures: new([new(64, 32, .25)], independent, compensate)));
        Assert.Equal(width, (await PsbTextureScanner.InspectFileAsync(Path.Combine(output, "hero.psb"))).Width);
        Assert.Equal(hasConfig, File.Exists(Path.Combine(output, PsbRenderManifest.FileName)));
        Assert.False(new PsbTextureOptions().Enabled); Assert.True(new PsbTextureOptions().CompensateRendering);
    }

    public void Dispose() => Directory.Delete(_root, true);
}
