using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace CursorShake.App.Appearance;

public static class WindowsAppearance
{
    public static void Initialize() => Application.SetColorMode(SystemColorMode.System);
    public static void Refresh()
    {
        var mode=SystemInformation.HighContrast?SystemColorMode.Classic:Application.SystemColorMode;
        if(Application.ColorMode!=mode)Application.SetColorMode(mode);
    }
    public static void StyleMenu(ToolStripDropDown menu)
    {
        Refresh();
        StyleMenu(menu,Application.SystemColorMode==SystemColorMode.Dark,SystemInformation.HighContrast);
    }
    // Explicit appearance parameters also allow rendering fixtures without changing Windows settings.
    public static void StyleMenu(ToolStripDropDown menu,bool dark,bool highContrast)
    {
        var colors=MenuColors.For(dark,highContrast);
        float scale=menu.DeviceDpi/96f;
        int Px(int value)=>Math.Max(1,(int)Math.Round(value*scale));
        menu.Renderer=highContrast?new ToolStripSystemRenderer():new MenuRenderer(colors);
        menu.BackColor=colors.Surface;menu.ForeColor=colors.Text;
        menu.Padding=new Padding(Px(5));
        foreach(ToolStripItem item in menu.Items)
        {
            item.ForeColor=colors.Text;
            if(item is ToolStripSeparator)item.Margin=new Padding(Px(10),Px(4),Px(10),Px(4));
            else item.Padding=new Padding(Px(8),Px(5),Px(8),Px(5));
            if(item is ToolStripMenuItem child && child.HasDropDownItems)StyleMenu(child.DropDown,dark,highContrast);
        }
        menu.Invalidate(true);
    }
    public static void PreferRoundedCorners(ToolStripDropDown menu)
    {
        if(SystemInformation.HighContrast||!OperatingSystem.IsWindowsVersionAtLeast(10,0,22000))return;
        int preference=2;
        _=DwmSetWindowAttribute(menu.Handle,33,ref preference,sizeof(int));
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window,int attribute,ref int value,int size);
    private sealed record MenuColors(Color Surface,Color Text,Color Muted,Color Selected,Color Border)
    {
        public static MenuColors For(bool dark,bool highContrast)=>highContrast
            ?new(SystemColors.Menu,SystemColors.MenuText,SystemColors.GrayText,SystemColors.Highlight,SystemColors.WindowFrame)
            :dark?new(Color.FromArgb(43,43,43),Color.FromArgb(245,245,245),Color.FromArgb(160,160,160),Color.FromArgb(65,65,65),Color.FromArgb(75,75,75))
            :new(Color.FromArgb(249,249,249),Color.FromArgb(27,27,27),Color.FromArgb(110,110,110),Color.FromArgb(232,232,232),Color.FromArgb(210,210,210));
    }
    private sealed class MenuRenderer(MenuColors colors):ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {using var brush=new SolidBrush(colors.Surface);e.Graphics.FillRectangle(brush,e.AffectedBounds);}
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {using var brush=new SolidBrush(colors.Surface);e.Graphics.FillRectangle(brush,e.AffectedBounds);}
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {using var pen=new Pen(colors.Border);e.Graphics.DrawRectangle(pen,0,0,e.ToolStrip.Width-1,e.ToolStrip.Height-1);}
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if(!e.Item.Enabled||!e.Item.Selected&&!e.Item.Pressed)return;
            float scale=e.ToolStrip?.DeviceDpi/96f??1;
            var rect=new RectangleF(2*scale,1*scale,e.Item.Width-4*scale,e.Item.Height-2*scale);
            if(rect.Width<=0||rect.Height<=0)return;
            using var shape=Rounded(rect,4*scale);using var brush=new SolidBrush(colors.Selected);
            var previous=e.Graphics.SmoothingMode;e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            e.Graphics.FillPath(brush,shape);e.Graphics.SmoothingMode=previous;
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {e.TextColor=e.Item.Enabled?colors.Text:colors.Muted;base.OnRenderItemText(e);}
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {e.ArrowColor=e.Item?.Enabled==false?colors.Muted:colors.Text;base.OnRenderArrow(e);}
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {using var pen=new Pen(colors.Border);int y=e.Item.Height/2;e.Graphics.DrawLine(pen,4,y,e.Item.Width-5,y);}
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var box=e.ImageRectangle;float unit=Math.Min(box.Width,box.Height)/16f;
            using var pen=new Pen(e.Item.Enabled?colors.Text:colors.Muted,Math.Max(1.5f,1.7f*unit)){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round};
            var previous=e.Graphics.SmoothingMode;e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            e.Graphics.DrawLines(pen,[new PointF(box.X+3*unit,box.Y+8*unit),new PointF(box.X+6.5f*unit,box.Y+11.5f*unit),new PointF(box.X+13*unit,box.Y+4.5f*unit)]);
            e.Graphics.SmoothingMode=previous;
        }
        private static GraphicsPath Rounded(RectangleF r,float radius)
        {
            var path=new GraphicsPath();float d=radius*2;
            path.AddArc(r.Left,r.Top,d,d,180,90);path.AddArc(r.Right-d,r.Top,d,d,270,90);
            path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);path.AddArc(r.Left,r.Bottom-d,d,d,90,90);path.CloseFigure();return path;
        }
    }
}
