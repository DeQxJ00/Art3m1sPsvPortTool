using System.Globalization;
using System.Numerics;

namespace Art3m1s.PsvTool.Core;

internal static class ArchiveOrdering
{
    internal static (string Family, BigInteger Priority)? Rank(string? archive)
    {
        if (archive is null) return null;
        int at = archive.LastIndexOf(".pfs", StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;
        string suffix = archive[(at + 4)..];
        if (suffix.Length == 0) return (archive, -1);
        if (suffix.Length < 2 || suffix[0] != '.' || !suffix[1..].All(char.IsAsciiDigit)) return null;
        return (archive[..(at + 4)], BigInteger.Parse(suffix[1..], CultureInfo.InvariantCulture));
    }

    internal static bool CanOrder(string? first, string? second)
    {
        var a = Rank(first); var b = Rank(second);
        return a is not null && b is not null && a.Value.Family.Equals(b.Value.Family, StringComparison.OrdinalIgnoreCase)
            && a.Value.Priority != b.Value.Priority;
    }

    internal static IOrderedEnumerable<PfsFileInfo> Sort(IEnumerable<PfsFileInfo> archives) =>
        archives.OrderBy(a => Rank(a.FileName)?.Family ?? a.FileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => Rank(a.FileName)?.Priority ?? -1)
            .ThenBy(a => a.FileName, StringComparer.OrdinalIgnoreCase);
}
