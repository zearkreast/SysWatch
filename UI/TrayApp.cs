using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using SysWatch.Commands;
using SysWatch.Config;
using SysWatch.Monitoring;
using SysWatch.Notify;
using SysWatch.Updates;

namespace SysWatch.UI;

public sealed class TrayApp : ApplicationContext, IUiHost
{
    private readonly SynchronizationContext _ui;
    private readonly NotifyIcon _icon;
    private readonly Icon _iconOk, _iconAlert, _iconMuted;
    private readonly SensorReader _sensors = new();
    private readonly AlertEngine _engine = new();
    private readonly SignalCliHost _signalHost = new();
    private readonly DiscordNotifier _discord = new();
    private readonly CommandRouter _router;
    private readonly object _configGate = new();
    private readonly List<Poller> _pollers = new();
    private readonly System.Threading.Timer _timer;
    private readonly System.Threading.Timer _updateTimer;
    private int _updating;
    private readonly FileSystemWatcher? _watcher;
    private volatile AppConfig _config;
    private string _channelKey = "";
    private string _hardwareKey = "";
    private DateTime _lastOwnSave = DateTime.MinValue;
    private MainWindow? _main;
    private TrayPopup? _popup;
    private DateTime _popupClosedAt = DateTime.MinValue;
    private readonly Dictionary<Metric, ReadingIcon> _readings = new();

    // One extra tray icon per chosen reading, showing just the number (like a taskbar meter).
    private sealed class ReadingIcon(NotifyIcon icon)
    {
        public NotifyIcon Icon { get; } = icon;
        public Icon? Image;
        public string Key = "";
    }
    private bool _disposed;

    public AppConfig Config => _config;
    public Snapshot? LastSnapshot { get; private set; }
    public IReadOnlyList<Metric> ActiveAlerts => _engine.ActiveMetrics;
    public NotifierHub Hub { get; }
    public MetricHistory History { get; } = new();
    public AlertLog Events { get; } = new(Path.Combine(Log.DataDir, "history.jsonl"));
    public bool PawnIoInstalled => _sensors.PawnIoInstalled;
    public List<SensorGroup> Sensors() => _sensors.AllSensors();

    public TrayApp()
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _config = AppConfig.Load();
        L.Apply(_config.Language);
        Theme.SetMode(_config.UiTheme);
        Motion.UserReduced = _config.ReduceMotion;

        _iconOk = MakeIcon(null);
        _iconAlert = MakeIcon(Color.FromArgb(218, 54, 51));
        _iconMuted = MakeIcon(Color.FromArgb(128, 128, 128));
        _icon = new NotifyIcon { Icon = _iconOk, Text = L.T("SysWatch: başlatılıyor…", "SysWatch: starting…"), Visible = true };
        _icon.MouseUp += OnTrayMouseUp;
        Theme.Changed += OnThemeChanged;

        Hub = new NotifierHub(
        [
            new LocalNotifier(ShowBalloon),
            new TelegramNotifier(),
            _discord,
            new EmailNotifier(),
            new SignalNotifier(_signalHost),
        ]);
        _router = new CommandRouter(this);

        _sensors.Watch(_config.ChartSensorIds());
        _timer = new System.Threading.Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
        Task.Run(() =>
        {
            _hardwareKey = HardwareKey(_config);
            _sensors.Open(_config.Hardware);
            if (!_sensors.PawnIoInstalled)
            {
                Log.Warn(L.T("PawnIO kurulu değil: CPU sıcaklığı gibi bazı sensörler okunamayabilir.", "PawnIO isn't installed: some sensors such as CPU temperature may not be readable."));
                ShowBalloon(L.T("SysWatch: PawnIO sürücüsü kurulu değil", "SysWatch: the PawnIO driver isn't installed"),
                    L.T("CPU sıcaklığı okunamayabilir. Ayarlar > Donanım'daki bağlantıdan kurabilirsiniz.",
                        "CPU temperature may not be readable. You can install it from the link in Settings > Hardware."));
            }
            _timer.Change(0, Timeout.Infinite);
        });

        ApplyChannels(_config);
        SyncReadingIcons(_config);

        try
        {
            _watcher = new FileSystemWatcher(Log.DataDir, "config.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += (_, _) => OnConfigFileChanged();
            _watcher.Renamed += (_, _) => OnConfigFileChanged();
        }
        catch (Exception ex)
        {
            Log.Warn(L.T($"config.json izlenemiyor: {ex.Message}", $"Can't watch config.json: {ex.Message}"));
        }

        Log.Info(L.T("SysWatch başladı.", "SysWatch started."));
        AnnounceUpdate();
        Updater.CleanUp();
        _updateTimer = new System.Threading.Timer(_ => _ = AutoUpdateAsync(), null, UpdateFirstCheck, UpdateInterval);
    }

    private static readonly TimeSpan UpdateFirstCheck = TimeSpan.FromMinutes(2), UpdateInterval = TimeSpan.FromHours(12);

    private void AnnounceUpdate()
    {
        var current = Updater.Current.ToString(3);
        if (_config.LastVersion == current) return;
        if (Version.TryParse(_config.LastVersion, out var previous) && previous < Updater.Current)
        {
            var text = L.T($"SysWatch {current} sürümüne güncellendi.", $"SysWatch was updated to {current}.");
            Log.Info(text);
            Events.Add(EventKind.Info, text);
            ShowBalloon("SysWatch", text);
        }
        UpdateConfig(c => c.LastVersion = current);
    }

    private async Task AutoUpdateAsync()
    {
        if (_disposed || !_config.AutoUpdate || Updater.Kind == InstallKind.None) return;
        try
        {
            var update = await Updater.CheckAsync(CancellationToken.None).ConfigureAwait(false);
            if (update != null) await InstallUpdateAsync(update).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warn(L.T($"Güncelleme denetlenemedi: {ex.Message}", $"Couldn't check for updates: {ex.Message}"));
        }
    }

    public async Task InstallUpdateAsync(UpdateInfo update)
    {
        if (Interlocked.Exchange(ref _updating, 1) == 1) return;
        try
        {
            var version = update.Version.ToString(3);
            Log.Info(L.T($"SysWatch {version} indiriliyor.", $"Downloading SysWatch {version}."));
            var file = await Updater.DownloadAsync(update, CancellationToken.None).ConfigureAwait(false);
            ShowBalloon(L.T("SysWatch güncelleniyor", "Updating SysWatch"),
                L.T($"{version} sürümü kuruluyor; SysWatch birkaç saniye içinde yeniden açılacak.", $"Installing {version}; SysWatch will reopen in a few seconds."));
            Log.Info(L.T($"SysWatch {version} kuruluyor.", $"Installing SysWatch {version}."));
            Updater.Apply(file);
            _ui.Post(_ => ExitThread(), null);
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _updating, 0);
            Log.Error(L.T("Güncelleme kurulamadı", "Couldn't install the update"), ex);
            throw;
        }
    }

    private void Tick()
    {
        if (_disposed) return;
        var cfg = _config;
        try
        {
            var snap = _sensors.Read();
            LastSnapshot = snap;
            History.Add(snap);
            var notifications = _engine.Evaluate(snap, cfg);
            foreach (var n in notifications)
            {
                Log.Info($"{n.Title} · {n.Body}");
                Events.Add(n.Kind == NotificationKind.Recovered ? EventKind.Recovered : EventKind.Alert, $"{n.Title} · {n.Body}");
                _ = Hub.BroadcastAsync(n, cfg);
            }
            _ui.Post(_ => UpdateTray(snap), null);
        }
        catch (Exception ex)
        {
            Log.Error(L.T("Ölçüm döngüsü hatası", "Measurement loop error"), ex);
        }
        finally
        {
            if (!_disposed) _timer.Change(cfg.IntervalSeconds * 1000, Timeout.Infinite);
        }
    }

    private void UpdateTray(Snapshot snap)
    {
        if (_disposed) return;
        string Part(string label, Metric m) => snap.Get(m) is { } v ? $"{label} {MetricInfo.Format(m, v)}" : "";
        var text = string.Join(" · ", new[]
        {
            Part("CPU", Metric.CpuTemp), Part("GPU", Metric.GpuTemp), Part(L.T("Yük", "Load"), Metric.CpuLoad), Part("RAM", Metric.Ram),
        }.Where(s => s.Length > 0));
        if (Hub.Muted) text = L.T("Susturuldu · ", "Muted · ") + text;
        _icon.Text = Truncate("SysWatch: " + (text.Length > 0 ? text : L.T("veri yok", "no data")), 127);
        var state = Hub.Muted ? 2 : _engine.ActiveMetrics.Count > 0 ? 1 : 0;
        _icon.Icon = state switch { 2 => _iconMuted, 1 => _iconAlert, _ => _iconOk };

        var lightTaskbar = TaskbarIsLight();
        var baseColor = _config.TrayNumberColor.Length > 0
            ? ColorTranslator.FromHtml(_config.TrayNumberColor)
            : lightTaskbar ? Color.FromArgb(52, 56, 62) : Color.FromArgb(222, 226, 232);
        var alarmColor = lightTaskbar ? Color.FromArgb(200, 50, 40) : Color.FromArgb(255, 120, 108);
        foreach (var (metric, reading) in _readings)
        {
            var v = snap.Get(metric);
            var number = v == null ? "--" : Math.Round(v.Value).ToString("0");
            var alarm = _engine.ActiveMetrics.Contains(metric);
            reading.Icon.Text = Truncate($"SysWatch · {MetricInfo.Label(metric)}: {(v == null ? L.T("okunamıyor", "unreadable") : MetricInfo.Format(metric, v.Value))}", 127);
            var color = alarm ? alarmColor : baseColor;
            var key = $"{number}|{color.ToArgb()}|{SystemInformation.SmallIconSize.Width}";
            if (key == reading.Key) continue;
            reading.Key = key;
            var old = reading.Image;
            reading.Image = RenderReadingIcon(number, color);
            reading.Icon.Icon = reading.Image;
            old?.Dispose();
        }
    }

    private void SyncReadingIcons(AppConfig cfg)
    {
        foreach (var metric in _readings.Keys.Where(m => !cfg.TrayReadings.Contains(m)).ToList())
        {
            var r = _readings[metric];
            r.Icon.Visible = false;
            r.Icon.Dispose();
            r.Image?.Dispose();
            _readings.Remove(metric);
        }
        foreach (var metric in cfg.TrayReadings.Distinct())
        {
            if (_readings.ContainsKey(metric)) continue;
            var icon = new NotifyIcon { Text = $"SysWatch · {MetricInfo.Label(metric)}", Icon = _iconOk, Visible = true };
            icon.MouseUp += OnTrayMouseUp;
            _readings[metric] = new ReadingIcon(icon);
        }
        if (LastSnapshot != null) UpdateTray(LastSnapshot);
    }

    private static bool TaskbarIsLight()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int v && v == 1;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // One fixed size per digit count, not per value, so neighbouring readings line up.
    private static Icon RenderReadingIcon(string text, Color color)
    {
        var size = SystemInformation.SmallIconSize.Width;
        using var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            var probe = text.Length >= 3 ? "188" : "88";
            var px = (float)Math.Floor(size * 0.86f);
            Font font;
            while (true)
            {
                font = new Font("Segoe UI Semibold", px, FontStyle.Regular, GraphicsUnit.Pixel);
                var w = g.MeasureString(probe, font, PointF.Empty, StringFormat.GenericTypographic).Width;
                if (w <= size + 0.5f || px < 6) break;
                font.Dispose();
                px -= 1f;
            }
            using (font)
            using (var brush = new SolidBrush(color))
            using (var fmt = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip })
            {
                // Drawn at a point (no layout box to clip against); Segoe UI's line box sits low, so nudge up.
                // Whole-pixel size and position with pixel-fitted glyphs: the sharpest result at 16-24 px.
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
                var m = g.MeasureString(text, font, PointF.Empty, fmt);
                g.DrawString(text, font, brush, (float)Math.Round((size - m.Width) / 2f), (float)Math.Round((size - m.Height) / 2f - size * 0.04f), fmt);
            }
        }
        var handle = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(handle).Clone();
        DestroyIcon(handle);
        return icon;
    }

    private void OnTrayMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) OpenMain(MainWindow.PageStatus);
        else if (e.Button == MouseButtons.Right) TogglePopup();
    }

    private void TogglePopup()
    {
        if (_popup is { IsDisposed: false })
        {
            _popup.Close();
            return;
        }
        if ((DateTime.Now - _popupClosedAt).TotalMilliseconds < 300) return;
        // Time from the click reaching the queue to the handler, and from there to the queue going idle (popup painted).
        var waited = unchecked(Environment.TickCount - GetMessageTime());
        var watch = System.Diagnostics.Stopwatch.StartNew();
        void Painted(object? s, EventArgs e)
        {
            Application.Idle -= Painted;
            var total = waited + watch.ElapsedMilliseconds;
            if (total >= SlowPopupMs)
                Log.Warn(L.T($"Sağ tık menüsü {total} ms'de açıldı (tıklama sırada {waited} ms bekledi, menü {watch.ElapsedMilliseconds} ms'de çizildi)",
                    $"Tray popup took {total} ms to open (click queued {waited} ms, popup drawn in {watch.ElapsedMilliseconds} ms)"));
        }
        Application.Idle += Painted;
        _popup = new TrayPopup(this, OpenMain, ExitThread);
        _popup.FormClosed += (_, _) =>
        {
            _popupClosedAt = DateTime.Now;
            _popup?.Dispose();
            _popup = null;
            ReleaseUiMemory();
        };
        _popup.ShowAboveTray(Cursor.Position);
    }

    public void Mute(int minutes)
    {
        Hub.MutedUntil = minutes <= 0 ? DateTime.MinValue : DateTime.Now.AddMinutes(minutes);
        Events.Add(EventKind.Info, minutes <= 0 ? L.T("Bildirimler açıldı", "Notifications are on") : L.T($"Bildirimler {Hub.MutedUntil:HH:mm}'e kadar susturuldu", $"Notifications muted until {Hub.MutedUntil:HH:mm}"));
        RefreshTray();
    }

    // Left click, the popup and commands open the one main window, centred on the screen of the click.
    private void OpenMain(int page)
    {
        if (_main is { IsDisposed: false })
        {
            _main.ShowSection(page);
            if (_main.WindowState == FormWindowState.Minimized) _main.WindowState = FormWindowState.Normal;
            _main.Activate();
            return;
        }
        _main = new MainWindow(this);
        _main.FormClosed += (_, _) =>
        {
            _main?.Dispose();
            _main = null;
            ReleaseUiMemory();
        };
        _main.ShowSection(page);
        _main.ShowCentered(Cursor.Position);
    }

    public void OpenHistory() => OpenMain(MainWindow.PageHistory);

    public void OpenSettings() => OpenMain(MainWindow.PageSettings);

    public void OpenPage(int page) => OpenMain(page);

    // Windows are gone once closed; compact once so the process drops back to its idle footprint.
    private void ReleaseUiMemory()
    {
        if (_main != null || _popup != null) return;
        _ui.Post(_ =>
        {
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }, null);
    }

    private void OnThemeChanged()
    {
        _ui.Post(_ =>
        {
            if (_disposed) return;
            foreach (var r in _readings.Values) r.Key = "";
            RefreshTray();
        }, null);
    }

    public void UpdateConfig(Action<AppConfig> change)
    {
        lock (_configGate)
        {
            var next = _config.Clone();
            change(next);
            SaveAndApply(next);
        }
    }

    public void SaveAndApply(AppConfig next)
    {
        lock (_configGate)
        {
            _lastOwnSave = DateTime.Now;
            next.Save();
            Apply(next);
        }
    }

    private void Apply(AppConfig next)
    {
        _config = next;
        _sensors.Watch(next.ChartSensorIds());
        _ui.Post(_ =>
        {
            L.Apply(next.Language);
            Theme.SetMode(next.UiTheme);
            Motion.UserReduced = next.ReduceMotion;
            SyncReadingIcons(next);
        }, null);
        var hw = HardwareKey(next);
        if (hw != _hardwareKey)
        {
            _hardwareKey = hw;
            Task.Run(() => _sensors.Open(next.Hardware));
        }
        ApplyChannels(next);
    }

    private void OnConfigFileChanged()
    {
        if ((DateTime.Now - _lastOwnSave).TotalSeconds < 3) return;
        Task.Run(async () =>
        {
            await Task.Delay(500).ConfigureAwait(false);
            lock (_configGate)
            {
                if ((DateTime.Now - _lastOwnSave).TotalSeconds < 3) return;
                Log.Info(L.T("config.json dışarıdan değişti, yeniden yükleniyor.", "config.json changed outside the app, reloading."));
                Apply(AppConfig.Load());
            }
        });
    }

    // Pollers and the Signal daemon only restart when settings that affect them actually change.
    private void ApplyChannels(AppConfig cfg)
    {
        var key = ChannelKey(cfg);
        if (key == _channelKey) return;
        _channelKey = key;

        foreach (var p in _pollers) p.Dispose();
        _pollers.Clear();
        _signalHost.Stop();

        if (cfg.Telegram.Mode == TelegramMode.NotifyAndCommands && cfg.Telegram.BotToken.Length > 0 && cfg.Telegram.ChatId != 0)
            _pollers.Add(new TelegramPoller(this, _router));
        if (cfg.Discord.Mode == DiscordMode.Bot && cfg.Discord.BotToken.Length > 0)
            _pollers.Add(new DiscordGateway(this, _router));
        if (cfg.Signal.Mode == SignalMode.NotifyAndCommands && cfg.Signal.Account.Length > 0)
        {
            try
            {
                _signalHost.Start(cfg.Signal);
                _pollers.Add(new SignalListener(this, _router));
            }
            catch (Exception ex)
            {
                Log.Error(L.T("signal-cli daemon başlatılamadı", "Couldn't start the signal-cli daemon"), ex);
                ShowBalloon(L.T("SysWatch: Signal başlatılamadı", "SysWatch: couldn't start Signal"), ex.Message);
            }
        }
        foreach (var p in _pollers) p.Start();
    }

    private static string ChannelKey(AppConfig c) => string.Join("|",
        c.Telegram.Mode, c.Telegram.BotToken.GetHashCode(), c.Telegram.ChatId,
        c.Discord.Mode, c.Discord.BotToken.GetHashCode(), c.Discord.ChannelId, c.Discord.AllowedUserId,
        c.Signal.Mode, c.Signal.CliPath, c.Signal.Account, c.Signal.Recipient, c.Signal.NoteToSelf, c.Signal.DaemonPort);

    private static string HardwareKey(AppConfig c) => System.Text.Json.JsonSerializer.Serialize(c.Hardware);

    private void RefreshTray()
    {
        if (LastSnapshot != null) UpdateTray(LastSnapshot);
    }

    private static void OpenFile(string path)
    {
        if (!File.Exists(path)) File.WriteAllText(path, "");
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void ShowBalloon(string title, string body)
    {
        _ui.Post(_ =>
        {
            if (_disposed) return;
            _icon.ShowBalloonTip(5000, Truncate(title, 63), body.Length > 0 ? Truncate(body, 255) : " ", ToolTipIcon.Warning);
        }, null);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    // The plain logo when all is well; alert and muted add a small dot in the bottom-right corner.
    private static Icon MakeIcon(Color? color)
    {
        var size = SystemInformation.SmallIconSize.Width;
        using var bmp = Logo.Render(size);
        if (color is { } c)
        {
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var d = Math.Max(6f, size * 0.36f);
            var r = new RectangleF(size - d - 0.5f, size - d - 0.5f, d, d);
            using var ring = new SolidBrush(Color.White);
            g.FillEllipse(ring, RectangleF.Inflate(r, 1, 1));
            using var fill = new SolidBrush(c);
            g.FillEllipse(fill, r);
        }
        var handle = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(handle).Clone();
        DestroyIcon(handle);
        return icon;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern int GetMessageTime();

    private const int SlowPopupMs = 250;

    protected override void ExitThreadCore()
    {
        Shutdown();
        base.ExitThreadCore();
    }

    private void Shutdown()
    {
        if (_disposed) return;
        _disposed = true;
        Log.Info(L.T("SysWatch kapanıyor.", "SysWatch is shutting down."));
        _timer.Dispose();
        _updateTimer.Dispose();
        _watcher?.Dispose();
        foreach (var p in _pollers) p.Dispose();
        _signalHost.Dispose();
        _sensors.Dispose();
        Theme.Changed -= OnThemeChanged;
        _main?.Close();
        _popup?.Close();
        foreach (var r in _readings.Values)
        {
            r.Icon.Visible = false;
            r.Icon.Dispose();
            r.Image?.Dispose();
        }
        _readings.Clear();
        _icon.Visible = false;
        _icon.Dispose();
        _iconOk.Dispose();
        _iconAlert.Dispose();
        _iconMuted.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Shutdown();
        base.Dispose(disposing);
    }
}
