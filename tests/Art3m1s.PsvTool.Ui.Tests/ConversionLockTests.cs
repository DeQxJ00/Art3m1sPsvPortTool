using Art3m1s.PsvTool.App;
using Art3m1s.PsvTool.Core;
using Xunit;

namespace Art3m1s.PsvTool.Ui.Tests;

public sealed class ConversionLockTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "art3m1s-lock-tests-" + Guid.NewGuid().ToString("N"));
    private string Input => Path.Combine(_root, "sample01");
    private string Output => Path.Combine(_root, "result");
    private string LockPath => Output + ".conversion.lock";
    public ConversionLockTests() { Directory.CreateDirectory(Input); }
    public void Dispose() { Directory.Delete(_root, true); }

    [Fact]
    public async Task ReclaimsUnheldLegacySidecarAndDeletesOnlyItsOwnLock()
    {
        await File.WriteAllBytesAsync(LockPath, []);
        int calls = 0;
        var converter = new CallbackConverter((_, _) =>
        {
            calls++;
            Assert.True(File.Exists(LockPath));
            Assert.ThrowsAny<IOException>(() => new FileStream(LockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
            return Task.CompletedTask;
        });
        Assert.Equal(0, await Run(converter));
        Assert.False(File.Exists(LockPath));
        Assert.Equal(0, await Run(converter));
        Assert.False(File.Exists(LockPath));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ContenderDoesNotDeleteExternallyHeldLock()
    {
        using var owner = new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        await owner.WriteAsync(new byte[] { 1, 2, 3 });
        int calls = 0;
        var converter = new CallbackConverter((_, _) => { calls++; return Task.CompletedTask; });
        Assert.Equal(2, await Run(converter));
        Assert.Equal(0, calls);
        Assert.True(File.Exists(LockPath));
        Assert.Equal(3, owner.Length);
        Assert.ThrowsAny<IOException>(() => new FileStream(LockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
        owner.Dispose();
        Assert.True(File.Exists(LockPath));
    }

    [Fact]
    public async Task PreviewAndPreCancelledInvocationDoNotTouchExistingSidecar()
    {
        using var owner = new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var converter = new CallbackConverter((_, _) => throw new InvalidOperationException("Must not convert."));
        Assert.Equal(0, await Run(converter, extra: ["--dry-run"]));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Equal(130, await Run(converter, token: cancellation.Token));
        Assert.True(File.Exists(LockPath));
        owner.Dispose();
        Assert.True(File.Exists(LockPath));
    }

    [Fact]
    public async Task SetupFailureAfterAcquiringLockCleansIt()
    {
        string blockedLog = Path.Combine(_root, "blocked.log");
        Directory.CreateDirectory(blockedLog);
        var converter = new CallbackConverter((_, _) => throw new InvalidOperationException("Must not convert."));
        Assert.Equal(2, await Run(converter, extra: ["--log", blockedLog]));
        Assert.False(File.Exists(LockPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailureOrCancellationAndContenderLeaveAnotherActiveOutputLocked(bool cancelOther)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = new CallbackConverter(async (_, token) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
        });
        Task<int> first = Run(active);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            string otherInput = Path.Combine(_root, "sample02"), otherOutput = Path.Combine(_root, "other-result");
            Directory.CreateDirectory(otherInput);
            using CancellationTokenSource otherCancellation = new();
            var failure = new CallbackConverter((_, token) =>
            {
                if (cancelOther) { otherCancellation.Cancel(); token.ThrowIfCancellationRequested(); }
                throw new InvalidDataException("Independent failure.");
            });
            Assert.Equal(cancelOther ? 130 : 1, await Run(failure, input: otherInput, output: otherOutput,
                token: otherCancellation.Token));
            Assert.False(File.Exists(otherOutput + ".conversion.lock"));
            Assert.True(File.Exists(LockPath));
            Assert.Equal(2, await Run(failure));
            Assert.True(File.Exists(LockPath));
            Assert.ThrowsAny<IOException>(() => new FileStream(LockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
        }
        finally { release.TrySetResult(); await first.WaitAsync(TimeSpan.FromSeconds(10)); }
        Assert.Equal(0, await first);
        Assert.False(File.Exists(LockPath));
    }

    [Fact]
    public async Task CompletionOutputFailureStillReleasesAndDeletesLock()
    {
        var converter = new CallbackConverter((_, _) => Task.CompletedTask);
        using var writer = new CompletionFailureWriter();
        await Assert.ThrowsAsync<IOException>(() => Run(converter, standardOutput: writer));
        Assert.False(File.Exists(LockPath));
    }

    [Fact]
    public async Task RepeatedConcurrentAcquisitionNeverOverlapsConversions()
    {
        int active = 0, overlaps = 0;
        var converter = new CallbackConverter(async (_, _) =>
        {
            if (Interlocked.Increment(ref active) > 1) Interlocked.Increment(ref overlaps);
            try { await Task.Delay(2); }
            finally { Interlocked.Decrement(ref active); }
        });
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int[]>[] workers = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            List<int> codes = [];
            for (int i = 0; i < 10; i++) codes.Add(await Run(converter));
            return codes.ToArray();
        })).ToArray();
        start.SetResult();
        int[] results = (await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(30))).SelectMany(x => x).ToArray();
        Assert.Equal(0, overlaps);
        Assert.Contains(0, results);
        Assert.All(results, code => Assert.True(code is 0 or 2, $"Unexpected exit code {code}."));
        Assert.False(File.Exists(LockPath));
    }

    private Task<int> Run(IConversionService converter, string? input = null, string? output = null,
        CancellationToken token = default, string[]? extra = null, TextWriter? standardOutput = null) =>
        ConversionCommandLine.RunAsync(["--convert", input ?? Input, output ?? Output,
            "--subset-fonts", "true", "--font-profile", "simplified", .. extra ?? []], token,
            scanner: new Scanner(), converter: converter, standardOutput: standardOutput ?? TextWriter.Null,
            standardError: TextWriter.Null, fallbackLogDirectory: Path.Combine(_root, "fallback"));

    private sealed class Scanner : IProjectScanner
    {
        public Task<ScanResult> ScanAsync(string inputDirectory, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ScanResult([new(Path.Combine(inputDirectory, "root.pfs"), "root.pfs", '8', 0)],
                true, 1920, 1080, []));
    }

    private sealed class CallbackConverter(Func<ConversionOptions, CancellationToken, Task> convert) : IConversionService
    {
        public Task ConvertAsync(ConversionOptions options, IProgress<ConversionProgress>? progress = null,
            CancellationToken cancellationToken = default) => convert(options, cancellationToken);
    }

    private sealed class CompletionFailureWriter : StringWriter
    {
        public override void WriteLine(string? value)
        {
            if (value?.Contains("code=RUN_END", StringComparison.Ordinal) == true) throw new IOException("Completion output failure.");
            base.WriteLine(value);
        }
    }
}
