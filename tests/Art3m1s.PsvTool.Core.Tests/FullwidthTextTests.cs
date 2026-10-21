using System.Text;
using Art3m1s.PsvTool.Core;
using Xunit;

namespace Art3m1s.PsvTool.Core.Tests;

public sealed class FullwidthTextTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "fullwidth-text-" + Guid.NewGuid().ToString("N"));
    public FullwidthTextTests() => Directory.CreateDirectory(root);

    [Theory]
    [InlineData(".ast", false)]
    [InlineData(".ast", true)]
    [InlineData(".tbl", false)]
    [InlineData(".tbl", true)]
    [InlineData(".lua", false)]
    [InlineData(".lua", true)]
    public async Task QuotedCoordinatesScaleAsciiPrefixAndPreserveFullwidthSuffix(string extension, bool cp932)
    {
        Encoding encoding = EncodingFor(cp932);
        foreach (string newline in new[] { "\n", "\r\n" })
            foreach (string value in new[] { "960４", "-640３", "１２３" })
            {
                string source = $"-- sample01{newline}tag{{x=\"{value}\",y=720,note=\"テスト１２３\"}}{newline}";
                string path = Path.Combine(root, "sample01" + extension);
                await File.WriteAllBytesAsync(path, Bytes(encoding, source));
                var lines = await new ArtemisTextProcessor().ProcessWithDiagnosticsAsync(path, .5);
                Assert.Equal(new[] { 2 }, lines);
                string expected = source.Replace("x=\"960４\"", "x=\"480４\"", StringComparison.Ordinal)
                    .Replace("x=\"-640３\"", "x=\"-320３\"", StringComparison.Ordinal).Replace("y=720", "y=360", StringComparison.Ordinal);
                Assert.Equal(Bytes(encoding, expected), await File.ReadAllBytesAsync(path));
            }
    }

    [Theory]
    [InlineData(".ast", false)]
    [InlineData(".ast", true)]
    [InlineData(".tbl", false)]
    [InlineData(".tbl", true)]
    [InlineData(".lua", false)]
    [InlineData(".lua", true)]
    [InlineData(".ipt", false)]
    [InlineData(".ipt", true)]
    public async Task BareCoordinatesScaleAsciiPrefixAndPreserveFullwidthSuffix(string extension, bool cp932)
    {
        Encoding encoding = EncodingFor(cp932);
        const string source = "-- sample02\r\ntag{x=960４,y=720};tag{x=-640３,y=720};tag{x=１２３,y=720}\r\n";
        string path = Path.Combine(root, "sample02" + extension);
        await File.WriteAllBytesAsync(path, Bytes(encoding, source));
        var lines = await new ArtemisTextProcessor().ProcessWithDiagnosticsAsync(path, .5);
        Assert.Equal(new[] { 2 }, lines);
        string expected = source.Replace("x=960４", "x=480４", StringComparison.Ordinal)
            .Replace("x=-640３", "x=-320３", StringComparison.Ordinal).Replace("y=720", "y=360", StringComparison.Ordinal);
        Assert.Equal(Bytes(encoding, expected), await File.ReadAllBytesAsync(path));
    }

    [Theory]
    [InlineData(".tbl", "{clip='10,20,960４,40',x=100}", "{clip='5,10,480４,20',x=50}")]
    [InlineData(".ipt", "{['piece']='10,20,-640３,40',x=100}", "{['piece']='5,10,-320３,20',x=50}")]
    [InlineData(".lua", "tag{x='10,20,１２３,40',y=100}", "tag{x='5,10,１２３,20',y=50}")]
    [InlineData(".ast", "tag{x=' 960.5４, -640.5３ ',y=100}", "tag{x=' 480４, -320３ ',y=50}")]
    public async Task CoordinateListsScaleKnownPrefixesWithoutChangingFullwidthDigits(string extension, string source, string expected)
    {
        string path = Path.Combine(root, "sample03" + extension);
        await File.WriteAllTextAsync(path, source, new UTF8Encoding(false));
        var lines = await new ArtemisTextProcessor().ProcessWithDiagnosticsAsync(path, .5);
        Assert.Equal(new[] { 1 }, lines);
        Assert.Equal(expected, await File.ReadAllTextAsync(path));
    }

    [Theory]
    [InlineData(.5, "480４", "-320３")]
    [InlineData(.75, "720４", "-480３")]
    [InlineData(1, "960４", "-640３")]
    public async Task PrefixesFollowGlobalRatioWhileDialogueAndSuffixesStayOriginal(double ratio, string x, string x2)
    {
        string path = Path.Combine(root, "sample06.ast");
        await File.WriteAllTextAsync(path, "tag{x=\"960４\",x2=\"-640３\",note=\"960４ -640３ １２３\"}");
        await new ArtemisTextProcessor().ProcessAsync(path, ratio);
        Assert.Equal($"tag{{x=\"{x}\",x2=\"{x2}\",note=\"960４ -640３ １２３\"}}", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task NumericTuplesAndMulposCallsKeepTrailingFullwidthDigits()
    {
        string path = Path.Combine(root, "sample07.tbl");
        await File.WriteAllTextAsync(path, "fontsize={20,960４,-640３}");
        await new ArtemisTextProcessor().ProcessAsync(path, .5);
        Assert.Equal("fontsize={10,480４,-320３}", await File.ReadAllTextAsync(path));
        path = Path.Combine(root, "sample07.lua");
        await File.WriteAllTextAsync(path, "local a=mulpos(960４);local b=mulpos(-640３);local c=mulpos(１２３)");
        await new ArtemisTextProcessor().ProcessAsync(path, .5);
        Assert.Equal("local a=mulpos(480４);local b=mulpos(-320３);local c=mulpos(１２３)", await File.ReadAllTextAsync(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IniScalesOnlyAsciiPrefixesAndUnificationRetainsSuffixes(bool cp932)
    {
        Encoding encoding = EncodingFor(cp932);
        const string source = "[WINDOWS]\r\nWIDTH=1920４ ; テスト\r\nHEIGHT=1080３\r\n";
        string path = Path.Combine(root, "sample08.ini");
        await File.WriteAllBytesAsync(path, Bytes(encoding, source));
        var lines = await new ArtemisTextProcessor().ProcessWithDiagnosticsAsync(path, .5);
        Assert.Equal(new[] { 2, 3 }, lines);
        await PlatformIniResolution.ApplyAsync(path, 960, 540, default);
        string expected = source.Replace("1920４", "960４", StringComparison.Ordinal).Replace("1080３", "540３", StringComparison.Ordinal);
        Assert.Equal(Bytes(encoding, expected), await File.ReadAllBytesAsync(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IniFullwidthDimensionsSurviveBothScalingAndPlatformUnification(bool cp932)
    {
        Encoding encoding = EncodingFor(cp932);
        const string source = "[WINDOWS]\r\nWIDTH=１９２０ ; テスト\r\nHEIGHT=1080\r\n[ANDROID]\r\nWIDTH=1280\r\nHEIGHT=720\r\n";
        string path = Path.Combine(root, "sample04.ini");
        await File.WriteAllBytesAsync(path, Bytes(encoding, source));
        var lines = await new ArtemisTextProcessor().ProcessWithDiagnosticsAsync(path, .5);
        Assert.Equal(new[] { 2 }, lines);
        await PlatformIniResolution.ApplyAsync(path, 960, 540, default);
        string expected = source.Replace("HEIGHT=1080", "HEIGHT=540", StringComparison.Ordinal)
            .Replace("WIDTH=1280", "WIDTH=960", StringComparison.Ordinal).Replace("HEIGHT=720", "HEIGHT=540", StringComparison.Ordinal);
        Assert.Equal(Bytes(encoding, expected), await File.ReadAllBytesAsync(path));
        await PlatformIniResolution.ApplyAsync(path, 960, 540, default);
        Assert.Equal(Bytes(encoding, expected), await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task FullwidthValuesDoNotAbortArchiveOrLooseConversion()
    {
        string input = Path.Combine(root, "input"), output = Path.Combine(root, "output");
        Directory.CreateDirectory(Path.Combine(input, "script"));
        const string script = "tag{x=\"960４\",x2=\"-640３\",y=720}\r\n";
        const string ini = "[WINDOWS]\nWIDTH=1920\nHEIGHT=1080\n[ANDROID]\nWIDTH=１２８０\nHEIGHT=720\n";
        string scriptPath = Path.Combine(input, "script", "sample05.ast"), iniPath = Path.Combine(input, "system.ini");
        await File.WriteAllBytesAsync(scriptPath, Encoding.UTF8.GetBytes(script));
        await File.WriteAllBytesAsync(iniPath, Encoding.UTF8.GetBytes(ini));
        var codec = new PfsCodec();
        string archive = Path.Combine(input, "root.pfs");
        await codec.PackPf8Async(new('8', [
            new(Encoding.UTF8.GetBytes("system.ini"), "system.ini", 0, (uint)new FileInfo(iniPath).Length, iniPath),
            new(Encoding.UTF8.GetBytes("script/sample05.ast"), "script/sample05.ast", 0, (uint)new FileInfo(scriptPath).Length, scriptPath)
        ]), archive);
        byte[] originalArchive = await File.ReadAllBytesAsync(archive);
        await new ConversionService().ConvertAsync(new(input, output, Ratio: .5, Categories: AssetCategories.Text));
        string expected = script.Replace("960４", "480４", StringComparison.Ordinal)
            .Replace("-640３", "-320３", StringComparison.Ordinal).Replace("y=720", "y=360", StringComparison.Ordinal);
        Assert.Equal(expected, await File.ReadAllTextAsync(Path.Combine(output, "script", "sample05.ast")));
        Assert.Equal(expected, Encoding.UTF8.GetString((await codec.ReadSmallEntryAsync(Path.Combine(output, "root.pfs"), "script/sample05.ast"))!));
        string resultIni = await File.ReadAllTextAsync(Path.Combine(output, "system.ini"));
        Assert.Contains("WIDTH=１２８０\nHEIGHT=540", resultIni);
        Assert.Equal(resultIni, Encoding.UTF8.GetString((await codec.ReadSmallEntryAsync(Path.Combine(output, "root.pfs"), "system.ini"))!));
        Assert.Equal(originalArchive, await File.ReadAllBytesAsync(archive));
        Assert.Equal(script, await File.ReadAllTextAsync(scriptPath));
        Assert.Equal(ini, await File.ReadAllTextAsync(iniPath));
    }

    private static Encoding EncodingFor(bool cp932)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return cp932 ? Encoding.GetEncoding(932) : new UTF8Encoding(true);
    }
    private static byte[] Bytes(Encoding encoding, string text) => [.. encoding.GetPreamble(), .. encoding.GetBytes(text)];
    public void Dispose() => Directory.Delete(root, true);
}
