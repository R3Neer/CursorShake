using CursorShake.Windows.Configuration;
using CursorShake.Windows.Cursors;
using CursorShake.Windows.Cursors.Formats;
using Xunit;

namespace CursorShake.Windows.Tests;

public sealed class ThemePreparerTests
{
    [Theory]
    [InlineData("windows")]
    [InlineData("bibata-ice")]
    [InlineData("nordzy-white")]
    public void EveryBundledRoleAndUniqueFrameCanBeLoadedAndRasterized(string id)
    {
        using var fixture = new CursorFixture();
        using var theme = new ThemePreparer(BundledRoot).Prepare(new AppConfiguration { Theme = id }, fixture.DirectoryPath);
        Assert.Equal(id, theme.Id);
        Assert.Equal(17, theme.Roles.Count);
        Assert.Equal(CursorRoles.All.OrderBy(x => x), theme.Roles.Keys.OrderBy(x => x));
        Assert.InRange(theme.EstimatedBytes, 1, CursorAssetLoader.MaximumSourceBytes);
        foreach (var asset in theme.Roles.Values.Distinct())
        {
            Assert.NotEmpty(asset.Frames);
            foreach (var frame in asset.Frames)
            {
                var pixels = frame.Rasterize(32, 32);
                Assert.Equal(32 * 32 * 4, pixels.Bgra.Length);
                Assert.InRange(pixels.HotspotX, 0, 31);
                Assert.InRange(pixels.HotspotY, 0, 31);
            }
        }
    }

    [Theory]
    [InlineData("windows")]
    [InlineData("bibata-ice")]
    [InlineData("nordzy-white")]
    public void BusyAndWorkingUseTheirOwnFrameTimelinesAndLoop(string id)
    {
        using var fixture = new CursorFixture();
        using var theme = new ThemePreparer(BundledRoot).Prepare(new AppConfiguration { Theme = id }, fixture.DirectoryPath);
        foreach (var role in new[] { CursorRole.Wait, CursorRole.Working })
        {
            var asset = theme.Roles[role];
            Assert.True(asset.IsAnimated);
            Assert.All(asset.Steps, step => Assert.True(step.Duration > TimeSpan.Zero));
            Assert.True(asset.Duration > TimeSpan.Zero);
            Assert.Equal(asset.Steps[0].FrameIndex, asset.FrameIndexAt(TimeSpan.Zero));
            Assert.Equal(asset.Steps[1].FrameIndex, asset.FrameIndexAt(asset.Steps[0].Duration));
            Assert.Equal(asset.Steps[0].FrameIndex, asset.FrameIndexAt(asset.Duration));
        }
    }

    [Fact]
    public void MissingRoleUsesThePersonalThemeDefaultBeforeWindows()
    {
        using var fixture = new CursorFixture();
        fixture.Write("themes/custom/pointer.vcur", TestCursorFiles.VectorArchive("version=1\nimage=\"p.svg\"\nhotspot=[4,2]", ("p.svg", TestCursorFiles.ArrowSvg)));
        fixture.Write("themes/custom/text.cur", TestCursorFiles.Cur());
        var definition = new PersonalThemeDefinition { DefaultCursor = "pointer.vcur", Roles = new Dictionary<CursorRole, string> { [CursorRole.Text] = "text.cur" } };
        using var theme = new ThemePreparer(BundledRoot).Prepare(Custom(definition), fixture.DirectoryPath);
        Assert.Same(theme.Roles[CursorRole.Arrow], theme.Roles[CursorRole.Wait]);
        Assert.True(theme.Roles[CursorRole.Wait].Frames[0].IsVector);
        Assert.False(theme.Roles[CursorRole.Wait].IsAnimated);
        Assert.NotSame(theme.Roles[CursorRole.Arrow], theme.Roles[CursorRole.Text]);
        Assert.False(theme.Roles[CursorRole.Text].Frames[0].IsVector);
    }

    [Fact]
    public void MissingPersonalDefaultFallsBackToWindowsForTheSpecificRole()
    {
        using var fixture = new CursorFixture();
        fixture.Write("themes/custom/text.cur", TestCursorFiles.Cur());
        var definition = new PersonalThemeDefinition { Roles = new Dictionary<CursorRole, string> { [CursorRole.Text] = "text.cur" } };
        using var theme = new ThemePreparer(BundledRoot).Prepare(Custom(definition), fixture.DirectoryPath);
        Assert.True(theme.Roles[CursorRole.Wait].IsAnimated);
        Assert.False(theme.Roles[CursorRole.Text].Frames[0].IsVector);
        Assert.NotSame(theme.Roles[CursorRole.Wait], theme.Roles[CursorRole.Arrow]);
    }

    [Fact]
    public void InvalidExplicitAssociationIsAnErrorRatherThanAReasonToFallback()
    {
        using var fixture = new CursorFixture();
        fixture.Write("themes/custom/broken.cur", [0, 1, 2]);
        var definition = new PersonalThemeDefinition { Roles = new Dictionary<CursorRole, string> { [CursorRole.Text] = "broken.cur" } };
        Assert.Throws<CursorAssetException>(() => new ThemePreparer(BundledRoot).Prepare(Custom(definition), fixture.DirectoryPath));
    }

    [Fact]
    public void InactiveBrokenThemeDoesNotPreventPreparingTheSelectedTheme()
    {
        using var fixture = new CursorFixture();
        var config = Custom(new PersonalThemeDefinition { DefaultCursor = "absent.vcur" }) with { Theme = "windows" };
        using var theme = new ThemePreparer(BundledRoot).Prepare(config, fixture.DirectoryPath);
        Assert.Equal(17, theme.Roles.Count);
        Assert.Equal("windows", theme.Id);
    }

    [Fact]
    public void CancellationDoesNotReturnAPartiallyPreparedTheme()
    {
        using var fixture = new CursorFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => new ThemePreparer(BundledRoot).Prepare(new AppConfiguration(), fixture.DirectoryPath, cancellation.Token));
    }

    [Fact]
    public void PersonalResourcesCannotEscapeTheirThemeFolder()
    {
        using var fixture = new CursorFixture();
        var definition = new PersonalThemeDefinition { DefaultCursor = "../outside.cur" };
        Assert.Throws<ConfigurationException>(() => new ThemePreparer(BundledRoot).Prepare(Custom(definition), fixture.DirectoryPath));
    }

    private static AppConfiguration Custom(PersonalThemeDefinition definition) => new() { Theme = "custom", PersonalThemes = new Dictionary<string, PersonalThemeDefinition> { ["custom"] = definition } };
    private static string BundledRoot
    {
        get
        {
            for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "CursorShake.slnx")))
                {
                    string root = Path.Combine(directory.FullName, "artifacts", "generated-themes");
                    Assert.True(Directory.Exists(root), "Prepare bundled themes before running theme integration tests.");
                    return root;
                }
            throw new InvalidOperationException("Cannot locate repository root.");
        }
    }
}
