using CursorShake.Windows.Configuration;
namespace CursorShake.App.Dialogs;

public sealed class DiagnosticDialog : DialogForm
{
    private readonly TableLayoutPanel layout,header;
    private readonly FlowLayoutPanel detailLinks;
    private bool measuring;
    private readonly Label heading,explanation,guidance,feedback,severity;
    private readonly LinkLabel disclosure,copyDetails;
    private readonly RichTextBox details;
    private readonly FlowLayoutPanel buttons;
    private readonly Action<string>? openPath;
    private readonly Action? retry;
    private readonly Func<Task>? retryAsync;
    private Action? resolveRecovery;
    private DiagnosticAction? additionalAction;
    private AppLanguage language;
    private bool _actionRunning,expanded;
    private string? actionFailure;
    private enum FeedbackState{None,Working,Completed,Failed}
    private FeedbackState feedbackState;
    private int compactHeight;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Func<DiagnosticAction,bool>? ConfirmAction {get;set;}
    public UiDiagnostic Diagnostic {get;private set;}
    internal bool DetailsExpanded=>expanded;
    internal string HeadingText=>heading.Text;
    internal string ExplanationText=>explanation.Text;
    internal string GuidanceText=>guidance.Text;
    internal string TechnicalText=>details.Text;
    internal string? PrimaryActionText=>buttons.Controls.OfType<Button>().FirstOrDefault(b=>Equals(b.Tag,"primary"))?.Text;
    public DiagnosticDialog(UiDiagnostic diagnostic,Action<string>? openPath=null,Action? retry=null,Action? resolveRecovery=null,DiagnosticAction? additionalAction=null,AppLanguage language=AppLanguage.English,Func<Task>? retryAsync=null)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);Diagnostic=diagnostic;
        this.openPath=openPath;this.retry=retry;this.retryAsync=retryAsync;this.resolveRecovery=resolveRecovery;this.additionalAction=additionalAction;this.language=language;
        Name="DiagnosticDialog";Text="CursorShake";ClientSize=new Size(600,320);MinimumSize=new Size(480,300);
        layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=1,RowCount=7};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        for(int i=0;i<7;i++)layout.RowStyles.Add(new RowStyle(i==5?SizeType.Percent:SizeType.AutoSize,i==5?100:0));
        heading=DialogStyle.CreateHeading("");heading.Name="UserHeading";
        severity=new Label{AutoSize=true,Margin=new Padding(0,4,10,0),UseMnemonic=false,AccessibleRole=AccessibleRole.Graphic};
        var iconFont=new Font("Segoe Fluent Icons",20);severity.Font=iconFont;severity.Disposed+=(_,_)=>iconFont.Dispose();
        header=new TableLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));header.Controls.Add(severity,0,0);header.Controls.Add(heading,1,0);
        explanation=DialogStyle.CreateBody("");explanation.Name="UserExplanation";
        guidance=DialogStyle.CreateBody("");guidance.Name="UserGuidance";guidance.Tag="muted";
        feedback=DialogStyle.CreateBody("");feedback.Name="ActionStatus";feedback.Visible=false;feedback.AccessibleRole=AccessibleRole.StaticText;
        disclosure=new LinkLabel{AutoSize=true,Margin=new Padding(0,4,0,16),LinkBehavior=LinkBehavior.HoverUnderline,TabStop=true};
        disclosure.LinkClicked+=(_,_)=>ToggleDetails();
        copyDetails=new LinkLabel{AutoSize=true,Margin=new Padding(18,4,0,16),LinkBehavior=LinkBehavior.HoverUnderline,Visible=false,TabStop=true};

        detailLinks=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty,MinimumSize=new Size(0,48)};detailLinks.Controls.Add(disclosure);detailLinks.Controls.Add(copyDetails);
        details=new RichTextBox{ReadOnly=true,Multiline=true,WordWrap=false,ScrollBars=RichTextBoxScrollBars.Both,Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,Visible=false,DetectUrls=false,Name="TechnicalDetails",Margin=new Padding(0,0,0,18)};
        copyDetails.LinkClicked+=(_,_)=>RunAction(()=>Clipboard.SetText(details.Text));
        var mono=new Font("Consolas",10);details.Font=mono;details.Disposed+=(_,_)=>mono.Dispose();
        buttons=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Margin=Padding.Empty,Padding=new Padding(0,8,0,0)};
        layout.Controls.Add(header,0,0);layout.Controls.Add(explanation,0,1);layout.Controls.Add(guidance,0,2);layout.Controls.Add(feedback,0,3);layout.Controls.Add(detailLinks,0,4);layout.Controls.Add(details,0,5);layout.Controls.Add(buttons,0,6);Controls.Add(layout);
        layout.SizeChanged+=(_,_)=>MeasureSections();heading.FontChanged+=(_,_)=>MeasureSections();severity.FontChanged+=(_,_)=>MeasureSections();disclosure.FontChanged+=(_,_)=>MeasureSections();
        SetLanguage(language);
    }
    public void SetLanguage(AppLanguage value)
    {
        language=value;var copy=DiagnosticText.Present(Diagnostic,language);
        heading.Text=copy.Title;explanation.Text=copy.Explanation;guidance.Text=copy.Guidance;
        severity.Text=copy.Warning?"\uE7BA":"\uE783";severity.Tag=copy.Warning?"warning-icon":"error-icon";severity.AccessibleName=copy.Warning?T("Warning","Aviso"):T("Error","Error");
        AccessibleName=copy.Title;disclosure.Text=expanded?T("Hide details","Ocultar detalles"):T("View details","Ver detalles");
        copyDetails.Text=T("Copy","Copiar");copyDetails.AccessibleName=T("Copy technical details","Copiar detalles técnicos");copyDetails.Visible=expanded;
        disclosure.AccessibleName=disclosure.Text;details.AccessibleName=T("Technical details, read only","Detalles técnicos, solo lectura");
                if(feedback.Visible)feedback.Text=feedbackState switch
        {
            FeedbackState.Working=>T("Working…","Realizando la acción…"),
            FeedbackState.Completed=>T("The request has finished. Check whether the issue has been resolved.","La petición ha terminado. Comprueba si se ha resuelto la incidencia."),
            _=>T("The action could not be completed. Try again, or copy the technical details if the problem continues.","No se ha podido completar la acción. Vuelve a intentarlo o copia los detalles técnicos si el problema continúa.")
        };
        RefreshTechnicalText();BuildButtons(copy);DialogStyle.Apply(this);MeasureSections();EnsureCompactFits();
    }
    internal void UpdateDiagnostic(UiDiagnostic diagnostic,Action? recovery=null,DiagnosticAction? action=null){Diagnostic=diagnostic;resolveRecovery=recovery;additionalAction=action;SetLanguage(language);}
    private string T(string en,string es)=>DiagnosticText.T(language,en,es);
    private void RefreshTechnicalText()=>details.Text=Diagnostic.CopyText(language)+(actionFailure is null?"":Environment.NewLine+Environment.NewLine+T("Action failure:","Fallo de la acción:")+Environment.NewLine+actionFailure);
    private void BuildButtons(DiagnosticPresentation copy)
    {
        while(buttons.Controls.Count>0){var c=buttons.Controls[0];buttons.Controls.RemoveAt(0);c.Dispose();}
        var close=DialogStyle.CreateButton(T("Close","Cerrar"));close.DialogResult=DialogResult.Cancel;close.Click+=(_,_)=>Close();buttons.Controls.Add(close);CancelButton=close;AcceptButton=close;
        Button? primary=null;
        if(additionalAction is not null)
        {
            primary=DialogStyle.CreateButton(additionalAction.Label,true);
            primary.Click+=async(_,_)=>await RunAdditionalAction(additionalAction,primary);
        }
        else if(resolveRecovery is not null)
        {primary=DialogStyle.CreateButton(T("Review configuration","Revisar configuración"),true);primary.Click+=(_,_)=>RunAction(resolveRecovery);}
        else if(copy.CanRetry&&(retry is not null||retryAsync is not null))
        {primary=DialogStyle.CreateButton(T("Retry","Reintentar"),true);primary.Click+=async(_,_)=>await RunOperation(retryAsync??(()=>{retry!();return Task.CompletedTask;}));}
        else if(copy.FileActionLabel is not null&&Diagnostic.Path is{} file&&openPath is not null)
        {
            bool configuration=copy.FileAction==DiagnosticFileAction.OpenConfiguration;
            primary=DialogStyle.CreateButton(copy.FileActionLabel,true);
            primary.Click+=(_,_)=>RunAction(()=>openPath(configuration?file:FolderFor(file)));
        }
        if(primary is not null){primary.Enabled=!_actionRunning;buttons.Controls.Add(primary);if(additionalAction is null)AcceptButton=primary;}
    }
    internal static string FolderFor(string path){string full=Path.GetFullPath(path);return Directory.Exists(full)?full:Path.GetDirectoryName(full)??full;}
    internal void ToggleDetails()
    {
        if(!expanded){compactHeight=Height;expanded=true;details.Visible=true;Height=Math.Min(Screen.FromControl(this).WorkingArea.Height-40,Height+240);}
        else{expanded=false;details.Visible=false;Height=Math.Max(MinimumSize.Height,compactHeight);}
        SetLanguage(language);
    }
    private void MeasureSections()
    {
        if(measuring)return;measuring=true;
        try
        {
            int width=Math.Max(100,layout.ClientSize.Width-layout.Padding.Horizontal);
            int iconWidth=severity.GetPreferredSize(Size.Empty).Width+severity.Margin.Horizontal;
            heading.MaximumSize=new Size(Math.Max(80,width-iconWidth),0);
            foreach(var label in new[]{explanation,guidance,feedback})label.MaximumSize=new Size(width,0);
            header.MinimumSize=new Size(0,Math.Max(heading.GetPreferredSize(new Size(Math.Max(80,width-iconWidth),0)).Height+heading.Margin.Vertical,severity.GetPreferredSize(Size.Empty).Height+severity.Margin.Vertical));
            detailLinks.MinimumSize=new Size(0,Math.Max(disclosure.GetPreferredSize(Size.Empty).Height+disclosure.Margin.Vertical,copyDetails.GetPreferredSize(Size.Empty).Height+copyDetails.Margin.Vertical));
            buttons.MinimumSize=new Size(0,buttons.Controls.Count==0?0:buttons.Controls.Cast<Control>().Max(c=>c.GetPreferredSize(Size.Empty).Height+c.Margin.Vertical)+buttons.Padding.Vertical);
        }
        finally{measuring=false;}
    }
    private void EnsureCompactFits()
    {
        if(expanded)return;
        layout.PerformLayout();
        int needed=layout.Padding.Vertical+new Control[]{header,explanation,guidance,feedback,detailLinks,buttons}.Where(c=>c.Visible||c==header||c==explanation||c==guidance||c==detailLinks||c==buttons).Sum(c=>c.GetPreferredSize(new Size(Math.Max(100,layout.ClientSize.Width-layout.Padding.Horizontal),0)).Height+c.Margin.Vertical);
        int max=(Screen.PrimaryScreen?.WorkingArea.Height??900)-80;
        if(ClientSize.Height<needed)ClientSize=new Size(ClientSize.Width,Math.Min(max,needed+8));
    }
    private async Task RunAdditionalAction(DiagnosticAction action,Button button)
    {
        if(_actionRunning||IsDisposed)return;
        bool confirmed=ConfirmAction?.Invoke(action)??DialogStyle.Confirm(this,action.Label.TrimEnd('…')+"?",action.ConfirmationText,action.Label,language);
        if(!confirmed)return;
        await RunOperation(action.Execute);
    }
    private async Task RunOperation(Func<Task> action)
    {
        if(_actionRunning||IsDisposed)return;
        _actionRunning=true;feedbackState=FeedbackState.Working;feedback.Visible=true;feedback.Text=T("Working…","Realizando la acción…");SetButtonsBusy(true);EnsureCompactFits();
        try{await action();if(!IsDisposed){feedbackState=FeedbackState.Completed;feedback.Text=T("The request has finished. Check whether the issue has been resolved.","La petición ha terminado. Comprueba si se ha resuelto la incidencia.");}}
        catch(Exception error){if(!IsDisposed)ShowActionFailure(error);}
        finally{_actionRunning=false;if(!IsDisposed)SetButtonsBusy(false);}
    }
    private void SetButtonsBusy(bool busy){foreach(var b in buttons.Controls.OfType<Button>())if(Equals(b.Tag,"primary"))b.Enabled=!busy;}
    private void RunAction(Action action)
    {if(_actionRunning||IsDisposed)return;try{action();}catch(Exception error){ShowActionFailure(error);}}
    private void ShowActionFailure(Exception error)
    {
        feedbackState=FeedbackState.Failed;actionFailure=error.ToString();RefreshTechnicalText();feedback.Visible=true;
        feedback.Text=T("The action could not be completed. Try again, or copy the technical details if the problem continues.","No se ha podido completar la acción. Vuelve a intentarlo o copia los detalles técnicos si el problema continúa.");
        EnsureCompactFits();
    }
}