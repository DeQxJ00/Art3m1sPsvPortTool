using System.Text;
using Art3m1s.PsvTool.App;
using Art3m1s.PsvTool.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Art3m1s.PsvTool.Ui.Tests;

public sealed class TextureCategoryDepthUiTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "texture-depth-ui-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void AutomaticModeIsDefaultAndManualDepthPersistsAndIsLocalized()
    {
        string settingsPath = Path.Combine(root, "settings.json");
        var store = new LocalSettingsStore(settingsPath);
        var vm = new MainViewModel(settings: store);
        Assert.Equal(0, vm.TextureCategoryDepth);
        Assert.Equal(0, ConversionCommandLineOptions.Parse(["--convert", "input", "output"]).TextureCategoryDepth);
        Assert.Equal("图片分类方式", vm.TextureCategoryDepthLabel);
        Assert.Equal(9, vm.TextureCategoryDepthChoices.Count);
        Assert.Contains("自动", vm.TextureCategoryDepthChoices[0]);
        vm.TextureCategoryDepth = 3;
        Assert.Equal(3, new MainViewModel(settings: store).TextureCategoryDepth);
        vm.SetLanguage("en-US");
        Assert.Contains("classification", vm.TextureCategoryDepthLabel);
        Assert.Contains("Automatic", vm.TextureCategoryDepthChoices[0]);
        Assert.Contains("Manual: 3", vm.TextureCategoryDepthChoices[3]);
        Assert.Contains("image/hd/bg", vm.TextureCategoryDepthHelp);
        vm.TextureCategoryDepth = 0;
        Assert.Equal(0, new MainViewModel(settings: store).TextureCategoryDepth);
    }

    [Fact]
    public async Task TogglingDepthClearsOldRulesAndRescanSelectsBgInsteadOfMergedHd()
    {
        string input = Path.Combine(root, "input");
        Directory.CreateDirectory(input);
        string jpeg = Path.Combine(root, "source.jpg");
        using (var image = new Image<Rgb24>(32, 16, new Rgb24(40, 100, 180)))
            await image.SaveAsJpegAsync(jpeg);
        const string name = "image/hd/bg/sample.jpg";
        await new PfsCodec().PackPf8Async(new('8', [new(Encoding.UTF8.GetBytes(name), name, 0, 0, jpeg)]), Path.Combine(input, "root.pfs"));
        var vm = new MainViewModel(settings: new LocalSettingsStore(Path.Combine(root, "settings.json")))
        { InputDirectory = input, OutputDirectory = Path.Combine(root, "output") };
        vm.TextureCategoryDepth = 2;
        await vm.ScanTexturesAsync();
        Assert.Equal("image/hd", Assert.Single(vm.TextureCategories).Key);
        Assert.False(Assert.Single(vm.TextureCategories).Enabled);
        vm.TextureCategoryDepth = 0;
        Assert.Empty(vm.TextureCategories);
        await vm.ScanTexturesAsync();
        var category = Assert.Single(vm.TextureCategories);
        Assert.Equal("image/hd/bg", category.Key);
        Assert.True(category.Enabled);
        Assert.Equal(NativeTextureFormat.Auto, category.Rule.Format);
        vm.TextureCategoryDepth = 3;
        Assert.Empty(vm.TextureCategories);
    }

    [Theory]
    [InlineData("auto", 0)]
    [InlineData("1", 1)]
    [InlineData("3", 3)]
    [InlineData("8", 8)]
    public void CommandLineParsesCategoryMode(string value, int expected)
    {
        var options = ConversionCommandLineOptions.Parse(["--convert", "input", "output", "--texture-category-depth", value]);
        Assert.Equal(expected, options.TextureCategoryDepth);
        Assert.Contains("--texture-category-depth", ConversionCommandLine.Help);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("9")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("true")]
    public void InvalidCommandLineDepthIsRejected(string value) =>
        Assert.Throws<ArgumentException>(() => ConversionCommandLineOptions.Parse(["--convert", "input", "output", "--texture-category-depth", value]));

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"TextureCategoryDepth\":999}")]
    [InlineData("{\"TextureExtraCategoryLevel\":true}")]
    public void MissingOrInvalidSavedModeFallsBackToAutomatic(string json)
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "settings.json");
        File.WriteAllText(path, json);
        Assert.Equal(0, new MainViewModel(settings: new LocalSettingsStore(path)).TextureCategoryDepth);
    }

    [Theory]
    [InlineData("auto", 0)]
    [InlineData("3", 3)]
    public async Task CommandLineScanAndConverterReceiveSameMode(string mode, int depth)
    {
        string input = Path.Combine(root, "input");
        Directory.CreateDirectory(input);
        string jpeg = Path.Combine(root, "source.jpg");
        using (var image = new Image<Rgb24>(32, 16, new Rgb24(40, 100, 180)))
            await image.SaveAsJpegAsync(jpeg);
        const string name = "image/hd/bg/sample.jpg";
        await new PfsCodec().PackPf8Async(new('8', [new(Encoding.UTF8.GetBytes(name), name, 0, 0, jpeg)]), Path.Combine(input, "root.pfs"));
        var capture = new CapturingConverter();
        int result = await ConversionCommandLine.RunAsync(["--convert", input, Path.Combine(root, "output"),
            "--ratio", "0.5", "--types", "images", "--texture-category-depth", mode],
            standardOutput: TextWriter.Null, standardError: TextWriter.Null, converter: capture);
        Assert.Equal(0, result);
        Assert.NotNull(capture.Options);
        Assert.Equal(depth, capture.Options.NativeTextures!.CategoryDepth);
        var rule = Assert.Single(capture.Options.NativeTextures.Rules!);
        Assert.Equal("image/hd/bg", rule.Category);
        Assert.True(rule.Enabled);
    }

    private sealed class CapturingConverter : IConversionService
    {
        public ConversionOptions? Options { get; private set; }
        public Task ConvertAsync(ConversionOptions options, IProgress<ConversionProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Options = options;
            return Task.CompletedTask;
        }
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
