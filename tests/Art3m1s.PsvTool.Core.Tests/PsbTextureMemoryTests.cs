using Art3m1s.PsvTool.Core;
using Xunit;

namespace Art3m1s.PsvTool.Core.Tests;

public sealed class PsbTextureMemoryTests
{
    [Theory]
    [InlineData(4096, 2048, 33554432L, 8388608L, 33554432L, 8388608L)]
    [InlineData(2048, 1024, 8388608L, 2097152L, 8388608L, 2097152L)]
    [InlineData(3072, 1536, 18874368L, 4718592L, 18874368L, 8388608L)]
    [InlineData(64, 64, 16384L, 4096L, 262144L, 262144L)]
    [InlineData(1, 1, 4L, 16L, 262144L, 262144L)]
    [InlineData(513, 257, 527364L, 134160L, 786432L, 524288L)]
    public void EstimatesSeparatePixelDataFromGxmAllocation(int width, int height,
        long rgbaData, long bc3Data, long rgbaAllocation, long bc3Allocation)
    {
        Assert.Equal(new PsbTextureMemoryEstimate(rgbaData, bc3Data, rgbaAllocation, bc3Allocation), PsbTextureMemory.Estimate(width, height));
    }

    [Theory]
    [InlineData(0, 512)]
    [InlineData(512, -1)]
    [InlineData(4097, 512)]
    [InlineData(512, int.MaxValue)]
    public void UnsupportedDimensionsAreNotPresentedAsGpuEstimates(int width, int height)
        => Assert.Null(PsbTextureMemory.Estimate(width, height));
}
