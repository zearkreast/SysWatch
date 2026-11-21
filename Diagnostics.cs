using System.Diagnostics;
using System.Text;
using LibreHardwareMonitor.Hardware;
using SysWatch.Monitoring;

namespace SysWatch;

// `SysWatch.exe --diag`: measures memory after each startup stage and writes %APPDATA%\SysWatch\diag.txt.
internal static class Diagnostics
{
    // `SysWatch.exe --glass-shot <png> [dark] [en|tr]`: captures the main window and tray popup over a full-screen backdrop
    // of its own, so the composited glass (and the README screenshots) never include anything else on screen.
    public static void GlassShot(string output, bool dark)
    {
        UI.Theme.ForceDark(dark);
        var area = Screen.PrimaryScreen!.Bounds;
        var backdrop = new Form { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Bounds = area, ShowInTaskbar = false, TopMost = true };
        backdrop.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var b = new System.Drawing.Drawing2D.LinearGradientBrush(backdrop.ClientRectangle,
                       dark ? Color.FromArgb(8, 32, 64) : Color.FromArgb(18, 120, 196), dark ? Color.FromArgb(20, 86, 92) : Color.FromArgb(132, 206, 120), 35f))
                g.FillRectangle(b, backdrop.ClientRectangle);
            // Soft bokeh, so the blur behind the glass has something to show.
            var rng = new Random(7);
            for (var i = 0; i < 70; i++)
            {
                var d = rng.Next(40, 260);
                var x = rng.Next(-60, backdrop.Width);
                var y = rng.Next(-60, backdrop.Height);
                using var bubble = new SolidBrush(Color.FromArgb(rng.Next(25, 75), 255, 255, rng.Next(2) == 0 ? 255 : 200));
                g.FillEllipse(bubble, x, y, d, d);
            }
        };
        backdrop.Show();
        Pump(400);
        using var reader = new SensorReader();
        reader.Open(new Config.HardwareConfig());
        var host = new DiagHost(reader);
        var main = new UI.MainWindow(host) { ShowInTaskbar = false, TopMost = true };
        main.ShowCentered(new Point(area.Left + 5, area.Top + 5));
        main.Location = new Point(area.Left + 60, area.Top + 60);
        var popup = new UI.TrayPopup(host, _ => { }, () => { });
        popup.ShowAboveTray(new Point(area.Left + 5, area.Top + 5));
        popup.Location = new Point(main.Right + 40, area.Top + 60);
        Pump(1500);
        var r = Rectangle.Union(main.Bounds, popup.Bounds);
        r.Inflate(30, 30);
        r.Intersect(area);
        using (var bmp = new Bitmap(r.Width, r.Height))
        {
            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
            bmp.Save(output, System.Drawing.Imaging.ImageFormat.Png);
        }
        popup.Close();
        main.Close();
        backdrop.Close();

        static void Pump(int ms)
        {
            var end = Environment.TickCount64 + ms;
            while (Environment.TickCount64 < end)
            {
                Application.DoEvents();
                Thread.Sleep(15);
            }
        }
    }

    public static void Run()
    {
        var sb = new StringBuilder();
        var last = 0L;
        void Stage(string name)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            using var p = Process.GetCurrentProcess();
            var priv = p.PrivateMemorySize64;
            var gc = GC.GetGCMemoryInfo();
            sb.AppendLine($"{name,-34} Private {priv / 1048576.0,6:N1} MB (+{(priv - last) / 1048576.0,5:N1})  " +
                          $"WS {p.WorkingSet64 / 1048576.0,6:N1}  GC heap {GC.GetTotalMemory(false) / 1048576.0,5:N1}  " +
                          $"GC committed {gc.TotalCommittedBytes / 1048576.0,5:N1}  Modules {p.Modules.Count}  Threads {p.Threads.Count}");
            last = priv;
        }

        sb.AppendLine($"SysWatch diag {DateTime.Now:yyyy-MM-dd HH:mm:ss}, elevated={new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator)}");
        Stage("baseline");

        using (var icon = new NotifyIcon { Icon = SystemIcons.Information, Visible = true, Text = "SysWatch diag" })
        {
            icon.ContextMenuStrip = new ContextMenuStrip();
            icon.ContextMenuStrip.Items.Add("x");
            Stage("tray icon + menu");
            icon.Visible = false;
        }

        using (var gpu = new NativeGpu())
        {
            gpu.Read();
            Thread.Sleep(500);
            gpu.Read();
            Stage("native GPU");
        }

        void Lhm(string name, Action<Computer> configure)
        {
            var c = new Computer();
            configure(c);
            c.Open();
            for (var i = 0; i < 5; i++)
            {
                foreach (var h in c.Hardware)
                {
                    h.Update();
                    foreach (var s in h.SubHardware) s.Update();
                }
                Thread.Sleep(300);
            }
            Stage($"LHM {name} open");
            c.Close();
            Stage($"LHM {name} closed");
        }

        Lhm("CPU", c => c.IsCpuEnabled = true);
        Lhm("Storage", c => c.IsStorageEnabled = true);
        Lhm("CPU+Storage", c => { c.IsCpuEnabled = true; c.IsStorageEnabled = true; });

        using (var reader = new SensorReader())
        {
            reader.Open(new Config.HardwareConfig());
            for (var i = 0; i < 10; i++)
            {
                reader.Read();
                Thread.Sleep(300);
            }
            Stage("SensorReader (app defaults)");

            var host = new DiagHost(reader);
            void Compact()
            {
                System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
            }
            void Window(string name, Func<Form> create)
            {
                var watch = Stopwatch.StartNew();
                var form = create();
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-5000, -5000);
                form.Show();
                Application.DoEvents();
                sb.AppendLine($"{name} shown in {watch.ElapsedMilliseconds} ms");
                for (var i = 0; i < 5; i++) { Application.DoEvents(); Thread.Sleep(200); }
                Stage($"{name} open");
                form.Close();
                form.Dispose();
                Application.DoEvents();
                Compact();
                Stage($"{name} closed");
            }
            Window("main window", () => new UI.MainWindow(host));
            Window("main window again", () => new UI.MainWindow(host));
        }

        using (var p = Process.GetCurrentProcess())
        {
            sb.AppendLine();
            sb.AppendLine("Largest modules:");
            foreach (var m in p.Modules.Cast<ProcessModule>().OrderByDescending(m => m.ModuleMemorySize).Take(25))
                sb.AppendLine($"  {m.ModuleName,-45} {m.ModuleMemorySize / 1048576.0,5:N1} MB  {m.FileName}");
        }

        Directory.CreateDirectory(Log.DataDir);
        File.WriteAllText(Path.Combine(Log.DataDir, "diag.txt"), sb.ToString(), Encoding.UTF8);
    }
}

internal sealed class DiagHost : UI.IUiHost
{
    public DiagHost(SensorReader reader)
    {
        _reader = reader;
        for (var i = 0; i < 100; i++) History.Add(reader.Read());
        LastSnapshot = reader.Read();
    }

    public Config.AppConfig Config { get; } = new();
    public Snapshot? LastSnapshot { get; }
    public IReadOnlyList<Config.Metric> ActiveAlerts { get; } = [];
    public Notify.NotifierHub Hub { get; } = new(
    [
        new Notify.LocalNotifier((_, _) => { }), new Notify.TelegramNotifier(), new Notify.DiscordNotifier(),
        new Notify.EmailNotifier(), new Notify.SignalNotifier(new Notify.SignalCliHost()),
    ]);
    public AlertLog Events { get; } = new(Path.Combine(Path.GetTempPath(), "syswatch-diag-events.jsonl"));
    public MetricHistory History { get; } = new();
    public bool PawnIoInstalled => false;
    private readonly SensorReader _reader;
    public List<SensorGroup> Sensors() => _reader.AllSensors();
    public void UpdateConfig(Action<Config.AppConfig> change) { }
    public void SaveAndApply(Config.AppConfig next) { }
    public void Mute(int minutes) { }
    public void OpenHistory() { }
    public void OpenPage(int page) { }
    public Task InstallUpdateAsync(Updates.UpdateInfo update) => Task.CompletedTask;
    public void OpenSettings() { }
}
