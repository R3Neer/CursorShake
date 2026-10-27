using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using CursorShake.Windows.Configuration;
namespace CursorShake.App.Dialogs;

public abstract class DialogForm : Form
{
    private readonly Font dialogFont;
    protected DialogForm()
    {
        dialogFont=new Font("Segoe UI",10.5f);
        Font=dialogFont;AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
        StartPosition=FormStartPosition.CenterScreen;MinimizeBox=false;MaximizeBox=false;
        ShowInTaskbar=true;Padding=Padding.Empty;
    }
    protected override void OnShown(EventArgs e){base.OnShown(e);FitToScreen();DialogStyle.Apply(this);}
    protected override void OnDpiChanged(DpiChangedEventArgs e){base.OnDpiChanged(e);FitToScreen();DialogStyle.Apply(this);}
    private void FitToScreen()
    {
        var area=Screen.FromControl(this).WorkingArea;
        var available=new Size(Math.Max(1,area.Width-32),Math.Max(1,area.Height-40));
        MinimumSize=DialogStyle.FitWithin(MinimumSize,available);Size=DialogStyle.FitWithin(Size,available);
        Left=Math.Clamp(Left,area.Left,Math.Max(area.Left,area.Right-Width));Top=Math.Clamp(Top,area.Top,Math.Max(area.Top,area.Bottom-Height));
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if(!SystemInformation.HighContrast&&OperatingSystem.IsWindowsVersionAtLeast(10,0,22000))
        {int corner=2;_=DwmSetWindowAttribute(Handle,33,ref corner,sizeof(int));}
        DialogStyle.Apply(this);
    }
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if((m.Msg==0x001A||m.Msg==0x031A)&&IsHandleCreated&&!IsDisposed)
        {
            try{BeginInvoke(new Action(()=>{if(!IsDisposed){Appearance.WindowsAppearance.Refresh();DialogStyle.Apply(this);}}));}
            catch(InvalidOperationException){}
        }
    }
    protected override void Dispose(bool disposing){base.Dispose(disposing);if(disposing)dialogFont.Dispose();}
    [DllImport("dwmapi.dll")]private static extern int DwmSetWindowAttribute(nint window,int attribute,ref int value,int size);
}

internal static class DialogStyle
{
    internal sealed record Palette(Color Surface,Color Raised,Color Text,Color Muted,Color Border,Color Accent,Color AccentText,bool HighContrast);
    internal static Palette Colors
    {
        get
        {
            bool dark=Application.ColorMode==SystemColorMode.Dark||(Application.ColorMode==SystemColorMode.System&&Application.SystemColorMode==SystemColorMode.Dark);
            Color accent=dark?Color.FromArgb(96,205,255):Color.FromArgb(0,103,192);
            return CreatePalette(dark,SystemInformation.HighContrast,accent);
        }
    }
    internal static Palette CreatePalette(bool dark,bool highContrast,Color accent)
    {
        if(highContrast)return new(SystemColors.Window,SystemColors.Window,SystemColors.WindowText,SystemColors.WindowText,SystemColors.WindowFrame,SystemColors.Highlight,SystemColors.HighlightText,true);
        Color surface=dark?Color.FromArgb(32,32,32):Color.FromArgb(250,250,250);
        if(Contrast(accent,surface)<4.5)accent=dark?Color.FromArgb(96,205,255):Color.FromArgb(0,103,192);
        Color accentText=Contrast(Color.White,accent)>=Contrast(Color.Black,accent)?Color.White:Color.Black;
        return dark?new(surface,Color.FromArgb(43,43,43),Color.FromArgb(245,245,245),Color.FromArgb(195,195,195),Color.FromArgb(75,75,75),accent,accentText,false)
            :new(surface,Color.White,Color.FromArgb(27,27,27),Color.FromArgb(85,85,85),Color.FromArgb(205,205,205),accent,accentText,false);
    }
    internal static double Contrast(Color a,Color b)
    {
        static double L(Color c){static double Linear(double x){x/=255;return x<=.04045?x/12.92:Math.Pow((x+.055)/1.055,2.4);}return .2126*Linear(c.R)+.7152*Linear(c.G)+.0722*Linear(c.B);}
        double left=L(a),right=L(b);return(Math.Max(left,right)+.05)/(Math.Min(left,right)+.05);
    }
    internal static Size FitWithin(Size desired,Size available)
    {
        if(available.Width<=0||available.Height<=0)throw new ArgumentOutOfRangeException(nameof(available));
        return new(Math.Clamp(desired.Width,1,available.Width),Math.Clamp(desired.Height,1,available.Height));
    }
    internal static Button CreateButton(string text,bool primary=false)=>new FluentButton{Text=text,Tag=primary?"primary":null,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,MinimumSize=new Size(96,36),Padding=new Padding(14,6,14,6),Margin=new Padding(8,0,0,0),UseVisualStyleBackColor=false};
    internal static Label CreateHeading(string text)
    {
        var font=new Font("Segoe UI",17,FontStyle.Regular);
        var label=new Label{Text=text,AutoSize=true,Dock=DockStyle.Fill,UseMnemonic=false,Font=font,Margin=new Padding(0,0,0,12)};
        label.Disposed+=(_,_)=>font.Dispose();return label;
    }
    internal static Label CreateBody(string text)=>new(){Text=text,AutoSize=true,Dock=DockStyle.Fill,UseMnemonic=false,Margin=new Padding(0,0,0,10)};
    internal static void Apply(Control root)
    {
        var p=Colors;
        void Visit(Control c)
        {
            if(c is Button b)
            {
                bool primary=Equals(b.Tag,"primary");b.FlatStyle=p.HighContrast?FlatStyle.Standard:FlatStyle.Flat;
                b.FlatAppearance.BorderSize=1;b.FlatAppearance.BorderColor=p.Border;
                b.BackColor=primary?p.Accent:p.Raised;b.ForeColor=primary?p.AccentText:p.Text;
            }
            else if(c is TextBoxBase){c.BackColor=p.Raised;c.ForeColor=p.Text;}
            else if(c is DataGridView grid)
            {
                grid.EnableHeadersVisualStyles=false;grid.BackgroundColor=p.Surface;grid.GridColor=p.Border;
                grid.DefaultCellStyle.BackColor=p.Raised;grid.DefaultCellStyle.ForeColor=p.Text;
                grid.DefaultCellStyle.SelectionBackColor=p.Accent;grid.DefaultCellStyle.SelectionForeColor=p.AccentText;
                grid.ColumnHeadersDefaultCellStyle.BackColor=p.Raised;grid.ColumnHeadersDefaultCellStyle.ForeColor=p.Text;
                grid.ColumnHeadersDefaultCellStyle.SelectionBackColor=p.Raised;grid.ColumnHeadersDefaultCellStyle.SelectionForeColor=p.Text;
                grid.AlternatingRowsDefaultCellStyle.BackColor=p.Surface;grid.AlternatingRowsDefaultCellStyle.ForeColor=p.Text;
                grid.ColumnHeadersHeight=Math.Max(30,(int)Math.Round(32*grid.DeviceDpi/96f));
                foreach(DataGridViewColumn column in grid.Columns){column.SortMode=DataGridViewColumnSortMode.NotSortable;column.MinimumWidth=Math.Max(32,(int)Math.Ceiling(TextRenderer.MeasureText(column.HeaderText,grid.Font).Width+40*grid.DeviceDpi/96f));}
            }
            else if(c is ListView list){list.BackColor=p.Raised;list.ForeColor=p.Text;}
            else if(c is ListBox){c.BackColor=p.Raised;c.ForeColor=p.Text;}
            else if(c is LinkLabel link){link.BackColor=p.Surface;link.ForeColor=p.Accent;link.LinkColor=p.Accent;link.ActiveLinkColor=p.Accent;link.VisitedLinkColor=p.Accent;}
            else{c.BackColor=p.Surface;c.ForeColor=Equals(c.Tag,"muted")?p.Muted:Equals(c.Tag,"warning-icon")&&!p.HighContrast?Color.DarkGoldenrod:Equals(c.Tag,"error-icon")&&!p.HighContrast?Color.IndianRed:p.Text;}
            foreach(Control child in c.Controls)Visit(child);
            c.Invalidate();
        }
        Visit(root);
    }
    internal static bool Confirm(IWin32Window owner,string title,string text,string confirmLabel,AppLanguage language)
    {
        using var dialog=new ConfirmationDialog(title,text,confirmLabel,language);
        return dialog.ShowDialog(owner)==DialogResult.OK;
    }
    private sealed class ConfirmationDialog:DialogForm
    {
        internal ConfirmationDialog(string title,string text,string confirm,AppLanguage language)
        {
            Text="CursorShake";Size=new Size(550,300);MinimumSize=new Size(420,250);StartPosition=FormStartPosition.CenterParent;ShowInTaskbar=false;
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=1,RowCount=3};
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(CreateHeading(title),0,0);
            var body=new Panel{Dock=DockStyle.Fill,AutoScroll=true,Margin=new Padding(0,0,0,12)};
            var description=CreateBody(text);description.Dock=DockStyle.Top;body.Controls.Add(description);layout.Controls.Add(body,0,1);
            body.SizeChanged+=(_,_)=>description.MaximumSize=new Size(Math.Max(100,body.ClientSize.Width-SystemInformation.VerticalScrollBarWidth),0);
            var buttons=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=true};
            var cancel=CreateButton(DiagnosticText.T(language,"Cancel","Cancelar"));cancel.DialogResult=DialogResult.Cancel;
            var yes=CreateButton(confirm.TrimEnd('…'),true);yes.DialogResult=DialogResult.OK;
            buttons.Controls.Add(cancel);buttons.Controls.Add(yes);CancelButton=cancel;AcceptButton=cancel;
            layout.Controls.Add(buttons,0,2);Controls.Add(layout);
            layout.SizeChanged+=(_,_)=>{foreach(var l in layout.Controls.OfType<Label>())l.MaximumSize=new Size(Math.Max(100,layout.ClientSize.Width-layout.Padding.Horizontal),0);};
        }
    }
    private sealed class FluentButton:Button
    {
        // This control paints its own accessible palette; WinForms must not darken it again.
        internal FluentButton() => SetStyle(ControlStyles.ApplyThemingImplicitly, false);
        protected override void OnPaint(PaintEventArgs e)
        {
            if(SystemInformation.HighContrast){base.OnPaint(e);return;}
            e.Graphics.Clear(Parent?.BackColor??BackColor);
            float radius=6*DeviceDpi/96f;var r=new RectangleF(.5f,.5f,Width-1,Height-1);using var shape=new GraphicsPath();float d=radius*2;
            shape.AddArc(r.Left,r.Top,d,d,180,90);shape.AddArc(r.Right-d,r.Top,d,d,270,90);shape.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);shape.AddArc(r.Left,r.Bottom-d,d,d,90,90);shape.CloseFigure();
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using var fill=new SolidBrush(Enabled?BackColor:Colors.Raised);e.Graphics.FillPath(fill,shape);
            using var border=new Pen(Focused?Colors.Accent:Colors.Border,Focused?2:1);e.Graphics.DrawPath(border,shape);
            TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,Enabled?ForeColor:Colors.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|(ShowKeyboardCues?0:TextFormatFlags.HidePrefix));
            if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-5,-5),ForeColor,BackColor);
        }
    }
}