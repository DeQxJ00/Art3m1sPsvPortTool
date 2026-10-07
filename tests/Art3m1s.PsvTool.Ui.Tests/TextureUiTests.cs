using Art3m1s.PsvTool.App;
using Art3m1s.PsvTool.Core;
using Xunit;

namespace Art3m1s.PsvTool.Ui.Tests;

public sealed class TextureUiTests
{
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
    [Fact]
    public void DefaultsOnlySelectColorBackgrounds()
    {
        Assert.True(new TextureCategoryViewModel([Sample("image/bg")], 1, false, false).Enabled);
        Assert.False(new TextureCategoryViewModel([Sample("image/bg", gray: true)], 1, false, false).Enabled);
        Assert.False(new TextureCategoryViewModel([Sample("image/fg")], 1, false, false).Enabled);
        Assert.False(new AppSettings().NativeTextures);
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
