using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace CursorShake.NativeSmoke;

internal static class StandardPointerOwner
{
    internal static int Run(string readyPath, string stopName, int roleId = 32512)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        using var stop = EventWaitHandle.OpenExisting(stopName);
        using var form = new OwnerForm(readyPath, stop, roleId);
        Application.Run(form);
        return 0;
    }

    private sealed class OwnerForm : Form
    {
        private readonly string readyPath;
        private readonly EventWaitHandle stop;
        private readonly nint arrow;
        private readonly nint previousForeground;
        private readonly Native.Point previousPosition;
        private readonly System.Windows.Forms.Timer readyTimer = new() { Interval = 25 };
        private bool readyPublished;
        private int selections;
        internal OwnerForm(string readyPath, EventWaitHandle stop, int roleId)
        {
            this.readyPath = readyPath; this.stop = stop;
            arrow = Native.LoadCursor(0, (nint)roleId);
            if (arrow == 0) throw new InvalidOperationException("Cannot load standard shared arrow.");
            Native.GetCursorPos(out previousPosition); previousForeground = Native.GetForegroundWindow();
            Text = "CursorShake native QA pointer owner";
            TopMost = true; ShowInTaskbar = false; FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual; ClientSize = new Size(320, 180);
            var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1024, 768);
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
            BackColor = Color.White;
            readyTimer.Tick += (_, _) => PublishReadyWhenSelected();
        }
        protected override void OnShown(EventArgs args)
        {
            base.OnShown(args);
            Native.SetForegroundWindow(Handle);
            var center = PointToScreen(new Point(ClientSize.Width / 2, ClientSize.Height / 2));
            Native.SetCursorPos(center.X, center.Y);
            // The owner asks its own WndProc to select the role once. No engine code calls SetCursor.
            Native.SendMessage(Handle, 0x20, Handle, (nint)(1 | (0x200 << 16)));
            readyTimer.Start();
            _ = Task.Run(() =>
            {
                stop.WaitOne(TimeSpan.FromSeconds(90));
                try { if (!IsDisposed) BeginInvoke(Close); } catch (InvalidOperationException) { }
            });
        }
        protected override void OnPaint(PaintEventArgs args)
        {
            base.OnPaint(args);
            args.Graphics.DrawString("Independent shared-arrow owner\nWindows selects the cursor here.\nNo periodic SetCursor calls.", Font, Brushes.Black, new PointF(18, 18));
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x20)
            {
                Native.SetCursor(arrow); selections++; message.Result = 1; return;
            }
            base.WndProc(ref message);
        }
        private void PublishReadyWhenSelected()
        {
            if (readyPublished) return;
            var info = new Native.CursorInfo { Size = Marshal.SizeOf<Native.CursorInfo>() };
            bool observed=Native.GetCursorInfo(ref info);
            string observation=readyPath+".observation.json";
            if(!File.Exists(observation))File.WriteAllText(observation,JsonSerializer.Serialize(new {Observed=observed,Expected=arrow.ToInt64(),Actual=info.Cursor.ToInt64(),info.Flags,Visible,Window=Handle.ToInt64(),Foreground=Native.GetForegroundWindow().ToInt64(),Selections=selections,info.Position.X,info.Position.Y}));
            if (!observed || info.Cursor != arrow) return;
            readyPublished = true; readyTimer.Stop();
            string temporary = readyPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new { Pid = Environment.ProcessId, WindowHandle = Handle.ToInt64(), SharedArrow = arrow.ToInt64(), SelectedCursor = info.Cursor.ToInt64(), SelectionsBeforeReady = selections, TimerReselectsCursor = false, info.Position.X, info.Position.Y }));
            File.Move(temporary, readyPath);
        }
        protected override void OnFormClosed(FormClosedEventArgs args)
        {
            readyTimer.Stop(); readyTimer.Dispose();
            Native.SetCursorPos(previousPosition.X, previousPosition.Y);
            if (previousForeground != 0) Native.SetForegroundWindow(previousForeground);
            base.OnFormClosed(args);
        }
    }

    internal sealed class Client : IDisposable
    {
        private readonly EventWaitHandle stop;
        private readonly Process process;
        private bool disposed;
        private readonly int pid;
        internal string ReadyPath { get; }
        internal int Pid => pid;
        internal Client(string root, int roleId = 32512)
        {
            string directory = Path.Combine(root, "pointer-owner-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory); ReadyPath = Path.Combine(directory, "ready.json");
            string name = "Local\\CursorShake.NativeSmoke.PointerOwner." + Guid.NewGuid().ToString("N");
            stop = new EventWaitHandle(false, EventResetMode.ManualReset, name);
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Test host executable is unavailable.");
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Normal };
            start.ArgumentList.Add("--pointer-owner"); start.ArgumentList.Add(ReadyPath); start.ArgumentList.Add(name); start.ArgumentList.Add(roleId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start independent cursor owner.");
            pid = process.Id;
            try
            {
                using var watcher = new FileSystemWatcher(directory) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite, EnableRaisingEvents = true };
                var timeout = Stopwatch.StartNew();
                while (!File.Exists(ReadyPath))
                {
                    if (process.HasExited) throw new InvalidOperationException("Independent pointer owner exited before selecting the shared cursor.");
                    if (timeout.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Independent pointer owner did not confirm cursor selection.");
                    watcher.WaitForChanged(WatcherChangeTypes.All, 250);
                }
            }
            catch { Dispose(); throw; }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                stop.Set();
                if (!process.WaitForExit(5000)) throw new TimeoutException("Independent pointer owner did not close; its90secondtimeout remains armed.");
            }
            finally { process.Dispose(); stop.Dispose(); }
        }
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct CursorInfo { public int Size, Flags; public nint Cursor; public Point Position; }
        [DllImport("user32.dll", EntryPoint = "LoadCursorW")] internal static extern nint LoadCursor(nint instance, nint name);
        [DllImport("user32.dll")] internal static extern nint SetCursor(nint cursor);
        [DllImport("user32.dll")] internal static extern bool GetCursorInfo(ref CursorInfo info);
        [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point position);
        [DllImport("user32.dll")] internal static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint window);
        [DllImport("user32.dll", EntryPoint = "SendMessageW")] internal static extern nint SendMessage(nint window, uint message, nint w, nint l);
    }
}
