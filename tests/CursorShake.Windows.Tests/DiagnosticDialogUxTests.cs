using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using CursorShake.App.Dialogs;
using CursorShake.Windows.Configuration;
namespace CursorShake.Windows.Tests;

public sealed class DiagnosticDialogUxTests
{
    [Fact] public void NewDiagnosticIsCompactAndKeepsStacksOutOfItsMainMessage()=>Sta(()=>
    {
        using var dialog=new DiagnosticDialog(new("Configuración","InvalidToml","RAW-SUMMARY",@"System.Exception: SECRET-STACK C:\Private\config.toml",@"C:\Private\config.toml",12));
        Assert.False(dialog.DetailsExpanded);AssertMainHasNoSecret(dialog);Assert.Contains("SECRET-STACK",dialog.TechnicalText);
        Assert.Contains("line 12",dialog.GuidanceText);
    });
    [Fact] public void LanguageChangesKeepTheUsersDisclosureChoice()=>Sta(()=>
    {
        using var dialog=new DiagnosticDialog(new("Configuración","InvalidConfiguration","Raw","SECRET-STACK"));
        string english=dialog.HeadingText;dialog.ToggleDetails();Assert.True(dialog.DetailsExpanded);
        dialog.SetLanguage(AppLanguage.Spanish);Assert.True(dialog.DetailsExpanded);Assert.NotEqual(english,dialog.HeadingText);Assert.Contains("Código:",dialog.TechnicalText);
        dialog.SetLanguage(AppLanguage.English);Assert.True(dialog.DetailsExpanded);Assert.Equal(english,dialog.HeadingText);Assert.Contains("Code:",dialog.TechnicalText);
        dialog.ToggleDetails();Assert.False(dialog.DetailsExpanded);
    });
    [Theory] [InlineData(AppLanguage.English)] [InlineData(AppLanguage.Spanish)]
    public void TomlErrorOffersItsConfigurationAndShowsTheLineBeforeDetails(AppLanguage language)=>Sta(()=>
    {
        string? opened=null;const string path=@"C:\Private\config.toml";
        using var dialog=new DiagnosticDialog(new("Configuración","InvalidToml","Raw","SECRET-STACK",path,27),openPath:p=>opened=p,language:language);
        Assert.False(dialog.DetailsExpanded);Assert.Contains(language==AppLanguage.Spanish?"línea 27":"line 27",dialog.GuidanceText);
        Assert.Equal(language==AppLanguage.Spanish?"Editar":"Edit",dialog.PrimaryActionText);
        ClickPrimary(dialog);Assert.Equal(path,opened);AssertMainHasNoSecret(dialog);
    });
    [Fact] public void OwnershipWarningDoesNotBlameConfigurationOrOfferBlindRetry()=>Sta(()=>
    {
        int opened=0,retried=0;
        using var dialog=new DiagnosticDialog(new("Cursor","CursorOwnershipLost","Raw","SECRET-STACK",@"C:\Private\config.toml"),_=>opened++,()=>retried++);
        Assert.Null(dialog.PrimaryActionText);Assert.DoesNotContain("TOML",dialog.HeadingText+dialog.ExplanationText+dialog.GuidanceText,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("View errors",dialog.GuidanceText);Assert.Contains("current cursors",dialog.GuidanceText);Assert.Equal(0,opened);Assert.Equal(0,retried);
    });
    [Fact] public void BusyFilePrioritizesRetryOverOpeningTheConfiguration()=>Sta(()=>
    {
        int opened=0,retried=0;
        using var dialog=new DiagnosticDialog(new("Configuración","Busy","Raw","SECRET-STACK",@"C:\Private\config.toml"),_=>opened++,()=>retried++);
        Assert.Equal("Retry",dialog.PrimaryActionText);ClickPrimary(dialog);Assert.Equal(1,retried);Assert.Equal(0,opened);
    });
    [Fact] public void ActionFailureShowsHumanFeedbackAndKeepsTheExceptionInTechnicalDetails()=>Sta(()=>
    {
        using var dialog=new DiagnosticDialog(new("Inicio","IOException","Raw","Initial evidence"));
        Invoke(dialog,"RunAction",(Action)(()=>throw new IOException("SECRET-ACTION-FAILURE")));
        Assert.Contains("SECRET-ACTION-FAILURE",dialog.TechnicalText);AssertMainHasNoSecret(dialog);
        string feedback=Feedback(dialog).Text;Assert.Contains("action could not be completed",feedback);Assert.DoesNotContain("SECRET",feedback);Assert.False(dialog.DetailsExpanded);
    });
    [Fact] public void SuccessfulAdditionalActionRemainsFinishedAcrossDisclosureAndLanguageChanges()=>Sta(()=>
    {
        int calls=0;var action=new DiagnosticAction("Review recovery","Confirm review",()=>{calls++;return Task.CompletedTask;});
        using var dialog=new DiagnosticDialog(new("Recuperación","RecoveryRequired","Raw","Evidence"),additionalAction:action);
        dialog.ConfirmAction=_=>true;var primary=Primary(dialog);InvokeTask(dialog,"RunAdditionalAction",action,primary).GetAwaiter().GetResult();
        Assert.Equal(1,calls);var feedback=Feedback(dialog);Assert.Contains("request has finished",feedback.Text);Assert.DoesNotContain("could not",feedback.Text);
        // Detach the real status label so Visible reflects its own state, not the never-shown parent.
        // This exercises the production refresh branch without showing a desktop window.
        var parent=feedback.Parent!;parent.Controls.Remove(feedback);
        try
        {
            dialog.ToggleDetails();dialog.SetLanguage(AppLanguage.Spanish);
            Assert.Contains("petición ha terminado",feedback.Text);Assert.DoesNotContain("No se ha podido",feedback.Text);
            dialog.SetLanguage(AppLanguage.English);Assert.Contains("request has finished",feedback.Text);Assert.DoesNotContain("could not",feedback.Text);
        }
        finally{parent.Controls.Add(feedback);}
    });
    [Fact] public void DecliningConfirmationDoesNotRunTheAdditionalAction()=>Sta(()=>
    {
        int calls=0;var action=new DiagnosticAction("Review recovery","Confirm review",()=>{calls++;return Task.CompletedTask;});
        using var dialog=new DiagnosticDialog(new("Recuperación","RecoveryRequired","Raw","Evidence"),additionalAction:action);
        dialog.ConfirmAction=_=>false;InvokeTask(dialog,"RunAdditionalAction",action,Primary(dialog)).GetAwaiter().GetResult();Assert.Equal(0,calls);
    });
    [Fact] public void CenterGroupsChangedStacksButRetainsLatestEvidenceAndSeparatePaths()=>Sta(()=>
    {
        int presentations=0;using var center=new DiagnosticCenter(presenter:_=>presentations++);
        var original=new UiDiagnostic("Configuración","InvalidToml","Raw","stack-one",@"C:\Data\one.toml",5,2);
        center.Report(original);center.Report(original with{Details="stack-two"});
        Assert.Equal(1,presentations);Assert.Equal("stack-two",Assert.Single(center.Current).Details);
        center.Report(original with{Path=@"C:\Data\two.toml",Details="stack-three"});Assert.Equal(2,presentations);Assert.Equal(2,center.Current.Count);
    });
    [Fact] public void AsyncRetryIsTrackedUntilTheShutdownBarrierCompletes()=>Sta(()=>
    {
        var pending=new TaskCompletionSource();int calls=0;
        using var center=new DiagnosticCenter(presenter:_=>{},retryAsync:()=>{calls++;return pending.Task;});
        Task running=InvokeTask(center,"InvokeRetryAsync");Assert.Equal(1,calls);Assert.False(center.PendingOperations.IsCompleted);
        SynchronizationContext.SetSynchronizationContext(null);Task shutdown=center.BeginShutdownAsync();Assert.False(shutdown.IsCompleted);
        pending.SetResult();running.GetAwaiter().GetResult();shutdown.GetAwaiter().GetResult();Assert.True(center.PendingOperations.IsCompleted);
        Assert.Throws<InvalidOperationException>(()=>InvokeTask(center,"InvokeRetryAsync").GetAwaiter().GetResult());Assert.Equal(1,calls);
    });
    [Fact] public void LocationActionResolvesTheContainingFolderWithoutCreatingAnything()
    {
        using var fixture=new CursorFixture();string file=fixture.WriteText("nested/config.toml","fixture");string folder=Path.GetDirectoryName(file)!;
        Assert.Equal(folder,DiagnosticDialog.FolderFor(file));Assert.Equal(folder,DiagnosticDialog.FolderFor(folder));
        string missing=Path.Combine(fixture.DirectoryPath,"missing","config.toml");Assert.Equal(Path.GetDirectoryName(missing),DiagnosticDialog.FolderFor(missing));Assert.False(Directory.Exists(Path.GetDirectoryName(missing)));
    }
    private static void AssertMainHasNoSecret(DiagnosticDialog dialog)
    {
        string text=dialog.HeadingText+dialog.ExplanationText+dialog.GuidanceText;
        Assert.DoesNotContain("SECRET",text);Assert.DoesNotContain("Exception",text);Assert.DoesNotContain(@"C:\",text);Assert.DoesNotContain("RAW-SUMMARY",text);
    }
    private static Label Feedback(DiagnosticDialog dialog)=>Children(dialog).OfType<Label>().Single(c=>c.Name=="ActionStatus");
    private static Button Primary(DiagnosticDialog dialog)=>Children(dialog).OfType<Button>().Single(c=>Equals(c.Tag,"primary"));
    private static void ClickPrimary(DiagnosticDialog dialog)
    {
        SynchronizationContext.SetSynchronizationContext(null);
        typeof(Button).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(Primary(dialog),[EventArgs.Empty]);
    }
    private static object? Invoke(object instance,string method,params object[] arguments)=>instance.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(instance,arguments);
    private static Task InvokeTask(object instance,string method,params object[] arguments)
    {SynchronizationContext.SetSynchronizationContext(null);return (Task)Invoke(instance,method,arguments)!;}
    private static IEnumerable<Control> Children(Control parent)=>parent.Controls.Cast<Control>().SelectMany(c=>new[]{c}.Concat(Children(c)));
    private static void Sta(Action action)
    {
        Exception? failure=null;var thread=new Thread(()=>{try{SynchronizationContext.SetSynchronizationContext(null);action();}catch(Exception error){failure=error;}});
        thread.IsBackground=true;thread.Name="CursorShake UX fixture";
        thread.SetApartmentState(ApartmentState.STA);thread.Start();Assert.True(thread.Join(TimeSpan.FromSeconds(10)),"STA UX test did not finish; no forms should be shown or message loop required.");
        if(failure is not null)ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
