using System.Drawing.Imaging;
using System.Text.Json;
using CursorShake.App.Dialogs;
using CursorShake.Windows.Configuration;
namespace CursorShake.NativeSmoke;
internal static class DialogUxPreview
{
    internal static int Run(string root)
    {
        Directory.CreateDirectory(root);Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        var results=new List<object>();
        foreach(var dark in new[]{true,false})foreach(var language in new[]{AppLanguage.Spanish,AppLanguage.English})
        {
            Application.SetColorMode(dark?SystemColorMode.Dark:SystemColorMode.Classic);
            var diagnostic=new UiDiagnostic("Configuración","InvalidToml","No se ha podido completar la operación.","System.IO.InvalidDataException: TOML parse error at line 4, column 12.\r\n   at CursorShake.Windows.Configuration.ConfigParser.Parse(...)\r\n   at CursorShake.App.AppHost.StartAsync()",@"C:\Example\CursorShake\config.toml",4,12){PreviousConfigurationActive=true};
            string key=(language==AppLanguage.Spanish?"es":"en")+(dark?"-dark":"-light");
            using(var dialog=new DiagnosticDialog(diagnostic,_=>{},language:language))
            {
                Draw(dialog,key+"-compact");bool collapsed=!dialog.DetailsExpanded;
                dialog.ToggleDetails();Draw(dialog,key+"-details");
                results.Add(new{key,CollapsedInitially=collapsed,dialog.HeadingText,dialog.PrimaryActionText,Expanded=dialog.DetailsExpanded});
            }
            using(var paused=new DiagnosticDialog(new UiDiagnostic("Cursor","CursorOwnershipLost","Raw","Technical evidence"),language:language))
            {Draw(paused,key+"-sole-close");}
            using(var recovery=new ConfigurationRecoveryDialog(new(@"C:\Example\CursorShake\config.toml",[@"C:\Example\CursorShake\before.toml",@"C:\Example\CursorShake\candidate.toml"],()=>Task.FromResult(new RecoveryReview("# Current configuration\r\n[appearance]\r\ntheme = \"bibata-ice\"","revision-example",true)),_=>Task.CompletedTask),_=>{},language))
            {Draw(recovery,key+"-recovery");}
            using(var list=new DiagnosticListDialog(_=>{},language,_=>3,_=>DateTimeOffset.Now))
            {list.SetDiagnostics([diagnostic,new UiDiagnostic("Inicio","IOException","technical","RecoveryGuard did not confirm readiness.")]);Draw(list,key+"-issues");}
        }
        File.WriteAllText(Path.Combine(root,"report.json"),JsonSerializer.Serialize(new{Passed=true,PrivateRenderingOnly=true,NoAppHost=true,NoSystemSettingsChanged=true,NoSystemCursorChanges=true,Runtime=Environment.Version.ToString(),Results=results},new JsonSerializerOptions{WriteIndented=true}));return 0;
        void Draw(Form form,string name)
        {
            form.CreateControl();form.PerformLayout();DialogStyle.Apply(form);
            var content=form.Controls[0];content.Font=form.Font;form.Controls.Remove(content);
            try
            {
                content.Visible=true;
                var visible=new List<Control>();void Collect(Control c){foreach(Control child in c.Controls){if(child.Visible){visible.Add(child);Collect(child);}}}Collect(content);
                content.SuspendLayout();foreach(var c in visible.AsEnumerable().Reverse())c.Visible=false;foreach(var c in visible)c.Visible=true;content.ResumeLayout(true);
                content.CreateControl();content.PerformLayout();
                using var bitmap=new Bitmap(content.Width,content.Height);content.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
                var colors=new HashSet<int>();for(int y=0;y<bitmap.Height;y+=3)for(int x=0;x<bitmap.Width;x+=3)colors.Add(bitmap.GetPixel(x,y).ToArgb());
                if(colors.Count<8)throw new InvalidOperationException("Preview rendering produced an empty surface: "+name);
                bitmap.Save(Path.Combine(root,name+".png"),ImageFormat.Png);
                var controls=new List<object>();void Describe(Control c){controls.Add(new{Type=c.GetType().Name,c.Name,Text=c is TextBoxBase?"technical":c.Text,c.Bounds,c.Visible});foreach(Control child in c.Controls)Describe(child);}Describe(content);File.WriteAllText(Path.Combine(root,name+"-layout.json"),JsonSerializer.Serialize(controls));
            }
            finally{form.Controls.Add(content);}
        }
    }
}