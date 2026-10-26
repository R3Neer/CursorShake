using CursorShake.Windows.Configuration;
namespace CursorShake.Windows.Tests;

public sealed class ConfigLanguageTests
{
    [Fact] public void OmittedLanguageAndNewConfigurationDefaultToEnglish()
    {
        Assert.Equal(AppLanguage.English,new AppConfiguration().Language);
        Assert.Equal(AppLanguage.English,ConfigParser.Parse("version=1").Language);
    }
    [Theory] [InlineData("english",AppLanguage.English)] [InlineData("spanish",AppLanguage.Spanish)]
    public void ExactRootTokensSelectTheLanguage(string token,AppLanguage expected)
        =>Assert.Equal(expected,ConfigParser.Parse($"version=1\nlanguage=\"{token}\"\n[appearance]\ntheme=\"windows\"").Language);
    [Theory]
    [InlineData("\"English\"")] [InlineData("\"SPANISH\"")] [InlineData("\"es\"")] [InlineData("\" spanish\"")] [InlineData("\"\"")]
    [InlineData("true")] [InlineData("1")] [InlineData("[\"english\"]")]
    public void InvalidLanguageIsRejectedWithLocationAndNeverSilentlyDefaulted(string value)
    {
        const string source="language-fixture.toml";
        var error=Assert.Throws<ConfigurationException>(()=>ConfigParser.Parse("version=1\nlanguage="+value,source));
        Assert.Equal("InvalidConfiguration",error.Code);Assert.Equal(source,error.SourcePath);Assert.Equal(2,error.Line);
    }
    [Fact] public void TemplateDeclaresEnglishAtTheRootAndKeepsVersionOne()
    {
        string text=ConfigurationDefaults.Text;var parsed=ConfigParser.Parse(text);
        Assert.Equal(1,parsed.Version);Assert.Equal(AppLanguage.English,parsed.Language);
        Assert.True(text.IndexOf("language = \"english\"",StringComparison.Ordinal)<text.IndexOf("[appearance]",StringComparison.Ordinal));
    }
    [Fact] public void LanguageUnderAppearanceDoesNotBecomeARootSetting()
        =>Assert.Throws<ConfigurationException>(()=>ConfigParser.Parse("[appearance]\nlanguage=\"spanish\""));
}
