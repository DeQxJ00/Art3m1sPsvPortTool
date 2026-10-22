using System.Text;
using Art3m1s.PsvTool.App;
using Art3m1s.PsvTool.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Art3m1s.PsvTool.Ui.Tests;

public sealed class ConversionLogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "art3m1s-log-tests-" + Guid.NewGuid().ToString("N"));
    private string Input => Path.Combine(_root, "source", "sample01");
    private string Output => Path.Combine(_root, "destination", "result");
    public ConversionLogTests() { Directory.CreateDirectory(Input); }
    public void Dispose() { Directory.Delete(_root, true); }

    [Fact]
    public void SuccessfulRunCreatesSeparateErrorLogWithoutOrdinaryProgress()
    {
        string path, errors;
        using (var log = new ConversionRunLog(Input, Output, standardOutput: TextWriter.Null, standardError: TextWriter.Null))
        {
            path = log.LogPath; errors = log.ErrorLogPath;
            Assert.True(File.Exists(path)); Assert.True(File.Exists(errors));
            Assert.NotEqual(path, errors);
            using (var reader = new StreamReader(new FileStream(errors, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                Assert.Contains("code=RUN_START", reader.ReadToEnd());
            log.Report(new(50, "resource", "root.pfs", "image/bg/sample.png"));
            log.Complete("success", 0);
        }
        string details = File.ReadAllText(errors);
        Assert.Contains("code=RUN_END", details);
        Assert.Contains("outcome=success; exit-code=0", details);
        Assert.Contains("warning-events=0; error-events=0", details);
        Assert.DoesNotContain("code=PROGRESS", details);
        Assert.Contains("image/bg/sample.png", File.ReadAllText(path));
        Assert.DoesNotContain("image/bg/sample.png", details);
    }

    [Fact]
    public void AllResourcesReachFileWhileOnlyConsoleProgressIsThrottled()
    {
        StringWriter console = new();
        string full, issues;
        using (var log = new ConversionRunLog(Input, Output, standardOutput: console, standardError: new StringWriter()))
        {
            full = log.LogPath; issues = log.ErrorLogPath;
            for (int i = 0; i < 80; i++) log.Report(new(i, "resource", "root.pfs.010", $"script/{i:D3}.ast"));
            log.Report(new(81, "resource", "root.pfs.010", "image/bg/large.png",
                new(ConversionLogLevel.Warning, "TARGET_DIMENSIONS_GT_4096", "source=9000x8; target=4500x4; limit=4096")));
            log.Exception(new ConversionItemException("root.pfs.010", "movie/sample.ogv",
                new InvalidDataException("encode failed", new IOException("ffmpeg exit=69\nfull stderr details")), "resource"));
            log.Complete("failed", 1);
        }
        Assert.Equal(Path.Combine(Path.GetDirectoryName(Output)!, "logs", "sample01", "sample01.log"), full);
        Assert.Equal(Path.Combine(Path.GetDirectoryName(full)!, "sample01.errors.log"), issues);
        string all = File.ReadAllText(full), errors = File.ReadAllText(issues);
        for (int i = 0; i < 80; i++) Assert.Contains($"script/{i:D3}.ast", all);
        Assert.DoesNotContain("script/079.ast", console.ToString());
        Assert.Contains("TARGET_DIMENSIONS_GT_4096", console.ToString());
        Assert.Contains("full stderr details", errors);
        Assert.Contains("movie/sample.ogv", errors);
        Assert.Contains("archive=\"root.pfs.010\"", errors);
        Assert.DoesNotContain("script/079.ast", errors);
        Assert.Contains("code=RUN_END", all); Assert.Contains("exit-code=1", all);
    }

    [Fact]
    public void RepeatedRunsAppendToGameNamedLogsWithDistinctRunIds()
    {
        string path, errorPath, firstRun, secondRun;
        using (var first = new ConversionRunLog(Input, Output, standardOutput: TextWriter.Null, standardError: TextWriter.Null))
        {
            path = first.LogPath; errorPath = first.ErrorLogPath; firstRun = first.RunId;
            Assert.True(File.Exists(errorPath));
            first.Report(new(0, "texture-scan", null, "broken.png", new(ConversionLogLevel.Warning, "PRESERVED", "kept original")));
            first.Complete("success", 0);
        }
        using (var second = new ConversionRunLog(Input, Output, standardOutput: TextWriter.Null, standardError: TextWriter.Null))
        {
            Assert.Equal(path, second.LogPath); Assert.Equal(errorPath, second.ErrorLogPath);
            secondRun = second.RunId;
            Assert.NotEqual(firstRun, secondRun);
            second.Info("SECOND_RUN", "second invocation");
            second.Complete("success", 0);
        }
        string all = File.ReadAllText(path), errors = File.ReadAllText(errorPath);
        Assert.Equal(2, all.Split("code=RUN_START", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, all.Split("code=RUN_END", StringSplitOptions.None).Length - 1);
        Assert.Contains("kept original", all); Assert.Contains("second invocation", all);
        Assert.Contains("run=" + firstRun, errors); Assert.Contains("kept original", errors);
        Assert.Contains("run=" + secondRun, errors);
        Assert.Equal(2, errors.Split("code=RUN_START", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, errors.Split("code=RUN_END", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("second invocation", errors);
    }

    [Fact]
    public async Task SetupErrorIsPersistedUnderGameNameEvenWhenParsingFails()
    {
        string fallback = Path.Combine(_root, "fallback");
        Assert.Equal(2, await ConversionCommandLine.RunAsync(["--convert", Input, Output, "--ratio", "2"],
            standardOutput: new StringWriter(), standardError: new StringWriter(), fallbackLogDirectory: fallback));
        string error = Directory.GetFiles(fallback, "*.errors.log", SearchOption.AllDirectories).Single();
        Assert.Equal(Path.Combine(fallback, "sample01", "sample01.errors.log"), error);
        string text = await File.ReadAllTextAsync(error);
        Assert.Contains("--ratio", text); Assert.Contains("stage=\"setup\"", text);
        Assert.False(Directory.Exists(Output));
    }

    [Fact]
    public async Task PreviewErrorsDoNotWriteFallbackOrLogs()
    {
        string fallback = Path.Combine(_root, "fallback");
        Assert.Equal(2, await ConversionCommandLine.RunAsync(["--convert", Input, Output, "--ratio", "2", "--dry-run"],
            standardOutput: new StringWriter(), standardError: new StringWriter(), fallbackLogDirectory: fallback));
        Assert.False(Directory.Exists(fallback)); Assert.False(Directory.Exists(Path.GetDirectoryName(Output)!));
    }

    [Fact]
    public async Task OversizedArchivedPngRecordsBothSizesAndPreservation()
    {
        string pngPath = Path.Combine(_root, "payload.png");
        using (Image<Rgb24> png = new(9000, 8, new Rgb24(170, 90, 30))) await png.SaveAsPngAsync(pngPath);
        await Pack("image/bg/large.png", pngPath);
        Assert.Equal(0, await ConversionCommandLine.RunAsync(["--convert", Input, Output, "--subset-fonts", "true", "--font-profile", "simplified"],
            standardOutput: new StringWriter(), standardError: new StringWriter()));
        string error = Directory.GetFiles(Path.GetDirectoryName(Output)!, "*.errors.log", SearchOption.AllDirectories).Single();
        string text = await File.ReadAllTextAsync(error);
        Assert.Contains("TARGET_DIMENSIONS_GT_4096", text);
        Assert.Contains("source=9000x8; target=4500x4", text);
        Assert.Contains("archive=\"root.pfs\"", text);
        Assert.Contains("image", text); Assert.Contains("large.png", text);
        string full = error.Replace(".errors.log", ".log", StringComparison.Ordinal);
        Assert.Contains("SOURCE_DIMENSIONS_GT_4096", await File.ReadAllTextAsync(full));
        string unpack = Path.Combine(_root, "unpacked");
        await new PfsCodec().ExtractAsync(Path.Combine(Output, "root.pfs"), unpack);
        using Image result = await Image.LoadAsync(Path.Combine(unpack, "image", "bg", "large.png"));
        Assert.Equal(4500, result.Width);
    }

    [Fact]
    public async Task EveryImageScanFailureIsLoggedWithoutPublishingOutput()
    {
        string payload = Path.Combine(_root, "payload.bin");
        await File.WriteAllBytesAsync(payload, [1]); await Pack("sample.bin", payload);
        await File.WriteAllBytesAsync(Path.Combine(Input, "sample01.png"), [1, 2]);
        await File.WriteAllBytesAsync(Path.Combine(Input, "sample02.png"), [3, 4]);
        Assert.Equal(1, await ConversionCommandLine.RunAsync(["--convert", Input, Output, "--subset-fonts", "true", "--font-profile", "simplified"],
            standardOutput: new StringWriter(), standardError: new StringWriter()));
        string error = Directory.GetFiles(Path.GetDirectoryName(Output)!, "*.errors.log", SearchOption.AllDirectories).Single();
        string text = await File.ReadAllTextAsync(error);
        Assert.Contains("sample01.png", text); Assert.Contains("sample02.png", text);
        Assert.Contains("IMAGE_SCAN_FAILED", text);
        Assert.False(Directory.Exists(Output));
    }

    [Fact]
    public void ErrorLogWriteFailureDoesNotMaskOriginalError()
    {
        StringWriter stderr = new();
        string path = Path.Combine(_root, "blocked.log");
        Directory.CreateDirectory(Path.Combine(_root, "blocked.errors.log"));
        using var log = new ConversionRunLog(Input, Output, logPath: path, standardOutput: TextWriter.Null, standardError: stderr);
        log.Exception(new InvalidDataException("original failure"));
        Assert.True(log.HasWriteFailure);
        Assert.Contains("LOG_WRITE_FAILED", stderr.ToString());
        Assert.Contains("original failure", stderr.ToString());
        log.Dispose();
        Assert.Contains("original failure", File.ReadAllText(log.LogPath));
    }

    [Fact]
    public void MultilineMetadataCannotInjectUnprefixedLogRecords()
    {
        using var log = new ConversionRunLog(Input, Output, standardOutput: TextWriter.Null, standardError: TextWriter.Null);
        log.Report(new(0, "resource", "root.pfs\nFORGED", "image/entry\nFORGED.png", new(ConversionLogLevel.Error, "TEST", "line1\nline2")));
        log.Dispose();
        Assert.All(File.ReadAllLines(log.ErrorLogPath), line => Assert.Contains("game=\"sample01\"", line));
    }

    private async Task Pack(string name, string source) => await new PfsCodec().PackPf8Async(new('8',
        [new(Encoding.UTF8.GetBytes(name), name, 0, (uint)new FileInfo(source).Length, source)]), Path.Combine(Input, "root.pfs"));
}
