namespace Art3m1s.PsvTool.Core;

public static class TextureDimensionDiagnostics
{
    public const int Limit = 4096;

    public static IEnumerable<ConversionDiagnostic> Create(int width, int height, int targetWidth, int targetHeight, string texture)
    {
        string dimensions = $"texture={texture}; source={width}x{height}; target={targetWidth}x{targetHeight}; limit={Limit}";
        if (width > Limit || height > Limit)
            yield return new(ConversionLogLevel.Info, "SOURCE_DIMENSIONS_GT_4096", dimensions +
                $"; source exceeds 4096; target-within-limit={targetWidth <= Limit && targetHeight <= Limit}");
        if (targetWidth > Limit || targetHeight > Limit)
            yield return new(ConversionLogLevel.Warning, "TARGET_DIMENSIONS_GT_4096", dimensions +
                "; target still exceeds 4096; native PSV texture compatibility is not guaranteed");
    }
}
