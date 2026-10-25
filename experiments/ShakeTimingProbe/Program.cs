using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

record Sample(double EventMs, double ReceivedMs, int X, int Y);
record Turn(double Ms, double Position, double? IntervalMs, double ExcursionPx);
record Analysis(double AxisX, double AxisY, double ThresholdPx, int Samples, Turn[] Turns,
    double? MedianMs, double? P90Ms, double? P95Ms, double? MinimumMs, double? MaximumMs);
static class Analyzer
{
    public static double Quantile(double[] values, double p)
    {
        var a = values.Order().ToArray(); double index = (a.Length - 1) * p;
        int i = (int)index; return a[i] + (a[Math.Min(i + 1, a.Length - 1)] - a[i]) * (index - i);
    }
    public static Analysis Analyze(IReadOnlyList<Sample> samples, double threshold)
    {
        if (samples.Count < 2) return new(1, 0, threshold, samples.Count, [], null, null, null, null, null);
        double mx = samples.Average(s => (double)s.X), my = samples.Average(s => (double)s.Y);
        double xx = 0, xy = 0, yy = 0;
        foreach (var s in samples) { double x = s.X - mx, y = s.Y - my; xx += x * x; xy += x * y; yy += y * y; }
        double angle = .5 * Math.Atan2(2 * xy, xx - yy), ax = Math.Cos(angle), ay = Math.Sin(angle);
        double Project(Sample s) => (s.X - mx) * ax + (s.Y - my) * ay;
        double start = Project(samples[0]), extreme = start, extremeTime = samples[0].EventMs, legStart = start;
        int direction = 0; List<Turn> turns = [];
        foreach (var s in samples.Skip(1))
        {
            double p = Project(s);
            if (direction == 0)
            {
                if (Math.Abs(p - start) >= threshold) { direction = p > start ? 1 : -1; extreme = p; extremeTime = s.EventMs; }
                continue;
            }
            if ((p - extreme) * direction >= 0) { extreme = p; extremeTime = s.EventMs; continue; }
            if ((extreme - p) * direction < threshold) continue;
            double excursion = Math.Abs(extreme - legStart);
            turns.Add(new(extremeTime, extreme, turns.Count == 0 ? null : extremeTime - turns[^1].Ms, excursion));
            legStart = extreme; direction = -direction; extreme = p; extremeTime = s.EventMs;
        }
        double[] intervals = turns.Where(t => t.IntervalMs > 0).Select(t => t.IntervalMs!.Value).ToArray();
        return new(ax, ay, threshold, samples.Count, turns.ToArray(),
            intervals.Length == 0 ? null : Quantile(intervals, .5), intervals.Length == 0 ? null : Quantile(intervals, .9),
            intervals.Length == 0 ? null : Quantile(intervals, .95), intervals.Length == 0 ? null : intervals.Min(), intervals.Length == 0 ? null : intervals.Max());
    }
    public static void SelfTest()
    {
        foreach (var axis in new[] { (1.0, 0.0), (0.0, 1.0), (.70710678, .70710678) })
        {
            var data = Enumerable.Range(0, 1201).Select(i => { double x = 100 * Math.Cos(2 * Math.PI * i / 400); return new Sample(i, i, (int)Math.Round(x * axis.Item1), (int)Math.Round(x * axis.Item2)); }).ToArray();
            var a = Analyze(data, 8);
            if (a.Turns.Length < 4 || a.MedianMs is not double med || Math.Abs(med - 200) > 8) throw new Exception("Known 200 ms half-period failed");
        }
        var quiet = Enumerable.Range(0, 100).Select(i => new Sample(i, i, i % 2, 0)).ToArray();
        if (Analyze(quiet, 8).Turns.Length != 0) throw new Exception("Jitter rejected incorrectly");
        // No temporal ceiling: slow turns must remain measurable.
        var slow = Enumerable.Range(0, 5001).Select(i => new Sample(i, i, (int)Math.Round(100 * Math.Cos(2 * Math.PI * i / 1600)), 0)).ToArray();
        if (Analyze(slow, 8).MedianMs is not double m || Math.Abs(m - 800) > 16) throw new Exception("Slow turns were lost");
    }
}
static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct MouseData { public Point Point; public uint Mouse, Flags, Time; public UIntPtr Extra; }
    public delegate IntPtr Callback(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookExW(int id, Callback callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandleW(string? module);
}
sealed class ProbeForm : Form
{
    readonly Label status = new() { AutoSize = false, Height = 62, Dock = DockStyle.Top, TextAlign = ContentAlignment.MiddleLeft };
    readonly Label live = new() { AutoSize = false, Height = 45, Dock = DockStyle.Top };
    readonly TextBox output = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    readonly Button start = new() { Text = "Prueba guiada: 4 ritmos", AutoSize = true };
    readonly Button stop = new() { Text = "Terminar antes", AutoSize = true, Enabled = false };
    readonly NumericUpDown threshold = new() { Minimum = 2, Maximum = 64, Value = 8, Width = 65 };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 100 };
    readonly List<Sample> samples = [];
    readonly Native.Callback callback;
    readonly string logRoot;
    IntPtr hook; Stopwatch clock = new(); uint startTick; DateTimeOffset countdownUntil; bool countdown, recording; string? session;
    double activeThreshold;
    readonly string[] stageNames = ["MEDIO · primera repetición", "LENTO · el que NO debería activar", "MUY RÁPIDO", "MEDIO · segunda repetición"];
    readonly string[] stageTags = ["medium-1", "slow", "fast", "medium-2"];
    int stageIndex; string guidedRunId = ""; const double StageSeconds = 6;
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public ProbeForm(string root)
    {
        logRoot = root; Directory.CreateDirectory(logRoot);
        Text = "CursorShake · medir ritmo (experimento descartable)"; Width = 930; Height = 650; MinimumSize = new(730, 520);
        Font = new("Segoe UI", 11); StartPosition = FormStartPosition.CenterScreen;
        var intro = new Label { Dock = DockStyle.Top, Height = 86, Text = "Una sola prueba guiada: MEDIO → LENTO → MUY RÁPIDO → MEDIO. Cada ritmo dura 6 segundos, con 3 segundos de preparación.\r\nSigue el nombre grande del ritmo. Agita aproximadamente en línea recta; puedes salir de esta ventana. No pulses botones.\r\nSe cuentan inversiones; una ida y vuelta contiene dos. No hay límite de intervalo ni velocidad mínima." };
        var controls = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 52, WrapContents = false, AutoScroll = true };
        controls.Controls.Add(start); controls.Controls.Add(stop); controls.Controls.Add(new Label { Text = "Filtro de temblor:", AutoSize = true, Padding = new(12, 7, 0, 0) }); controls.Controls.Add(threshold); controls.Controls.Add(new Label { Text = "px físicos", AutoSize = true, Padding = new(0, 7, 0, 0) });
        var folder = new Button { Text = "Abrir logs", AutoSize = true }; controls.Controls.Add(folder);
        var footer = new TextBox { Dock = DockStyle.Bottom, Height = 56, Multiline = true, ReadOnly = true, Text = "Logs locales (cada prueba conserva posiciones e intervalos):\r\n" + logRoot };
        Controls.Add(output); Controls.Add(live); Controls.Add(status); Controls.Add(controls); Controls.Add(intro); Controls.Add(footer);
        status.Font = new Font("Segoe UI", 16, FontStyle.Bold); status.Text = "Preparado. Pulsa Prueba guiada cuando quieras.";
        output.Text = "El filtro solo evita contar pequeños temblores. No es el detector definitivo.\r\nGuardaremos también el movimiento para poder revisar el filtro después.\r\n\r\nHaremos cuatro tramos etiquetados automáticamente para comparar ritmos sin tener que recordar cuándo cambiaste.";
        callback = OnMouse;
        start.Click += (_, _) => { stageIndex = 0; guidedRunId = Guid.NewGuid().ToString("N"); BeginCountdown(); }; stop.Click += (_, _) => Finish("manual");
        folder.Click += (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", logRoot) { UseShellExecute = true });
        timer.Tick += (_, _) => Tick(); timer.Start();
        FormClosing += (_, _) => { if (recording || countdown) Finish("window-closed"); timer.Stop(); if (hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(hook); hook = IntPtr.Zero; } };
    }
    void BeginCountdown()
    {
        if (recording || countdown) return;
        samples.Clear(); activeThreshold = (double)threshold.Value;
        session = Path.Combine(logRoot, DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)); Directory.CreateDirectory(session);
        File.WriteAllText(Path.Combine(session, "status.json"), JsonSerializer.Serialize(new { State = "countdown", GuidedRunId = guidedRunId, StageIndex = stageIndex, Stage = stageTags[stageIndex], Started = DateTimeOffset.Now, ThresholdPx = activeThreshold }, Json));
        countdownUntil = DateTimeOffset.UtcNow.AddSeconds(3); countdown = true; start.Enabled = threshold.Enabled = false; stop.Enabled = true;
    }
    IntPtr OnMouse(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && recording && message.ToInt64() == 0x200)
        {
            var d = Marshal.PtrToStructure<Native.MouseData>(data);
            if ((d.Flags & 1) == 0) samples.Add(new(unchecked(d.Time - startTick), clock.Elapsed.TotalMilliseconds, d.Point.X, d.Point.Y));
        }
        return Native.CallNextHookEx(hook, code, message, data);
    }
    void Tick()
    {
        if (countdown)
        {
            var left = (countdownUntil - DateTimeOffset.UtcNow).TotalSeconds;
            status.Text = $"{stageNames[stageIndex]} · empieza en {Math.Max(1, (int)Math.Ceiling(left))}";
            if (left <= 0)
            {
                countdown = false; hook = Native.SetWindowsHookExW(14, callback, Native.GetModuleHandleW(null), 0);
                if (hook == IntPtr.Zero) { Finish("hook-error-" + Marshal.GetLastWin32Error()); return; }
                startTick = unchecked((uint)Environment.TickCount64); clock.Restart(); recording = true;
                File.WriteAllText(Path.Combine(session!, "status.json"), JsonSerializer.Serialize(new { State = "recording", GuidedRunId = guidedRunId, StageIndex = stageIndex, Stage = stageTags[stageIndex], Started = DateTimeOffset.Now }, Json));
            }
        }
        if (!recording) return;
        status.Text = $"{stageNames[stageIndex]} · quedan {Math.Max(0, StageSeconds - clock.Elapsed.TotalSeconds):0.0} s";
        // Analyze a small recent window for display; the final result uses the complete trial.
        var recent = samples.Where(s => s.ReceivedMs >= clock.Elapsed.TotalMilliseconds - 2000).Select(s => s with { EventMs = s.ReceivedMs }).ToArray();
        var a = Analyzer.Analyze(recent, activeThreshold);
        live.Text = $"Muestras: {samples.Count:N0} · intervalo mediano reciente: {Ms(a.MedianMs)} · estimación provisional";
        if (clock.Elapsed.TotalSeconds >= StageSeconds) Finish("completed");
    }
    static string Ms(double? value) => value is double x ? x.ToString("0", CultureInfo.CurrentCulture) + " ms" : "—";
    void Finish(string reason)
    {
        bool wasRecording = recording; recording = countdown = false;
        if (hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        clock.Stop(); start.Enabled = threshold.Enabled = true; stop.Enabled = false;
        if (session == null) return;
        try
        {
            var highResolution = samples.Select(s => s with { EventMs = s.ReceivedMs }).ToArray();
            var a = Analyzer.Analyze(highResolution, activeThreshold);
            using (var csv = new StreamWriter(Path.Combine(session, "movement.csv"), false, Encoding.UTF8))
            {
                csv.WriteLine("event_ms,received_ms,x_physical_px,y_physical_px");
                foreach (var s in samples) csv.WriteLine(FormattableString.Invariant($"{s.EventMs:0.###},{s.ReceivedMs:0.###},{s.X},{s.Y}"));
            }
            using (var csv = new StreamWriter(Path.Combine(session, "intervals.csv"), false, Encoding.UTF8))
            {
                csv.WriteLine("turn,peak_received_ms,interval_ms,excursion_physical_px");
                for (int i = 0; i < a.Turns.Length; i++) { var t = a.Turns[i]; csv.WriteLine(FormattableString.Invariant($"{i + 1},{t.Ms:0.###},{t.IntervalMs:0.###},{t.ExcursionPx:0.###}")); }
            }
            var sensitivity = new[] { 4d, 8d, 16d }.Select(t => Analyzer.Analyze(highResolution, t)).ToArray();
            File.WriteAllText(Path.Combine(session, "result.json"), JsonSerializer.Serialize(new { Experiment = "Shake timing only; not product detector", Reason = reason, GuidedRunId = guidedRunId, StageIndex = stageIndex, Stage = stageTags[stageIndex], IntervalClock = "Stopwatch at hook reception", Finished = DateTimeOffset.Now, DurationMs = wasRecording ? clock.Elapsed.TotalMilliseconds : 0, NoMaximumIntervalFilter = true, NoSpeedFilter = true, CoordinateUnit = "physical screen pixels", Final = a, WindowsEventClock = Analyzer.Analyze(samples, activeThreshold), ThresholdSensitivity = sensitivity }, Json));
            File.WriteAllText(Path.Combine(session, "status.json"), JsonSerializer.Serialize(new { State = "finished", Reason = reason, GuidedRunId = guidedRunId, Stage = stageTags[stageIndex], Samples = samples.Count }, Json));
            status.Text = $"Prueba guardada · {a.Turns.Length} inversiones · mediana {Ms(a.MedianMs)}";
            live.Text = $"P90: {Ms(a.P90Ms)} · P95: {Ms(a.P95Ms)} · mínimo: {Ms(a.MinimumMs)} · máximo: {Ms(a.MaximumMs)}";
            output.Text = $"Resultado: {reason}\r\nEje principal detectado: {Math.Atan2(a.AxisY, a.AxisX) * 180 / Math.PI:0}°\r\n\r\n" + string.Join("\r\n", a.Turns.Select((t, i) => $"Inversión {i + 1,3} · instante {t.Ms,8:0} ms · intervalo {Ms(t.IntervalMs),9} · recorrido {t.ExcursionPx,6:0.0} px")) + "\r\n\r\nGuardado en: " + session + (a.Turns.Length < 4 ? "\r\nPocas inversiones: repite con algo más de recorrido o reduce el filtro." : "\r\nPrueba guardada. Los intervalos largos también están guardados, sin descartarlos.");
        }
        catch (Exception e) { status.Text = "No se pudo guardar la prueba"; output.Text = session + "\r\n" + e; return; }
        if (reason == "completed" && stageIndex < stageNames.Length - 1) { stageIndex++; BeginCountdown(); }
        else if (reason == "completed") status.Text = "CUATRO RITMOS GUARDADOS · ya puedes avisar en el chat";
    }
}
static class Program
{
    [STAThread] static void Main(string[] args)
    {
        if (args.Contains("--self-test")) { Analyzer.SelfTest(); File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test-passed.txt"), "Known 200/800 ms intervals, horizontal/vertical/diagonal and jitter checks passed."); return; }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../artifacts/sessions"));
        Application.Run(new ProbeForm(root));
    }
}
