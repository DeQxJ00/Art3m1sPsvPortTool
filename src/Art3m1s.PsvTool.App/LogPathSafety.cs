namespace Art3m1s.PsvTool.App;

internal static class LogPathSafety
{
    public static void Validate(string input, string output, params string[] logs)
    {
        string[] roots = [NormalizeForComparison(Path.GetFullPath(input)), NormalizeForComparison(Path.GetFullPath(output))];
        foreach (string path in logs)
        {
            string full = Path.GetFullPath(path);
            string comparable = NormalizeForComparison(full);
            foreach (string root in roots)
            {
                string relative = Path.GetRelativePath(root, comparable);
                if (relative == "." || !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    throw new ArgumentException("Logs must be outside input/output directories: " + path);
            }
            RejectLinkedAncestors(full);
        }
    }

    internal static void RejectLinkedAncestors(string path, string message = "Log paths must not contain symbolic links/junctions: ")
    {
        for (string? ancestor = Path.GetFullPath(path); ancestor != null; ancestor = Path.GetDirectoryName(ancestor))
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(ancestor); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            // GetAttributes also detects dangling links, unlike File/Directory.Exists.
            if ((attributes & FileAttributes.ReparsePoint) != 0 && !IsVerifiedSystemAlias(ancestor))
                throw new ArgumentException(message + ancestor);
        }
    }

    // Pure lexical helpers are testable on every host; production only uses them
    // on macOS after checking the exact system link and its resolved target.
    internal static string NormalizeMacOSSystemPath(string path)
    {
        foreach (string alias in new[] { "/var", "/tmp", "/etc" })
            if (path == alias || path.StartsWith(alias + "/", StringComparison.Ordinal)) return "/private" + path;
        return path;
    }

    internal static bool IsTrustedMacOSAliasTarget(string alias, string? target) =>
        alias is "/var" or "/tmp" or "/etc" && target == "/private" + alias;

    internal static string NormalizeForComparison(string path)
    {
        if (!OperatingSystem.IsMacOS()) return path;
        string normalized = NormalizeMacOSSystemPath(path);
        if (normalized == path) return path;
        int end = path.IndexOf('/', 1);
        string alias = end < 0 ? path : path[..end];
        return IsVerifiedSystemAlias(alias) ? normalized : path;
    }

    private static bool IsVerifiedSystemAlias(string path)
    {
        if (!OperatingSystem.IsMacOS() || path is not ("/var" or "/tmp" or "/etc")) return false;
        try { return IsTrustedMacOSAliasTarget(path, new DirectoryInfo(path).ResolveLinkTarget(true)?.FullName); }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
