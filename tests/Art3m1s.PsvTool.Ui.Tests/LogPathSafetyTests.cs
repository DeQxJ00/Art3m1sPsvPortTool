using Art3m1s.PsvTool.App;
using Xunit;

namespace Art3m1s.PsvTool.Ui.Tests;

public sealed class LogPathSafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "art3m1s-log-path-" + Guid.NewGuid().ToString("N"));
    public LogPathSafetyTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Theory]
    [InlineData("/var", "/private/var")]
    [InlineData("/var/folders/sample/log.txt", "/private/var/folders/sample/log.txt")]
    [InlineData("/tmp/log.txt", "/private/tmp/log.txt")]
    [InlineData("/etc/sample/log.txt", "/private/etc/sample/log.txt")]
    [InlineData("/private/var/log.txt", "/private/var/log.txt")]
    [InlineData("/various/log.txt", "/various/log.txt")]
    [InlineData("/tmp-extra/log.txt", "/tmp-extra/log.txt")]
    [InlineData("/etc-extra/log.txt", "/etc-extra/log.txt")]
    [InlineData("/VAR/log.txt", "/VAR/log.txt")]
    [InlineData("var/log.txt", "var/log.txt")]
    [InlineData("/home/sample/var/log.txt", "/home/sample/var/log.txt")]
    public void NormalizesOnlyExactSystemAliasPrefixes(string path, string expected)
    {
        Assert.Equal(expected, LogPathSafety.NormalizeMacOSSystemPath(path));
        Assert.Equal(expected, LogPathSafety.NormalizeMacOSSystemPath(expected));
    }

    [Theory]
    [InlineData("/var", "/private/var", true)]
    [InlineData("/tmp", "/private/tmp", true)]
    [InlineData("/etc", "/private/etc", true)]
    [InlineData("/var", "/other/var", false)]
    [InlineData("/var", "/private/var/child", false)]
    [InlineData("/var/child", "/private/var/child", false)]
    [InlineData("/home/sample", "/private/var", false)]
    [InlineData("/var", null, false)]
    public void WhitelistRequiresExactAliasAndTarget(string alias, string? target, bool expected)
        => Assert.Equal(expected, LogPathSafety.IsTrustedMacOSAliasTarget(alias, target));

    [Fact]
    public void SystemTempLogsWorkWithoutChangingTheirDisplayedPaths()
    {
        string source = Path.Combine(_root, "source", "sample01"), output = Path.Combine(_root, "output", "result");
        Directory.CreateDirectory(source);
        LogPathSafety.RejectLinkedAncestors(source);
        LogPathSafety.RejectLinkedAncestors(output);
        LogPathSafety.RejectLinkedAncestors(output + ".conversion.lock");
        using var log = new ConversionRunLog(source, output, standardOutput: TextWriter.Null, standardError: TextWriter.Null);
        Assert.Equal(Path.Combine(_root, "output", "logs", "sample01", "sample01.log"), log.LogPath);
        Assert.True(File.Exists(log.LogPath)); Assert.True(File.Exists(log.ErrorLogPath));
        log.Complete("success", 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MacOSAliasCannotHideLogInsideProtectedInputOrOutput(bool protectInput)
    {
        if (!OperatingSystem.IsMacOS()) return;
        string source = Path.Combine(_root, "source"), output = Path.Combine(_root, "output");
        string protectedRoot = protectInput ? source : output;
        Directory.CreateDirectory(protectedRoot);
        string canonical = LogPathSafety.NormalizeMacOSSystemPath(protectedRoot);
        // Test both alias -> physical and physical -> alias spellings.
        Assert.Throws<ArgumentException>(() => LogPathSafety.Validate(source, output, Path.Combine(canonical, "bad.log")));
        Assert.Throws<ArgumentException>(() => LogPathSafety.Validate(
            LogPathSafety.NormalizeMacOSSystemPath(source), LogPathSafety.NormalizeMacOSSystemPath(output),
            Path.Combine(protectedRoot, "bad.log")));
        Assert.False(File.Exists(Path.Combine(protectedRoot, "bad.log")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UserDirectoryLinksRemainForbiddenEvenWhenPointingOutsideProtectedRoots(bool pointsIntoInput)
    {
        if (OperatingSystem.IsWindows()) return; // Windows link creation requires privileges; Unix CI exercises this guard.
        string source = Path.Combine(_root, "source"), output = Path.Combine(_root, "output");
        string target = pointsIntoInput ? source : Path.Combine(_root, "other");
        Directory.CreateDirectory(source); Directory.CreateDirectory(target);
        string link = Path.Combine(_root, "custom-link");
        Directory.CreateSymbolicLink(link, target);
        try
        {
            Assert.Throws<ArgumentException>(() => LogPathSafety.RejectLinkedAncestors(Path.Combine(link, "bad.log")));
            Assert.Throws<ArgumentException>(() => new ConversionRunLog(source, output,
                logPath: Path.Combine(link, "bad.log"), standardOutput: TextWriter.Null, standardError: TextWriter.Null));
            Assert.False(File.Exists(Path.Combine(target, "bad.log")));
        }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public void DanglingFileLinkCannotCreateLogInsideInput()
    {
        if (OperatingSystem.IsWindows()) return;
        string source = Path.Combine(_root, "source"), output = Path.Combine(_root, "output");
        Directory.CreateDirectory(source);
        string link = Path.Combine(_root, "log-link.log"), target = Path.Combine(source, "must-not-exist.log");
        File.CreateSymbolicLink(link, target);
        try
        {
            Assert.Throws<ArgumentException>(() => LogPathSafety.RejectLinkedAncestors(link));
            Assert.Throws<ArgumentException>(() => new ConversionRunLog(source, output,
                logPath: link, standardOutput: TextWriter.Null, standardError: TextWriter.Null));
            Assert.False(File.Exists(target));
        }
        finally { File.Delete(link); }
    }
}
