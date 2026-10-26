using CursorShake.Windows.Configuration;
using Xunit;

namespace CursorShake.Windows.Tests;

public sealed class FileMonitorTests
{
    [Fact]
    public async Task SavingTomlRaisesAnAutomaticChangeNotification()
    {
        using var fixture = new CursorFixture();
        var path = fixture.WriteText("config.toml", "version=1");
        using var monitor = new FileMonitor(fixture.DirectoryPath);
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.Changed += () => changed.TrySetResult();
        await File.WriteAllTextAsync(path, "version=1\n[gesture]\ncount=5");
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("count=5", File.ReadAllText(path));
    }

    [Fact]
    public async Task RenameBasedEditorSaveIsDetectedWithoutAnApplyButton()
    {
        using var fixture = new CursorFixture();
        var path = fixture.WriteText("config.toml", "version=1");
        var temporary = fixture.WriteText("editor.tmp", "version=1\n[gesture]\ncount=7");
        using var monitor = new FileMonitor(fixture.DirectoryPath);
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.Changed += () => changed.TrySetResult();
        File.Move(temporary, path, true);
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("count=7", File.ReadAllText(path));
    }

    [Fact]
    public async Task DeletingAPersonalCursorIsDetectedWithoutEditingToml()
    {
        using var fixture = new CursorFixture();
        fixture.WriteText("config.toml", "version=1");
        var path = fixture.Write("themes/custom/pointer.cur", TestCursorFiles.Cur());
        using var monitor = new FileMonitor(fixture.DirectoryPath);
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.Changed += () => changed.TrySetResult();
        File.Delete(path);
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(File.Exists(path));
    }
}
