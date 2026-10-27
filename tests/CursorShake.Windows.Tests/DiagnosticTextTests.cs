using CursorShake.App.Dialogs;
using CursorShake.Windows.Configuration;
namespace CursorShake.Windows.Tests;

public sealed class DiagnosticTextTests
{
    [Theory]
    [InlineData("InvalidToml")] [InlineData("InvalidConfiguration")] [InlineData("UnsupportedVersion")] [InlineData("LimitExceeded")]
    [InlineData("DestinationConflict")] [InlineData("InvalidDestination")] [InlineData("UnsafePath")] [InlineData("Busy")]
    [InlineData("Conflict")] [InlineData("RecoveryRequired")] [InlineData("ThemeApplyFailed")] [InlineData("CursorOwnershipLost")]
    [InlineData("StopFailed")] [InlineData("StartupRegistrationFailed")]
    public void KnownFailuresHaveTwoUserFacingPresentationsWithoutRawEvidence(string code)
    {
        var diagnostic=new UiDiagnostic("Configuración",code,"RAW-SUMMARY-Exception",@"RAW-DETAIL C:\Private\secret.toml System.IO.IOException stack",@"C:\Private\secret.toml");
        var english=DiagnosticText.Present(diagnostic,AppLanguage.English);var spanish=DiagnosticText.Present(diagnostic,AppLanguage.Spanish);
        Assert.NotEqual(english.Title,spanish.Title);
        foreach(var presentation in new[]{english,spanish})
        {
            string text=presentation.Title+presentation.Explanation+presentation.Guidance;
            Assert.False(string.IsNullOrWhiteSpace(presentation.Title));Assert.False(string.IsNullOrWhiteSpace(presentation.Explanation));Assert.False(string.IsNullOrWhiteSpace(presentation.Guidance));
            Assert.DoesNotContain("RAW-",text);Assert.DoesNotContain("Exception",text);Assert.DoesNotContain(@"C:\",text);Assert.DoesNotContain("secret.toml",text);Assert.DoesNotContain(code,text);
        }
    }
    [Theory] [InlineData(AppLanguage.English)] [InlineData(AppLanguage.Spanish)]
    public void PreviousConfigurationStateRequiresAnExplicitFact(AppLanguage language)
    {
        var diagnostic=new UiDiagnostic("Configuración","InvalidToml","Raw","Raw");
        var unknown=DiagnosticText.Present(diagnostic,language);
        var active=DiagnosticText.Present(diagnostic with{PreviousConfigurationActive=true},language);
        var inactive=DiagnosticText.Present(diagnostic with{PreviousConfigurationActive=false},language);
        Assert.NotEqual(unknown.Explanation,active.Explanation);Assert.NotEqual(unknown.Explanation,inactive.Explanation);
        Assert.Contains(DiagnosticText.T(language,"previous configuration remains active","configuración anterior sigue activa"),active.Explanation);
        Assert.Contains(DiagnosticText.T(language,"There was no previous configuration to keep","No había una configuración anterior que conservar"),inactive.Explanation);
        Assert.DoesNotContain(DiagnosticText.T(language,"previous configuration","configuración anterior"),unknown.Explanation);
        // False records the state when the error occurred, not whether defaults or another revision loaded later.
        Assert.DoesNotContain("currently active",inactive.Explanation,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("actualmente",inactive.Explanation,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("defaults",inactive.Explanation,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("por defecto",inactive.Explanation,StringComparison.OrdinalIgnoreCase);
    }
    [Theory] [InlineData("ThemeApplyFailed")] [InlineData("CursorOwnershipLost")]
    public void CursorFailuresDoNotBlameTomlOrOfferEditingItEvenWhenTheCapturedPathIsToml(string code)
    {
        var diagnostic=new UiDiagnostic("Configuración",code,"Raw","Raw",@"D:\Data\config.toml");
        foreach(var language in new[]{AppLanguage.English,AppLanguage.Spanish})
        {
            var presentation=DiagnosticText.Present(diagnostic,language);string text=presentation.Title+presentation.Explanation+presentation.Guidance;
            Assert.DoesNotContain("TOML",text,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("format",text,StringComparison.OrdinalIgnoreCase);Assert.Null(presentation.FileActionLabel);Assert.Equal(DiagnosticFileAction.None,presentation.FileAction);
        }
    }
    [Theory] [InlineData(AppLanguage.English)] [InlineData(AppLanguage.Spanish)]
    public void ConfigurationActionIsOfferedOnlyForRelevantTomlPaths(AppLanguage language)
    {
        var diagnostic=new UiDiagnostic("Configuración","InvalidConfiguration","Raw","Raw",@"D:\Data\config.toml");
        Assert.Equal(DiagnosticText.T(language,"Edit","Editar"),DiagnosticText.Present(diagnostic,language).FileActionLabel);
        Assert.Equal(DiagnosticText.T(language,"Open folder","Abrir carpeta"),DiagnosticText.Present(diagnostic with{Path=@"D:\Data\assets"},language).FileActionLabel);
        Assert.Null(DiagnosticText.Present(diagnostic with{Path=null},language).FileActionLabel);
        Assert.Equal(DiagnosticFileAction.OpenConfiguration,DiagnosticText.Present(diagnostic,language).FileAction);
        Assert.Equal(DiagnosticFileAction.OpenFolder,DiagnosticText.Present(diagnostic with{Path=@"D:\Data\assets"},language).FileAction);
        Assert.Equal(DiagnosticFileAction.None,DiagnosticText.Present(diagnostic with{Path=null},language).FileAction);
    }
    [Fact] public void UnknownAreaNeverLeaksArbitraryTextAndInitializationCanRetry()
    {
        var unknown=DiagnosticText.Present(new(@"C:\Private\System.Exception","Unexpected", "RAW-SUMMARY", "RAW-DETAIL"),AppLanguage.English);
        Assert.Equal("The operation could not be completed",unknown.Title);Assert.DoesNotContain(@"C:\",unknown.Title+unknown.Explanation+unknown.Guidance);Assert.False(unknown.CanRetry);
        var startup=DiagnosticText.Present(new("Inicio","IOException","RAW-SUMMARY","RAW-DETAIL"),AppLanguage.English);
        Assert.True(startup.CanRetry);Assert.Contains("could not start",startup.Title);Assert.DoesNotContain("configuration",startup.Guidance,StringComparison.OrdinalIgnoreCase);
    }
    [Fact] public void RecoveryAndOwnershipWarningsNeverRecommendBlindRetry()
    {
        foreach(string code in new[]{"RecoveryRequired","CursorOwnershipLost","StopFailed"})
        {var presentation=DiagnosticText.Present(new("Recuperación",code,"Raw","Raw"));Assert.True(presentation.Warning);Assert.False(presentation.CanRetry);}
        Assert.False(DiagnosticText.Present(new("Configuración","DestinationConflict","Raw","Raw")).CanRetry);
        Assert.False(DiagnosticText.Present(new("Inicio con Windows","StartupRegistrationFailed","Raw","Raw")).CanRetry);
        Assert.True(DiagnosticText.Present(new("Inicio","Busy","Raw","Raw")).CanRetry);
    }
    [Theory] [InlineData("InvalidToml")] [InlineData("InvalidConfiguration")]
    public void ReportedLineIsVisibleWithoutOpeningTechnicalDetails(string code)
    {
        var diagnostic=new UiDiagnostic("Configuración",code,"Raw","Raw",@"C:\Private\config.toml",27,4);
        Assert.Contains("line 27",DiagnosticText.Present(diagnostic,AppLanguage.English).Guidance);
        Assert.Contains("línea 27",DiagnosticText.Present(diagnostic,AppLanguage.Spanish).Guidance);
    }
    [Fact] public void OwnershipGuidanceDoesNotSendTheUserBackToTheSameErrorList()
    {
        var diagnostic=new UiDiagnostic("Cursor","CursorOwnershipLost","Raw","Raw");
        var english=DiagnosticText.Present(diagnostic,AppLanguage.English);var spanish=DiagnosticText.Present(diagnostic,AppLanguage.Spanish);
        Assert.DoesNotContain("View errors",english.Guidance);Assert.DoesNotContain("Ver errores",spanish.Guidance);
        Assert.Contains("current cursors",english.Guidance);Assert.Contains("close CursorShake",english.Guidance);
        Assert.Contains("cursores actuales",spanish.Guidance);Assert.Contains("cerrar CursorShake",spanish.Guidance);
    }
    [Fact] public void ThemeFailureOffersAThemeActionWithoutInventingItsCause()
    {
        var presentation=DiagnosticText.Present(new("Configuración","ThemeApplyFailed","Raw","Raw"),AppLanguage.English);
        Assert.Contains("Choose another theme",presentation.Guidance);Assert.Contains("personal theme",presentation.Guidance);
        Assert.DoesNotContain("corrupt",presentation.Explanation,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("invalid",presentation.Explanation,StringComparison.OrdinalIgnoreCase);
    }
    [Fact] public void CopyableTechnicalTextLocalizesLabelsWhilePreservingTheActualEvidence()
    {
        var diagnostic=new UiDiagnostic("Configuration","InvalidToml","Raw summary","RAW-STACK",@"C:\Data\config.toml",7,2,["before.toml","candidate.toml"]);
        string english=diagnostic.CopyText(),spanish=diagnostic.CopyText(AppLanguage.Spanish);
        Assert.Contains("Code: InvalidToml",english);Assert.Contains(@"Location: C:\Data\config.toml:7:2",english);Assert.Contains("Preserved files:",english);
        Assert.Contains("Código: InvalidToml",spanish);Assert.Contains(@"Ubicación: C:\Data\config.toml:7:2",spanish);Assert.Contains("Archivos conservados:",spanish);
        foreach(string text in new[]{english,spanish}){Assert.Contains("RAW-STACK",text);Assert.Contains("before.toml",text);Assert.Contains("candidate.toml",text);}
    }
}
