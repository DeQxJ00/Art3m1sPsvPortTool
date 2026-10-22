using System.Runtime.InteropServices;

namespace Art3m1s.PsvTool.App;

internal static class CliConsole
{
    public static void Attach()
    {
        if (!OperatingSystem.IsWindows()) return;
        // A GUI-subsystem executable has no console by default. Preserve inherited pipes/files.
        nint stdout = GetStdHandle(-11), stderr = GetStdHandle(-12);
        if (!AttachConsole(uint.MaxValue)) return;
        if (stdout != 0 && stdout != -1) _ = SetStdHandle(-11, stdout);
        if (stderr != 0 && stderr != -1) _ = SetStdHandle(-12, stderr);
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint processId);
    [DllImport("kernel32.dll")]
    private static extern nint GetStdHandle(int handle);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetStdHandle(int handle, nint value);
}
