using CursorShake.Windows.Recovery;

namespace CursorShake.RecoveryGuard;
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        if (args.Length != 2 || args[0] != "--session") return 2;
        try { return RecoveryWorker.Run(Path.GetFullPath(args[1])).AllOwnedRestored ? 0 : 1; }
        catch { return 1; }
    }
}
