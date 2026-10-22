using Art3m1s.PsvTool.Core;
using Xunit;

namespace Art3m1s.PsvTool.Core.Tests;

public sealed class TextureDiagnosticTests
{
    [Theory]
    [InlineData(4096, 4096, 4096, 4096, 0)]
    [InlineData(8192, 8, 4096, 4, 1)]
    [InlineData(8, 8192, 4, 4096, 1)]
    [InlineData(8194, 8, 4097, 4, 2)]
    [InlineData(8, 8194, 4, 4097, 2)]
    public void DimensionsDistinguishSourceFromOversizedOutput(int w, int h, int tw, int th, int count)
    {
        var diagnostics = TextureDimensionDiagnostics.Create(w, h, tw, th, "atlas0").ToArray();
        Assert.Equal(count, diagnostics.Length);
        if (count == 1) Assert.Equal(ConversionLogLevel.Info, diagnostics[0].Level);
        if (count == 2) Assert.Equal(ConversionLogLevel.Warning, diagnostics[1].Level);
    }

    [Fact]
    public async Task OversizedPsbReportsAtlasAndDimensionsBeforeSwizzleFailure()
    {
        string directory = Path.Combine(Path.GetTempPath(), "art3m1s-psb-diagnostic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "sample.psb");
            byte[] source = PsbFixture.Create((8194, 4));
            await File.WriteAllBytesAsync(path, source);
            List<ConversionDiagnostic> diagnostics = [];
            await Assert.ThrowsAsync<InvalidDataException>(() => new PsbProcessor().ProcessWithDiagnosticsAsync(path, .5, .5,
                PsbTextureFormat.Dxt5, diagnostics.Add, dxt5Layout: PsbDxt5Layout.Swizzled));
            var warning = Assert.Single(diagnostics, x => x.Code == "TARGET_DIMENSIONS_GT_4096");
            Assert.Contains("source=8194x4; target=4097x2", warning.Message);
            Assert.Contains("texture=", warning.Message);
            Assert.Equal(source, await File.ReadAllBytesAsync(path));
        }
        finally { Directory.Delete(directory, true); }
    }
}
