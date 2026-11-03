using System.Text.Json;
using Art3m1s.PsvTool.Core;
using Xunit;

namespace Art3m1s.PsvTool.Core.Tests;

internal static class ConvertedArchiveAssertions
{
    public static IReadOnlyList<PfsEntry> Assets(ExtractedArchive archive)
    {
        var marker = Assert.Single(archive.Entries, e => e.Path == "art3m1s_conversion.json");
        using var json = JsonDocument.Parse(File.ReadAllBytes(marker.ExtractedPath));
        Assert.Equal(1, json.RootElement.GetProperty("SchemaVersion").GetInt32());
        return archive.Entries.Where(e => e != marker).ToArray();
    }
}
