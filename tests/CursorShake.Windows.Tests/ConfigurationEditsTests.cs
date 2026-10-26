using CursorShake.Windows.Configuration;
using Xunit;

namespace CursorShake.Windows.Tests;

public sealed class ConfigurationEditsTests
{
    [Theory]
    [InlineData("version=1\n[\"appearance\"]\ntheme=\"windows\"\n# retain")]
    [InlineData("version=1\nappearance.theme=\"windows\"\n# retain")]
    public void QuotedOrDottedAppearanceIsPatchedCorrectlyOrRejectedWithoutWriting(string original)
    {
        using var fixture = new CursorFixture();
        var store = new WorkspaceStore(Path.Combine(fixture.DirectoryPath, "source"), Path.Combine(fixture.DirectoryPath, "state"), persistLocator: false);
        store.EnsureConfiguration();
        File.WriteAllText(store.ConfigPath, original);
        var before = store.Read();
        try
        {
            var updated = store.ProtectedUpdate(text => ConfigurationEdits.SetTheme(text, "nordzy-white"), before.Revision);
            Assert.Equal("nordzy-white", ConfigParser.Parse(updated.Text).Theme);
            Assert.Contains("# retain", updated.Text);
        }
        catch (ConfigurationException exception)
        {
            Assert.Equal("Conflict", exception.Code);
            Assert.Equal(original, File.ReadAllText(store.ConfigPath));
        }
    }

    [Fact]
    public void StandardAppearanceEditPreservesCommentsLineEndingsAndUnrelatedOptions()
    {
        const string original = "version = 1\r\n# top\r\n[appearance]\r\n\"theme\" = 'windows' # inline\r\n[gesture]\r\ncount = 5\r\n";
        var updated = ConfigurationEdits.SetTheme(original, "bibata-ice");
        Assert.Equal(original.Replace("'windows'", "\"bibata-ice\""), updated);
        Assert.Equal("bibata-ice", ConfigParser.Parse(updated).Theme);
        Assert.Equal(5, ConfigParser.Parse(updated).Gesture.CountToActivate);
    }
}
