using Art3m1s.PsvTool.App;
using Art3m1s.PsvTool.Core;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Art3m1s.PsvTool.Ui.Tests;

public sealed class TextureUiTests
{
    [Fact]
    public void TextureTitleRecommendsConversionWithoutEnablingMasterSwitch()
    {
        var vm = new MainViewModel(settings: new MemoryStore());
        Assert.Equal("图片压制为 PSV 专用纹理（建议转换）", vm.NativeTexturesLabel);
        Assert.False(vm.NativeTextures);
        vm.SetLanguage("en-US");
        Assert.Equal("Convert images to PSV textures (recommended)", vm.NativeTexturesLabel);
        Assert.False(vm.NativeTextures);
    }

    [Fact]
    public void SwizzledAndLinearChoicesHaveStableEnumMappingAndSwizzledPriority()
    {
        Assert.Equal(4, (int)NativeTextureFormat.Bc3);
        Assert.Equal(16, (int)NativeTextureFormat.AutoWithoutMetadata);
        var vm = new TextureCategoryViewModel([Sample("image/bg", alpha: true)], 1, false, false);
        Assert.Equal(NativeTextureFormat.Auto, vm.Rule.Format);
        Assert.Contains("Swizzled", vm.Choices[vm.SelectedFormat]);
        foreach (var (preferred, linear) in new[] { (NativeTextureFormat.Auto, NativeTextureFormat.AutoLinear),
            (NativeTextureFormat.AutoWithoutMetadata, NativeTextureFormat.AutoWithoutMetadataLinear),
            (NativeTextureFormat.Bc1Swizzled, NativeTextureFormat.Bc1),
            (NativeTextureFormat.Bc3Swizzled, NativeTextureFormat.Bc3) })
        {
            Assert.True(NativeTextureFormats.IndexOf(preferred) < NativeTextureFormats.IndexOf(linear));
            Assert.Contains("Swizzled", vm.Choices[NativeTextureFormats.IndexOf(preferred)]);
            Assert.Contains("Linear", vm.Choices[NativeTextureFormats.IndexOf(linear)]);
            vm.SelectedFormat = NativeTextureFormats.IndexOf(linear);
            vm.Refresh(.5, false, true);
            Assert.Equal(linear, vm.Rule.Format);
            Assert.Contains("Linear", vm.Choices[vm.SelectedFormat]);
            vm.SelectedFormat = NativeTextureFormats.IndexOf(preferred);
            Assert.Equal(preferred, vm.Rule.Format);
        }
        Assert.Equal(NativeTextureFormats.All.Count, Enum.GetValues<NativeTextureFormat>().Length);
        for (int i = 0; i < NativeTextureFormats.All.Count; i++)
            Assert.Equal(i, NativeTextureFormats.IndexOf(NativeTextureFormats.All[i].Format));
    }
    private static TextureImageInfo Sample(string category, bool gray = false, bool alpha = false) =>
        new(null, category + "/test.png", category, 960, 540, 1024, gray, alpha, alpha, false);
    [Theory]
    [InlineData("image/bg")]
    [InlineData("image/fg")]
    [InlineData("image/cg")]
    [InlineData("image/event")]
    [InlineData("backgrounds")]
    [InlineData("characters")]
    public void DefaultsSelectSuitableAutoDetectedCategories(string category)
    {
        foreach (bool alpha in new[] { false, true })
        {
            var vm = new TextureCategoryViewModel([Sample(category, alpha: alpha)], .5, false, false);
            Assert.True(vm.Enabled);
            Assert.Equal(NativeTextureFormat.Auto, vm.Rule.Format);
            Assert.Equal(alpha ? NativeTextureFormat.Bc3Swizzled : NativeTextureFormat.Bc1Swizzled,
                NativeTextureFormats.Resolve(vm.Images[0], vm.Rule.Format, false));
        }
        Assert.False(new AppSettings().NativeTextures);
    }

    [Fact]
    public void AutoSelectionKeepsProtectedUnsuitableAndConflictingImagesUnchecked()
    {
        var color = Sample("image/fg");
        foreach (var image in new[]
        {
            Sample("image/bg", gray: true), Sample("system"), Sample("image/ui"), Sample("unknown"),
            color with { HasMetadata = true }, color with { IsNative = true }, color with { Error = "Invalid image" },
            color with { Width = 10000 }, color with { HasStemConflict = true },
            color with { ExistingPaths = new HashSet<string>(["image/fg/test.dds"], StringComparer.OrdinalIgnoreCase) },
            color with { OverlayPeers = [color with { HasMetadata = true }] }
        })
            Assert.False(new TextureCategoryViewModel([image], 1, false, false).Enabled);
        // A mixed category is selected only for its suitable images; protected images stay PNG.
        var mixed = new TextureCategoryViewModel([color, color with { HasMetadata = true }], 1, false, false);
        Assert.True(mixed.Enabled);
        Assert.Contains("可转换 1 张", mixed.Summary);
        var resized = new TextureCategoryViewModel([color with { Width = 6000 }], .5, false, false);
        Assert.True(resized.Enabled);
    }

    [Fact]
    public async Task ScanSelectsDetectedLooseAndArchivedCategoriesAndPreservesManualChoicesOnRescan()
    {
        string root = Path.Combine(Path.GetTempPath(), "texture-scan-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            async Task WriteImage(string category, bool gray = false)
            {
                string path = Path.Combine(root, category, "test.png");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (gray)
                {
                    using var image = new Image<L8>(8, 8, new L8(128));
                    await image.SaveAsPngAsync(path);
                }
                else
                {
                    using var image = new Image<Rgba32>(8, 8, new Rgba32(180, 90, 40, 255));
                    await image.SaveAsPngAsync(path);
                }
            }
            await WriteImage("image/bg");
            await WriteImage("image/fg");
            await WriteImage("image/ui");
            await WriteImage("image/mask", gray: true);
            string source = Path.Combine(root, "image/bg/test.png");
            var codec = new PfsCodec();
            await codec.PackPf8Async(new('8', [new(Encoding.UTF8.GetBytes("image/cg/test.png"),
                "image/cg/test.png", 0, (uint)new FileInfo(source).Length, source)]), Path.Combine(root, "root.pfs.010"));
            var vm = new MainViewModel(settings: new MemoryStore()) { InputDirectory = root };
            await vm.ScanTexturesAsync();
            Assert.Equal(5, vm.TextureCategories.Count);
            Assert.False(vm.IsBusy);
            Assert.Equal(100, vm.Progress);
            Assert.False(vm.NativeTextures);
            foreach (string category in new[] { "image/bg", "image/fg", "image/cg" })
            {
                var item = Assert.Single(vm.TextureCategories, item => item.Key == category);
                Assert.True(item.Enabled);
                Assert.Equal(NativeTextureFormat.Auto, item.Rule.Format);
            }
            Assert.False(Assert.Single(vm.TextureCategories, item => item.Key == "image/ui").Enabled);
            Assert.False(Assert.Single(vm.TextureCategories, item => item.Key == "image/mask#gray").Enabled);
            Assert.Equal("root.pfs.010", Assert.Single(Assert.Single(vm.TextureCategories,
                item => item.Key == "image/cg").Images).Archive);
            Assert.Single(vm.TextureCategories, item => item.Key == "image/fg").Enabled = false;
            Assert.Single(vm.TextureCategories, item => item.Key == "image/bg").SelectedFormat = NativeTextureFormats.IndexOf(NativeTextureFormat.Bc3);
            await WriteImage("image/event");
            await vm.ScanTexturesAsync();
            Assert.Equal(6, vm.TextureCategories.Count);
            Assert.False(Assert.Single(vm.TextureCategories, item => item.Key == "image/fg").Enabled);
            Assert.Equal(NativeTextureFormat.Bc3, Assert.Single(vm.TextureCategories, item => item.Key == "image/bg").Rule.Format);
            Assert.True(Assert.Single(vm.TextureCategories, item => item.Key == "image/event").Enabled);
            Assert.False(vm.NativeTextures);
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class MemoryStore : ISettingsStore
    {
        public AppSettings Load() => new();
        public void Save(AppSettings settings) { }
    }
    [Fact]
    public void ManualAutoShowsFormatsAndCountsWithoutChangingDefaults()
    {
        var vm = new TextureCategoryViewModel([
            Sample("system"), Sample("system", alpha: true),
            Sample("system") with { HasMetadata = true }, Sample("system", gray: true)
        ], 1, false, false);
        Assert.False(vm.Enabled);
        Assert.Equal(NativeTextureFormat.Preserve, vm.Rule.Format);
        var label = vm.Choices[NativeTextureFormats.IndexOf(NativeTextureFormat.AutoWithoutMetadata)];
        Assert.Contains("除带偏移信息外的 AUTO 转换（仅手动）", label);
        Assert.Contains("BC1 / DXT1 · Swizzled × 1", label);
        Assert.Contains("BC3 / DXT5 · Swizzled × 1", label);
        Assert.Contains("保留原格式 × 2", label);
        Assert.DoesNotContain("推荐", label);
        vm.SelectedFormat = NativeTextureFormats.IndexOf(NativeTextureFormat.AutoWithoutMetadata);
        Assert.Contains("可转换 0 张", vm.Summary);
        vm.Enabled = true;
        Assert.Contains("可转换 2 张", vm.Summary);
        vm.DetailsExpanded = true;
        Assert.Contains("附加信息", vm.FileRows[2]);
        Assert.Equal(NativeTextureFormat.Auto, new TextureCategoryViewModel([Sample("image/bg")], 1, false, false).Rule.Format);
    }
    [Fact]
    public void DropdownReasonsChangeWithAlphaAndOutputDimensions()
    {
        var vm = new TextureCategoryViewModel([Sample("image/bg", alpha: true)], 1, false, false);
        Assert.Contains("不适合", vm.Choices[NativeTextureFormats.IndexOf(NativeTextureFormat.Bc1)]);
        Assert.Contains("不能保留透明度", vm.Choices[NativeTextureFormats.IndexOf(NativeTextureFormat.Etc1)]);
        Assert.Contains("尺寸不是二次幂", vm.Choices[NativeTextureFormats.IndexOf(NativeTextureFormat.PvrtcRgb4)]);
        vm.Refresh(1, true, false);
        Assert.DoesNotContain("不适合", vm.Choices[NativeTextureFormats.IndexOf(NativeTextureFormat.Bc1)]);
        vm.SelectedFormat = NativeTextureFormats.IndexOf(NativeTextureFormat.Bc3);
        Assert.Equal(NativeTextureFormat.Bc3, vm.Rule.Format);
        vm.Refresh(1, false, true);
        Assert.Contains("unsuitable", vm.Choices[NativeTextureFormats.IndexOf(NativeTextureFormat.Bc1)]);
    }
    [Fact]
    public void CachedTextureLabelsRefreshAndDetailsStayLazy()
    {
        var vm = new TextureCategoryViewModel([Sample("image/bg", alpha: true)], 1, false, false);
        var choices = vm.Choices;
        var stableItems = vm.FormatItems;
        var summary = vm.Summary;
        Assert.Same(choices, vm.Choices);
        Assert.Same(summary, vm.Summary);
        Assert.Empty(vm.FileRows);
        vm.DetailsExpanded = true;
        var rows = vm.FileRows;
        Assert.Single(rows);
        Assert.Same(rows, vm.FileRows);
        vm.SelectedFormat = NativeTextureFormats.IndexOf(NativeTextureFormat.Bc1);
        Assert.Contains("半透明", vm.FileRows[0]);
        vm.Refresh(.5, true, true);
        Assert.Same(stableItems, vm.FormatItems);
        Assert.Equal(vm.Choices, vm.FormatItems.Select(item => item.Label));
        Assert.NotSame(choices, vm.Choices);
        Assert.Contains("BC1 / DXT1", vm.Choices[NativeTextureFormats.IndexOf(NativeTextureFormat.Auto)]);
        Assert.DoesNotContain("Contains smooth alpha", vm.FileRows[0]);
        Assert.NotEqual(summary, vm.Summary);
        vm.DetailsExpanded = false;
        Assert.Empty(vm.FileRows);
    }
}
