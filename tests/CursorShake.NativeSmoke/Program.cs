using CursorShake.NativeSmoke;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--retire-restored-session") { var result=CursorShake.Windows.Recovery.RecoveryBootstrap.TryRetireAlreadyRestored(Path.GetFullPath(args[1])); File.WriteAllText(Path.GetFullPath(args[2]),System.Text.Json.JsonSerializer.Serialize(result,new System.Text.Json.JsonSerializerOptions{WriteIndented=true})); return result.AllOwnedRestored?0:1; }
        if (args.Length == 3 && args[0] == "--inspect-live-cursors") return LiveCursorInspection.Run(Path.GetFullPath(args[1]),Path.GetFullPath(args[2]));
        if (args.Length == 2 && args[0] == "--dialog-ux-preview") return DialogUxPreview.Run(Path.GetFullPath(args[1]));
        if (args.Length == 3 && args[0] == "--reset-registered-scheme") { var result=CursorShake.Windows.Recovery.RecoveryBootstrap.ResetToRegisteredScheme(Path.GetFullPath(args[1])); File.WriteAllText(Path.GetFullPath(args[2]),System.Text.Json.JsonSerializer.Serialize(result,new System.Text.Json.JsonSerializerOptions{WriteIndented=true})); return result.AllOwnedRestored?0:1; }
        if (args.Length == 5 && args[0] == "--cursor-reload-recovery") return CursorReloadRecoverySmoke.Run(Path.GetFullPath(args[1]),Path.GetFullPath(args[2]),Path.GetFullPath(args[3]),Path.GetFullPath(args[4]));
        if (args.Length == 5 && args[0] == "--engine-failure-regressions") return EngineFailureRegressionSmoke.Run(Path.GetFullPath(args[1]),Path.GetFullPath(args[2]),Path.GetFullPath(args[3]),Path.GetFullPath(args[4]));
        if (args.Length == 3 && args[0] == "--maintenance-close") { using var target=System.Diagnostics.Process.GetProcessById(int.Parse(args[1],System.Globalization.CultureInfo.InvariantCulture)); if(!string.Equals(target.MainModule?.FileName,Path.GetFullPath(args[2]),StringComparison.OrdinalIgnoreCase))throw new IOException("Process path differs from the requested installed executable."); using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30)); CursorShake.Windows.Lifecycle.MaintenanceEndpoint.RequestShutdownAndWaitAsync(target,timeout.Token).GetAwaiter().GetResult(); return 0; }
        if (args.Length == 2 && args[0] == "--startup-logon-info") { File.WriteAllText(Path.GetFullPath(args[1]),System.Text.Json.JsonSerializer.Serialize(CursorShake.Windows.Lifecycle.StartupTimingProbe.ReadCurrentLogon(),new System.Text.Json.JsonSerializerOptions{WriteIndented=true})); return 0; }
        if (args.Length == 3 && args[0] == "--installer-wizard-preview") return InstallerWizardPreview.Run(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
        if (args.Length == 2 && args[0] == "--windows-svg-sheet") { WindowsSvgSheet.Create(Path.GetFullPath(args[1])); return 0; }
        if (args.Length == 3 && args[0] == "--prepared-theme-sheet") { PreparedThemeSheet.Create(Path.GetFullPath(args[1]), Path.GetFullPath(args[2])); return 0; }
        if (args.Length == 3 && args[0] == "--roundtrip") return NativeRoundtrip.Run(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
        if (args.Length == 3 && args[0] == "--capture-only") return NativeCapture.Run(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
        if (args.Length == 4 && args[0] == "--guard-ready-capture") return GuardReadyCapture.Run(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]), Path.GetFullPath(args[3]));
        if (args.Length == 5 && args[0] == "--app-smoke") return AppSmoke.Run(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]), Path.GetFullPath(args[3]), Path.GetFullPath(args[4]));
        if (args.Length == 3 && args[0] == "--installer-roundtrip") return InstallerRoundtripSmoke.Run(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
        if (args.Length == 3 && args[0] == "--installer-roundtrip-all-users") return InstallerRoundtripSmoke.Run(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]), allUsers: true);
        if (args.Length == 2 && args[0] == "--maintenance-process-smoke") return MaintenanceProcessSmoke.Run(Path.GetFullPath(args[1]));
        if (args.Length == 3 && args[0] == "--maintenance-process-child") return MaintenanceProcessSmoke.Child(int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture), Path.GetFullPath(args[2]));
        if (args.Length == 2 && args[0] == "--tray-menu-render") return TrayMenuRender.Run(Path.GetFullPath(args[1]));
        if (args.Length == 2 && args[0] == "--ui-theme-smoke") return UiThemeSmoke.Run(Path.GetFullPath(args[1]));
        if (args.Length == 2 && args[0] == "--menu-render") return MenuRender.Run(Path.GetFullPath(args[1]));
        if (args.Length == 2 && args[0] == "--runtime-proof") return RuntimeProof.Run(Path.GetFullPath(args[1]));
        if (args.Length == 2 && args[0] == "--startup-registration") return StartupRegistrationSmoke.Run(Path.GetFullPath(args[1]));
        if (args.Length == 2 && args[0] == "--registered-scheme-probe") return RegisteredSchemeProbe.Run(Path.GetFullPath(args[1]));
        if (args.Length == 4 && args[0] == "--foreign-change") return ForeignChangeSmoke.Run(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]), Path.GetFullPath(args[3]));
        if (args.Length == 5 && args[0] == "--engine-performance") return EnginePerformance.Run(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]), Path.GetFullPath(args[3]), Path.GetFullPath(args[4]));
        if (args.Length == 4 && args[0] == "--engine-stress") return EngineStress.Run(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]), Path.GetFullPath(args[3]));
        if (args.Length == 4 && args[0] == "--crash-guard") return CrashGuardSmoke.Run(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]), Path.GetFullPath(args[3]));
        if (args.Length == 4 && args[0] == "--crash-child") return CrashGuardSmoke.Child(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]), Path.GetFullPath(args[3]));
        if (args.Length == 5 && args[0] == "--engine-smoke") return EngineSmoke.Run(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]), Path.GetFullPath(args[3]), Path.GetFullPath(args[4]));
        if (args.Length == 4 && args[0] == "--pointer-owner") return StandardPointerOwner.Run(Path.GetFullPath(args[1]), args[2], int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture));
        if (args.Length == 3 && args[0] == "--pointer-owner") return StandardPointerOwner.Run(Path.GetFullPath(args[1]), args[2]);
        Console.Error.WriteLine("Test-only host: --windows-svg-sheet, --prepared-theme-sheet, --roundtrip, --capture-only, --guard-ready-capture, --app-smoke, --engine-smoke, --engine-stress, --engine-performance, --crash-guard, --foreign-change. Mutating modes require exclusive coordinated Windows QA.");
        return 2;
    }
}
