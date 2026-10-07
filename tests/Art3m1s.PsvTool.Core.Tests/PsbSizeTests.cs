using System.Text;
using Art3m1s.PsvTool.Core;
using Xunit;

namespace Art3m1s.PsvTool.Core.Tests;

public sealed class PsbSizeTests
{
    [Theory]
    [InlineData(PsbTextureFormat.Dxt5, PsbDxt5Layout.Swizzled, .5, 2129920)]
    [InlineData(PsbTextureFormat.Dxt5, PsbDxt5Layout.Swizzled, .75, 8519680)]
    [InlineData(PsbTextureFormat.Dxt5, PsbDxt5Layout.Linear, .75, 4792320)]
    [InlineData(PsbTextureFormat.Pvrtc2_4, PsbDxt5Layout.Swizzled, .5, 1064960)]
    [InlineData(PsbTextureFormat.Pvrtc2_2, PsbDxt5Layout.Swizzled, .5, 532480)]
    public void CountsAllAtlasesAndMatchesFormatPadding(PsbTextureFormat format, PsbDxt5Layout layout, double ratio, long bytes)
    {
        var file = File(new("atlas0", 4096, 2048, "RGBA8"), new("atlas1", 512, 256, "RGBA8"));
        var result = PsbTextureSizeEstimator.Estimate([file], ratio, format, layout);
        Assert.Equal(1, result.EstimatedFileCount); Assert.Empty(result.Issues); Assert.Equal(2, result.Textures.Count);
        Assert.Equal(34078720, result.SourceBytes); Assert.Equal(bytes, result.TargetBytes);
        Assert.Equal((int)(4096 * ratio), result.Textures[0].TargetWidth);
        Assert.Equal((int)(2048 * ratio), result.Textures[0].TargetHeight);
    }

    [Theory]
    [InlineData("DXT5", 1, 1, 16)]
    [InlineData("DXT5_SWIZZLED", 17, 11, 512)]
    [InlineData("DXT5", 17, 11, 240)]
    [InlineData("PVRTC2_4BPP", 1, 1, 8)]
    [InlineData("PVRTC2_2BPP", 1, 1, 8)]
    [InlineData("PVRTC2_2BPP", 17, 11, 72)]
    public void PayloadIncludesTinyBlocksAndNonPowerOfTwoPadding(string type, int width, int height, long bytes)
        => Assert.Equal(bytes, PsbTextureSizeEstimator.PayloadBytes(type, width, height));

    [Fact]
    public void SharedReferencesAreDeduplicatedOnlyWithinTheirFileAndResourceTable()
    {
        var file = File(new("atlas0", 32, 16, "RGBA8", 0), new("alias", 32, 16, "RGBA8", 0),
            new("extra", 32, 16, "RGBA8", 0, true), new("other", 8, 8, "RGBA8", 1));
        var result = PsbTextureSizeEstimator.Estimate([file, file with { Archive = "root.pfs.010" }], 1, PsbTextureFormat.Dxt5);
        Assert.Equal(6, result.Textures.Count); Assert.Equal(8704, result.SourceBytes); Assert.Equal(2176, result.TargetBytes);
    }

    [Fact]
    public void MasterOffKeepsEachOriginalFormatAndSwizzledLayout()
    {
        var result = PsbTextureSizeEstimator.Estimate([File(new("a", 34, 22, "DXT5_SWIZZLED"),
            new("b", 34, 22, "RGBA8"), new("c", 34, 22, "PVRTC2_2BPP"))], .5, null, PsbDxt5Layout.Linear);
        Assert.Equal(new[] { "DXT5_SWIZZLED", "RGBA8", "PVRTC2_2BPP" }, result.Textures.Select(x => x.TargetFormat));
        Assert.Equal(512 + 748 + 72, result.TargetBytes);
    }

    [Fact]
    public void UnsupportedAtlasExcludesWholeFileAndErrorsCannotLookLikeZeroSizedSuccess()
    {
        var files = new[] { File(new("a", 32, 16, "RGBA8"), new("b", 32, 16, "ASTC")),
            new PsbTextureFileInfo("root.pfs", "broken.psb", 100, null, "bad header"),
            new PsbTextureFileInfo(null, "other.psb", 100, new(false, [])),
            File(new PsbAtlasInfo("bad", 8192, 2048, "RGBA8")) };
        var result = PsbTextureSizeEstimator.Estimate(files, 1, PsbTextureFormat.Dxt5);
        Assert.Empty(result.Textures); Assert.Equal(0, result.EstimatedFileCount); Assert.Equal(4, result.FileCount);
        Assert.Equal(new[] { PsbSizeIssueKind.UnsupportedFormat, PsbSizeIssueKind.ScanError, PsbSizeIssueKind.NoAtlas, PsbSizeIssueKind.InvalidSize },
            result.Issues.Select(x => x.Kind));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidRatioIsRejected(double ratio)
        => Assert.Throws<ArgumentOutOfRangeException>(() => PsbTextureSizeEstimator.Estimate([], ratio, PsbTextureFormat.Dxt5));

    [Theory]
    [InlineData(PsbTextureFormat.Dxt5, PsbDxt5Layout.Linear)]
    [InlineData(PsbTextureFormat.Dxt5, PsbDxt5Layout.Swizzled)]
    [InlineData(PsbTextureFormat.Pvrtc2_4, PsbDxt5Layout.Swizzled)]
    [InlineData(PsbTextureFormat.Pvrtc2_2, PsbDxt5Layout.Swizzled)]
    public async Task MetadataScanForLooseAndPfsMatchesActualEncodedPayloads(PsbTextureFormat format, PsbDxt5Layout layout)
    {
        string root = Path.Combine(Path.GetTempPath(), "psb-size-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string input = Path.Combine(root, "input"); Directory.CreateDirectory(input);
            string source = Path.Combine(input, "hero.psb"); byte[] original = PsbFixture.Create((34, 22), (7, 3));
            await System.IO.File.WriteAllBytesAsync(source, original);
            var codec = new PfsCodec();
            await codec.PackPf8Async(new('8', [new(Encoding.UTF8.GetBytes("image/hero.psb"), "image/hero.psb", 0,
                (uint)original.Length, source)]), Path.Combine(input, "root.pfs.010"));
            var files = await new PsbTextureScanner().ScanAsync(input);
            Assert.Equal(2, files.Count);
            Assert.All(files, file => Assert.Equal(new int?[] { 0, 1 }, file.Inspection!.Atlases.Select(x => x.ResourceIndex)));
            var result = PsbTextureSizeEstimator.Estimate(files, .5, format, layout);
            Assert.Equal(4, result.Textures.Count); Assert.Equal(original, await System.IO.File.ReadAllBytesAsync(source));
            string converted = Path.Combine(root, "converted.psb"); await System.IO.File.WriteAllBytesAsync(converted, original);
            await new PsbProcessor().ProcessWithFormatAsync(converted, .5, .5, format, dxt5Layout: layout);
            byte[] bytes = await System.IO.File.ReadAllBytesAsync(converted);
            long payload = (await PsbTextureScanner.InspectFileAsync(converted)).Atlases.Sum(atlas =>
                (long)PsbProcessor.InspectTextureResource(bytes, atlas.Source).Length);
            Assert.Equal(2 * payload, result.TargetBytes);
        }
        finally { Directory.Delete(root, true); }
    }

    private static PsbTextureFileInfo File(params PsbAtlasInfo[] atlases) => new(null, "hero.psb", 100, new(true, atlases));
}
