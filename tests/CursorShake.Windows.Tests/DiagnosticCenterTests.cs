using System.Runtime.ExceptionServices;
using CursorShake.App.Dialogs;
using Xunit;

namespace CursorShake.Windows.Tests;

public sealed class DiagnosticCenterTests
{
    [Fact]
    public void RepeatedErrorsProduceOnlyOnePresentationButRemainAvailable() => Sta(() =>
    {
        var presented = new List<UiDiagnostic>();
        using var center = new DiagnosticCenter(presenter: presented.Add);
        var diagnostic = new UiDiagnostic("configuration", "InvalidToml", "Invalid configuration", "Expected number", "C:/test/config.toml", 7, 3);
        for (int i = 0; i < 100; i++) center.Report(diagnostic);
        Assert.Single(presented);
        Assert.Equal(diagnostic, Assert.Single(center.Current));
        Assert.True(center.HasErrors);
    });

    [Fact]
    public void BoundedCurrentDiagnosticsKeepTheNewestFiftyDistinctFailures() => Sta(() =>
    {
        int presented = 0;
        using var center = new DiagnosticCenter(presenter: _ => presented++);
        for (int i = 0; i < 60; i++) center.Report(new("resource", i.ToString(), "Failure", "Details " + i));
        Assert.Equal(50, center.Current.Count);
        Assert.Equal("10", center.Current[0].Code);
        Assert.Equal("59", center.Current[^1].Code);
        Assert.Equal(60, presented);
    });

    [Fact]
    public void ClearAreaOnlyRemovesResolvedDiagnosticsAndCanPresentThemAgainLater() => Sta(() =>
    {
        int presented = 0, changed = 0;
        using var center = new DiagnosticCenter(presenter: _ => presented++);
        center.Changed += () => changed++;
        var config = new UiDiagnostic("configuration", "Invalid", "Invalid file", "Details");
        center.Report(config);
        center.Report(new("startup", "Denied", "Startup error", "Other details"));
        center.ClearArea("configuration");
        Assert.Equal("startup", Assert.Single(center.Current).Area);
        Assert.Equal(3, changed);
        center.ClearArea("configuration");
        Assert.Equal(3, changed);
        center.Report(config);
        Assert.Equal(3, presented);
    });

    [Fact]
    public void DistinctResourcePathsDoNotHideEachOthersErrors() => Sta(() =>
    {
        using var center = new DiagnosticCenter(presenter: _ => { });
        center.Report(new("resource", "Missing", "Missing", "Missing file", "C:/themes/a.cur"));
        center.Report(new("resource", "Missing", "Missing", "Missing file", "C:/themes/b.cur"));
        center.Report(new("resource", "Missing", "Missing", "Missing file", "c:/THEMES/A.cur"));
        Assert.Equal(2, center.Current.Count);
    });

    [Fact]
    public void CopyableDiagnosticContainsSummaryLocationAndDetails()
    {
        var diagnostic = new UiDiagnostic("configuration", "InvalidToml", "Invalid configuration", "Expected number", "C:/test/config.toml", 7, 3);
        var text = diagnostic.CopyText();
        Assert.Contains("Invalid configuration", text);
        Assert.Contains("InvalidToml", text);
        Assert.Contains("C:/test/config.toml:7:3", text);
        Assert.Contains("Expected number", text);
    }

    [Fact]
    public void ReportingFromAnotherThreadIsRejectedBeforePresentation() => Sta(() =>
    {
        int presented = 0;
        using var center = new DiagnosticCenter(presenter: _ => presented++);
        var exception = Task.Run(() => Record.Exception(() => center.Report(new("a", "b", "c", "d")))).GetAwaiter().GetResult();
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(0, presented);
        Assert.Empty(center.Current);
    });

    [Fact]
    public void DisposalClearsStateAndRejectsFurtherReporting() => Sta(() =>
    {
        var center = new DiagnosticCenter(presenter: _ => { });
        center.Report(new("a", "b", "c", "d"));
        center.Dispose();
        center.Dispose();
        Assert.False(center.HasErrors);
        Assert.Empty(center.Current);
        Assert.Throws<ObjectDisposedException>(() => center.Report(new("a", "b", "c", "d")));
    });

    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "STA diagnostic test did not finish.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
