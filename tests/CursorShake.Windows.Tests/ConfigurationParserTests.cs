using CursorShake.Core.Shake;
using CursorShake.Windows.Configuration;
using CursorShake.Windows.Cursors;
using Xunit;

namespace CursorShake.Windows.Tests;

public sealed class ConfigurationParserTests
{
    [Fact]
    public void GeneratedTemplateMatchesTheAuthoritativeDomainDefaults()
    {
        var parsed = ConfigParser.Parse(ConfigurationDefaults.Text, "fixture.toml");
        var defaults = new GestureProfile();
        Assert.Equal(1, parsed.Version);
        Assert.Equal("windows", parsed.Theme);
        Assert.Equal(defaults, parsed.Gesture);
        Assert.Empty(parsed.PersonalThemes);
    }

    [Fact]
    public void PartialConfigurationChangesOnlyTheSpecifiedSetting()
    {
        var parsed = ConfigParser.Parse("version=1\n[gesture]\ncount=7", "fixture.toml");
        Assert.Equal(7, parsed.Gesture.CountToActivate);
        Assert.Equal(TimeSpan.FromMilliseconds(130), parsed.Gesture.MaximumInterval);
        Assert.Equal(16, parsed.Gesture.Growth.MaximumScale);
        Assert.Equal("windows", parsed.Theme);
    }

    [Fact]
    public void GestureAndGrowthOptionsAreParsedWithTheirDeclaredUnits()
    {
        const string text = """
            version = 1
            [appearance]
            theme = "nordzy-white"
            [gesture]
            count = 5
            maximum_interval_ms = 170
            minimum_travel = 22.5
            minimum_speed = 250
            [growth]
            maximum_scale = 10
            growth_duration_ms = 1600
            inactivity_delay_ms = 650
            return_duration_ms = 400
            maximum_cadence_multiplier = 1.25
            """;
        var parsed = ConfigParser.Parse(text, "fixture.toml");
        Assert.Equal("nordzy-white", parsed.Theme);
        Assert.Equal(5, parsed.Gesture.CountToActivate);
        Assert.Equal(TimeSpan.FromMilliseconds(170), parsed.Gesture.MaximumInterval);
        Assert.Equal(22.5, parsed.Gesture.MinimumTravel);
        Assert.Equal(250, parsed.Gesture.MinimumSpeed);
        Assert.Equal(10, parsed.Gesture.Growth.MaximumScale);
        Assert.Equal(TimeSpan.FromMilliseconds(1600), parsed.Gesture.Growth.GrowthDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(650), parsed.Gesture.Growth.InactivityDelay);
        Assert.Equal(TimeSpan.FromMilliseconds(400), parsed.Gesture.Growth.ReturnDuration);
        Assert.Equal(1.25, parsed.Gesture.Growth.MaximumCadenceMultiplier);
    }

    [Fact]
    public void PersonalThemeRetainsItsDefaultAndExplicitRoleAssociations()
    {
        const string text = """
            version = 1
            [appearance]
            theme = "custom"
            [themes.custom]
            default = "pointer.vcur"
            [themes.custom.roles]
            text = "text.cur"
            wait = "busy.vani"
            """;
        var parsed = ConfigParser.Parse(text, "fixture.toml");
        var theme = parsed.PersonalThemes["custom"];
        Assert.Equal("pointer.vcur", theme.DefaultCursor);
        Assert.Equal("text.cur", theme.Roles[CursorRole.Text]);
        Assert.Equal("busy.vani", theme.Roles[CursorRole.Wait]);
        Assert.False(theme.Roles.ContainsKey(CursorRole.Arrow));
    }

    [Theory]
    [InlineData("version=2")]
    [InlineData("version=1\nunknown=true")]
    [InlineData("version=1\n[gesture]\ncout=3")]
    [InlineData("version=1\n[gesture]\ncount=0")]
    [InlineData("version=1\n[gesture]\ncount=1.5")]
    [InlineData("version=1\n[gesture]\nmaximum_interval_ms=0")]
    [InlineData("version=1\n[gesture]\nminimum_travel=0")]
    [InlineData("version=1\n[gesture]\nminimum_speed=-1")]
    [InlineData("version=1\n[gesture]\nminimum_speed=nan")]
    [InlineData("version=1\n[growth]\nmaximum_scale=inf")]
    [InlineData("version=1\n[growth]\nmaximum_scale=0.5")]
    [InlineData("version=1\n[growth]\ngrowth_duration_ms=0")]
    [InlineData("version=1\n[growth]\nreturn_duration_ms=0")]
    [InlineData("version=1\n[growth]\nmaximum_cadence_multiplier=0.5")]
    [InlineData("version=1\n[growth]\ninactivity_delay_ms=100")]
    public void UnknownUnsupportedOrInvalidSettingsAreRejectedAsConfigurationErrors(string text)
    {
        var error = Assert.Throws<ConfigurationException>(() => ConfigParser.Parse(text, "fixture.toml"));
        Assert.Equal("fixture.toml", error.SourcePath);
    }

    [Fact]
    public void SyntaxDiagnosticCarriesSourceAndLocation()
    {
        var error = Assert.Throws<ConfigurationException>(() => ConfigParser.Parse("version=1\n[gesture]\ncount = [", "broken.toml"));
        Assert.Equal("broken.toml", error.SourcePath);
        Assert.True(error.Line > 0);
        Assert.True(error.Column > 0);
    }
}
