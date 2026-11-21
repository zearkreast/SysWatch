using System.Diagnostics;
using SysWatch.Config;
using SysWatch.Monitoring;
using SysWatch.Notify;

namespace SysWatch.UI;

public sealed class MainWindow : AeroForm
{
    private readonly IUiHost _app;
    private AppConfig _cfg;

    private readonly NumericUpDown _interval = Num(1, 60);
    private readonly CheckBox _hostname = new() { Text = L.T("Bildirimlere bilgisayar adını ekle", "Include the computer name in notifications"), AutoSize = true };
    private readonly CheckBox _local = new() { Text = L.T("Windows bildirimi göster", "Show Windows notifications"), AutoSize = true };
    private readonly Dictionary<string, CheckBox> _hw = new();
    private readonly DataGridView _grid = new();

    private readonly ComboBox _tgMode = Combo(L.T("Kapalı", "Off"), L.T("Sadece bildirim", "Notifications only"), L.T("Bildirim + komut", "Notifications + commands"));
    private readonly TextBox _tgToken = Secret();
    private readonly TextBox _tgChat = new();
    private readonly CheckBox _tgMinimal = MinimalBox();

    private readonly ComboBox _dcMode = Combo(L.T("Kapalı", "Off"), L.T("Webhook (sadece bildirim)", "Webhook (notifications only)"), L.T("Bot (bildirim + komut)", "Bot (notifications + commands)"));
    private readonly TextBox _dcWebhook = Secret();
    private readonly TextBox _dcToken = Secret();
    private readonly TextBox _dcChannel = new();
    private readonly TextBox _dcUser = new();
    private readonly CheckBox _dcMinimal = MinimalBox();

    private readonly CheckBox _mailOn = new() { Text = L.T("E-posta bildirimi açık", "E-mail notifications on"), AutoSize = true };
    private readonly TextBox _mailHost = new();
    private readonly NumericUpDown _mailPort = Num(1, 65535);
    private readonly TextBox _mailUser = new();
    private readonly TextBox _mailPass = Secret();
    private readonly TextBox _mailFrom = new();
    private readonly TextBox _mailTo = new();
    private readonly TextBox _mailPgp = new();
    private readonly CheckBox _mailMinimal = MinimalBox();

    private readonly ComboBox _sgMode = Combo(L.T("Kapalı", "Off"), L.T("Sadece bildirim", "Notifications only"), L.T("Bildirim + komut (~150-250 MB RAM)", "Notifications + commands (~150-250 MB RAM)"));
    private readonly TextBox _sgCli = new();
    private readonly TextBox _sgAccount = new();
    private readonly TextBox _sgRecipient = new();
    private readonly CheckBox _sgSelf = new() { Text = L.T("Kendime Not (kendi hesabıma bağlı cihaz)", "Note to Self (a device linked to my own account)"), AutoSize = true };
    private readonly NumericUpDown _sgPort = Num(1024, 65535);
    private readonly CheckBox _sgMinimal = MinimalBox();

    private readonly CheckBox _pinOn = new() { Text = L.T("/kill ve /shutdown için PIN iste", "Require a PIN for /kill and /shutdown"), AutoSize = true };
    private readonly TextBox _pin = Secret();
    private readonly CheckBox _autoUpdate = new() { Text = L.T("Güncellemeleri otomatik denetle ve kur", "Check for and install updates automatically"), AutoSize = true };
    private readonly Label _updateStatus = new() { AutoSize = true, Tag = "muted", MaximumSize = new Size(460, 0) };
    private readonly CheckBox _startup = new() { Text = L.T("Oturum açılınca başlat (Görev Zamanlayıcı)", "Start when I sign in (Task Scheduler)"), AutoSize = true };
    private readonly CheckBox _reduceMotion = new() { Text = L.T("Hareketleri azalt (animasyonları kapatır, daha hafif çalışır)", "Reduce motion (turns animations off, uses fewer resources)"), AutoSize = true };
    private readonly ComboBox _language = Combo(L.T("Otomatik (Windows dili)", "Automatic (Windows language)"), "Türkçe", "English");
    private readonly ComboBox _uiTheme = Combo(L.T("Açık (Aero cam)", "Light (Aero glass)"), L.T("Koyu (füme cam)", "Dark (smoked glass)"), L.T("Windows'u takip et", "Follow Windows"));
    private readonly Dictionary<Metric, CheckBox> _trayReadings = new();
    private readonly ComboBox _trayColor = Combo(L.T("Otomatik (görev çubuğuna göre)", "Automatic (matches the taskbar)"), L.T("Beyaz", "White"), L.T("Açık gri", "Light gray"), L.T("Siyah", "Black"), L.T("Gök mavisi", "Sky blue"), L.T("Yeşil", "Green"), L.T("Turuncu", "Orange"), L.T("Özel renk…", "Custom color…"));
    private static readonly string[] TrayColorPresets = ["", "#FFFFFF", "#C8CDD3", "#000000", "#8CC8FF", "#8EE08A", "#FFB45A"];
    private string _customTrayColor = "#FFFFFF";
    private int _trayColorIndex;

    private readonly Panel _host = new() { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 16, 0) };
    private readonly Panel?[] _pages = new Panel?[8];
    private Func<Panel>[] _pageBuilders = null!;
    private readonly Dictionary<string, Panel> _channelPages = new();
    private Dictionary<string, Func<Panel>> _channelBuilders = null!;
    private readonly Dictionary<string, StatusRow> _channelRows = new();
    private readonly Label _dirtyLabel = new() { AutoSize = true, Tag = "muted", Text = "" };
    private NavList _nav = null!;
    private bool _loading;
    private bool _dirty;
    private Panel _footer = null!;
    private Panel _footerButtons = null!;
    private Panel? _details;
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 2000 };
    private string _status = "";
    private Color _statusColor = Theme.Success;

    // Navigation tree:
    //   İzleme   Durum (› Detaylar) · Geçmiş
    //   Ayarlar  Uyarı eşikleri · Bildirim kanalları (› kanal) · Görünüm · Donanım · Güvenlik · Uygulama
    public const int PageStatus = 0, PageHistory = 1, PageSettings = 2;
    private const int PageChannels = 3, PageHardware = 5;

    protected override string GlassTitle => "SysWatch";
    protected override string GlassStatus => _status;
    protected override Color GlassStatusColor => _statusColor;

    public MainWindow(IUiHost app)
    {
        _app = app;
        _cfg = app.Config.Clone();

        Text = "SysWatch";
        StartPosition = FormStartPosition.Manual;
        KeyPreview = true;
        ApplyGlassPadding();
        var unit = Font.Height;
        MinimumSize = new Size(unit * 40, unit * 30);
        Size = new Size(unit * 46, unit * 36);

        _nav = new NavList(Font) { Dock = DockStyle.Left, Width = NavCollapsed ? NavList.CollapsedWidth(Font) : unit * 12 };
        _nav.CollapseToggled += ToggleNav;
        _nav.AddHeader(L.T("İzleme", "Monitoring"));
        _nav.Add(Icons.Status, L.T("Durum", "Status"));
        _nav.Add(Icons.History, L.T("Geçmiş", "History"));
        _nav.AddHeader(L.T("Ayarlar", "Settings"));
        _nav.Add(Icons.Thresholds, L.T("Uyarı eşikleri", "Alert thresholds"));
        _nav.Add(Icons.Channels, L.T("Bildirim kanalları", "Notification channels"));
        _nav.Add(Icons.Appearance, L.T("Görünüm", "Appearance"));
        _nav.Add(Icons.Hardware, L.T("Donanım", "Hardware"));
        _nav.Add(Icons.Security, L.T("Güvenlik", "Security"));
        _nav.Add(Icons.App, L.T("Uygulama", "App"));
        _nav.SelectedChanged += ShowPage;

        _pageBuilders =
        [
            () => new DashboardView(app, Font, ShowDetails),
            () => new HistoryView(app, Font),
            ThresholdTab, ChannelsPage, AppearancePage, HardwarePage, SecurityPage, AppPage,
        ];
        _channelBuilders = new()
        {
            ["Windows"] = WindowsTab, ["Telegram"] = TelegramTab, ["Discord"] = DiscordTab, ["E-posta"] = EmailTab, ["Signal"] = SignalTab,
        };
        CreateInputs();

        var footer = _footer = new Panel { Dock = DockStyle.Bottom, Height = (int)(unit * 3), Padding = new Padding(unit, 0, unit, 0) };
        var save = new FlatButton(L.T("Kaydet", "Save"), primary: true);
        var cancel = new FlatButton(L.T("Geri al", "Revert"));
        save.Click += (_, _) => Save();
        cancel.Click += (_, _) => Revert();
        _footerButtons = new Panel { Dock = DockStyle.Right, Width = unit * 14 };
        _footerButtons.Controls.Add(cancel);
        _footerButtons.Controls.Add(save);
        _footerButtons.Layout += (_, _) =>
        {
            var top = (_footerButtons.Height - save.Height) / 2;
            save.Location = new Point(_footerButtons.Width - save.Width, top);
            cancel.Location = new Point(save.Left - cancel.Width - 6, top);
        };
        footer.Controls.Add(_dirtyLabel);
        footer.Controls.Add(_footerButtons);
        footer.Layout += (_, _) => _dirtyLabel.Location = new Point(_nav.Width + 16, (footer.Height - _dirtyLabel.Height) / 2);

        Controls.Add(_host);
        Controls.Add(_nav);
        Controls.Add(footer);
        _loading = true;
        LoadValues();
        _loading = false;
        ShowPage(PageStatus);
        _statusTimer.Tick += (_, _) => UpdateStatus();
        _statusTimer.Start();
        UpdateStatus();
    }

    // Remembered for the session: the window is recreated each time it opens.
    private static bool NavCollapsed;

    // With redraw off, both states are photographed and the real layout jumps to the end state; an opaque overlay
    // then plays the change from the pictures, so nothing is laid out or half-drawn on screen mid-animation.
    private void ToggleNav()
    {
        NavCollapsed = !NavCollapsed;
        var wide = Font.Height * 12;
        var narrow = NavList.CollapsedWidth(Font);
        var from = _nav.Width;
        var to = NavCollapsed ? narrow : wide;
        if (Motion.Reduced || !IsHandleCreated || !Visible || _host.Width <= 0 || _host.Height <= 0)
        {
            _nav.Width = to;
            return;
        }

        SendMessage(Handle, 0x000B, IntPtr.Zero, IntPtr.Zero); // WM_SETREDRAW off
        var area = Rectangle.Union(_nav.Bounds, _host.Bounds);
        var pageFrom = Snapshot();
        Bitmap Menu(int width)
        {
            _nav.Width = width;
            var bmp = new Bitmap(width, _nav.Height);
            _nav.DrawToBitmap(bmp, new Rectangle(0, 0, width, _nav.Height));
            return bmp;
        }
        var menuFrom = Menu(from);
        var menuTo = Menu(to);
        PerformLayout();
        _host.PerformLayout();
        var pageTo = Snapshot();

        var overlay = new CollapseOverlay(menuFrom, menuTo, pageFrom, pageTo, from) { Bounds = area };
        Controls.Add(overlay);
        overlay.BringToFront();
        SendMessage(Handle, 0x000B, new IntPtr(1), IntPtr.Zero);
        RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0004 | 0x0080 | 0x0100); // INVALIDATE | ERASE | ALLCHILDREN | UPDATENOW
        Motion.Run(overlay, 220, t =>
        {
            overlay.Progress = t;
            overlay.MenuWidth = (int)Math.Round(from + (to - from) * t);
            overlay.Invalidate();
            overlay.Update();
        }, () =>
        {
            Controls.Remove(overlay);
            overlay.Dispose();
        });
    }

    private sealed class CollapseOverlay : Control
    {
        private readonly Bitmap _menuFrom, _menuTo, _pageFrom, _pageTo;
        private readonly Color _menuBack, _pageBack;
        public int MenuWidth { get; set; }
        public float Progress { get; set; }

        public CollapseOverlay(Bitmap menuFrom, Bitmap menuTo, Bitmap pageFrom, Bitmap pageTo, int start)
        {
            (_menuFrom, _menuTo, _pageFrom, _pageTo, MenuWidth) = (menuFrom, menuTo, pageFrom, pageTo, start);
            _menuBack = menuFrom.GetPixel(2, menuFrom.Height - 2);
            _pageBack = pageTo.GetPixel(pageTo.Width - 2, pageTo.Height / 2);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Opaque, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var w = MenuWidth;
            using (var back = new SolidBrush(_menuBack)) g.FillRectangle(back, 0, 0, w, Height);
            using (var back = new SolidBrush(_pageBack)) g.FillRectangle(back, w, 0, Width - w, Height);

            g.SetClip(new Rectangle(0, 0, w, Height));
            Blend(g, _menuFrom, 0, 1 - Progress);
            Blend(g, _menuTo, 0, Progress);
            g.SetClip(new Rectangle(w, 0, Width - w, Height));
            // The page settles into its new layout early so the two layouts are never seen overlapping for long.
            var page = Math.Min(1f, Progress * 2.5f);
            Blend(g, _pageFrom, w, 1 - page);
            Blend(g, _pageTo, w, page);
            g.ResetClip();
        }

        private void Blend(Graphics g, Bitmap bmp, int x, float alpha)
        {
            if (alpha <= 0.01f) return;
            var dest = new Rectangle(x, 0, bmp.Width, Math.Min(bmp.Height, Height));
            if (alpha >= 0.99f)
            {
                g.DrawImage(bmp, dest, 0, 0, dest.Width, dest.Height, GraphicsUnit.Pixel);
                return;
            }
            using var attrs = new System.Drawing.Imaging.ImageAttributes();
            attrs.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = alpha });
            g.DrawImage(bmp, dest, 0, 0, dest.Width, dest.Height, GraphicsUnit.Pixel, attrs);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _menuFrom.Dispose();
                _menuTo.Dispose();
                _pageFrom.Dispose();
                _pageTo.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    public void ShowSection(int page)
    {
        if (_nav.Selected == page) ShowPage(page);
        else _nav.Selected = page;
    }

    public void ShowCentered(Point from)
    {
        _ = Handle;
        PerformLayout();
        var area = Screen.FromPoint(from).WorkingArea;
        var target = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
        const int rise = 18;
        Location = new Point(target.X, target.Y + rise);
        Show();
        Activate();
        Motion.Run(this, 220, t =>
        {
            if (!IsDisposed) Location = new Point(target.X, (int)(target.Y + rise * (1 - t)));
        });
    }

    private void UpdateStatus()
    {
        var active = _app.ActiveAlerts;
        var (status, color) = _app.Hub.Muted
            ? (L.T($"Susturuldu · {_app.Hub.MutedUntil:HH:mm}", $"Muted · {_app.Hub.MutedUntil:HH:mm}"), Theme.Neutral)
            : active.Count > 0
                ? (active.Count == 1 ? L.T("1 uyarı", "1 alert") : L.T($"{active.Count} uyarı", $"{active.Count} alerts"), Theme.DangerFill)
                : ("Normal", Theme.SuccessFill);
        if (status == _status && color == _statusColor) return;
        _status = status;
        _statusColor = color;
        Invalidate(new Rectangle(0, 0, Width, GlassTop));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) Close();
        base.OnKeyDown(e);
    }

    protected override void RestyleTheme()
    {
        base.RestyleTheme();
        _nav.BackColor = Theme.Window;
    }

    private void ShowPage(int index) => ShowPanel(_pages[index] ??= Adopt(_pageBuilders[index](), track: index >= PageSettings));

    // Untracked: its search box isn't a setting and mustn't count as an unsaved change.
    private void ShowDetails() =>
        ShowPanel(_details ??= Adopt(new DetailsView(_app, Font, () => ShowPage(PageStatus), () => ShowSection(PageHardware)), track: false));

    private Panel ChannelPage(string name) =>
        _channelPages.TryGetValue(name, out var page) ? page : _channelPages[name] = Adopt(_channelBuilders[name]());

    // Pages are built on first show. The inputs exist from the start (CreateInputs), so LoadValues and Collect don't
    // depend on which pages were opened.
    private Panel Adopt(Panel page, bool track = true)
    {
        page.Visible = false;
        page.BackColor = Color.Transparent;
        _host.Controls.Add(page);
        Theme.ApplyControls(page);
        if (track) TrackChanges(page);
        return page;
    }

    private static (string Key, string Label)[] HardwareKeys =>
    [
        ("Cpu", "CPU"), ("Gpu", "GPU"), ("GpuDetailed", L.T("GPU ayrıntılı (+40 MB)", "GPU detailed (+40 MB)")), ("Storage", L.T("Diskler", "Disks")),
        ("StorageDetailed", L.T("Disk ayrıntılı (+40 MB)", "Disk detailed (+40 MB)")), ("Motherboard", L.T("Anakart", "Motherboard")),
        ("Memory", L.T("RAM modülleri", "Memory modules")), ("Controller", L.T("Fan kontrolcüleri", "Fan controllers")), ("Network", L.T("Ağ", "Network")), ("Psu", "PSU"),
    ];

    private void CreateInputs()
    {
        foreach (var (key, label) in HardwareKeys) _hw[key] = new CheckBox { Text = label, AutoSize = true };
        foreach (var m in Enum.GetValues<Metric>())
            _trayReadings[m] = new CheckBox { Text = MetricInfo.Label(m), AutoSize = true, Margin = new Padding(0, 3, 14, 3) };
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = L.T("Açık", "On"), FillWeight = 40 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = L.T("Metrik", "Metric"), ReadOnly = true, FillWeight = 130 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = L.T("Eşik", "Threshold") });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = L.T("Süre (sn)", "Duration (s)") });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = L.T("Tekrar (dk)", "Repeat (min)") });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = L.T("Histerezis", "Hysteresis") });
    }

    // Cross-fades between pages. Both pages are snapshotted and the overlay is opaque, so nothing half-drawn
    // shows; redraw of the host is suspended while the pages swap.
    private void ShowPanel(Panel panel)
    {
        var current = _host.Controls.Cast<Control>().FirstOrDefault(c => c.Visible && c is not FadeOverlay);
        if (current == panel) return;
        var animate = current != null && IsHandleCreated && Visible && !Motion.Reduced && _host.Width > 0 && _host.Height > 0;
        var before = animate ? Snapshot() : null;
        if (_host.IsHandleCreated) SendMessage(_host.Handle, 0x000B, IntPtr.Zero, IntPtr.Zero); // WM_SETREDRAW off
        foreach (Control c in _host.Controls) if (c is not FadeOverlay) c.Visible = c == panel;
        _footerButtons.Visible = Array.IndexOf(_pages, panel) >= PageSettings || _channelPages.ContainsValue(panel);
        if (!_footerButtons.Visible) _dirtyLabel.Text = "";
        else if (_dirty) _dirtyLabel.Text = L.T("Kaydedilmemiş değişiklik var", "Unsaved changes");
        if (panel == _pages[PageChannels]) RefreshChannelRows();
        _host.PerformLayout();
        var after = animate ? Snapshot() : null;
        FadeOverlay? overlay = null;
        if (before != null && after != null)
        {
            overlay = new FadeOverlay(before, after) { Bounds = new Rectangle(0, 0, _host.Width, _host.Height) };
            _host.Controls.Add(overlay);
            overlay.BringToFront();
        }
        if (_host.IsHandleCreated)
        {
            SendMessage(_host.Handle, 0x000B, new IntPtr(1), IntPtr.Zero);
            RedrawWindow(_host.Handle, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0004 | 0x0080 | 0x0100); // INVALIDATE | ERASE | ALLCHILDREN | UPDATENOW
        }
        if (overlay == null) return;
        Motion.Run(overlay, 180, t => { overlay.Alpha = 1 - t; overlay.Invalidate(); }, () =>
        {
            _host.Controls.Remove(overlay);
            overlay.Dispose();
        });
    }

    private Bitmap Snapshot()
    {
        var bmp = new Bitmap(_host.Width, _host.Height);
        _host.DrawToBitmap(bmp, new Rectangle(0, 0, _host.Width, _host.Height));
        return bmp;
    }

    private sealed class FadeOverlay : Control
    {
        private readonly Bitmap _before, _after;
        public float Alpha { get; set; } = 1;

        public FadeOverlay(Bitmap before, Bitmap after)
        {
            _before = before;
            _after = after;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Opaque, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.DrawImageUnscaled(_after, 0, 0);
            using var attrs = new System.Drawing.Imaging.ImageAttributes();
            attrs.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = Alpha });
            e.Graphics.DrawImage(_before, new Rectangle(0, 0, _before.Width, _before.Height), 0, 0, _before.Width, _before.Height, GraphicsUnit.Pixel, attrs);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _before.Dispose();
                _after.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool RedrawWindow(IntPtr hwnd, IntPtr rect, IntPtr region, uint flags);

    private void OpenChannel(string name)
    {
        ShowPanel(ChannelPage(name));
    }

    private void TrackChanges(Control root)
    {
        foreach (Control c in root.Controls)
        {
            switch (c)
            {
                case TextBox t: t.TextChanged += MarkDirty; break;
                case CheckBox cb: cb.CheckedChanged += MarkDirty; break;
                case ComboBox co: co.SelectedIndexChanged += MarkDirty; break;
                case NumericUpDown n: n.ValueChanged += MarkDirty; break;
                case DataGridView g:
                    g.CellValueChanged += MarkDirty;
                    g.CurrentCellDirtyStateChanged += (_, _) => g.CommitEdit(DataGridViewDataErrorContexts.Commit);
                    break;
            }
            if (c.HasChildren) TrackChanges(c);
        }
    }

    private void MarkDirty(object? sender, EventArgs e)
    {
        if (_loading || _dirty) return;
        _dirty = true;
        _dirtyLabel.Text = L.T("Kaydedilmemiş değişiklik var", "Unsaved changes");
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_dirty && e.CloseReason == CloseReason.UserClosing &&
            MessageBox.Show(this, L.T("Değişiklikler kaydedilmedi. Kapatılsın mı?", "Your changes aren't saved. Close anyway?"), "SysWatch",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            e.Cancel = true;
        base.OnFormClosing(e);
    }

    private Panel ChannelsPage()
    {
        var list = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        list.Controls.Add(Heading(L.T("Bildirim kanalları", "Notification channels"), Icons.Channels));
        foreach (var name in new[] { "Windows", "Telegram", "Discord", "E-posta", "Signal" })
        {
            var row = new StatusRow(Font) { Glyph = "brand:" + name, Title = name == "Windows" ? L.T("Windows bildirimi", "Windows notifications") : L.Channel(name), Margin = new Padding(0, 0, 0, 6) };
            row.Click += (_, _) => OpenChannel(name);
            _channelRows[name] = row;
            list.Controls.Add(row);
        }
        list.Controls.Add(Note(L.T("Bir kanala tıklayarak ayarlarını açın. Discord, Telegram ve Signal logoları Simple Icons'tan alındı ve ilgili şirketlerin ticari markalarıdır.", "Click a channel to open its settings. The Discord, Telegram and Signal logos come from Simple Icons and are trademarks of their owners.")));
        var page = Page(L.T("Kanallar", "Channels"), list);
        page.Resize += (_, _) =>
        {
            foreach (var r in _channelRows.Values) r.Width = Math.Max(200, page.ClientSize.Width - 4);
        };
        return page;
    }

    private void RefreshChannelRows()
    {
        var c = Collect();
        var amber = Color.FromArgb(186, 117, 23);
        foreach (var (name, row) in _channelRows)
        {
            var notifier = _app.Hub.Notifiers.First(n => n.Name == name);
            var enabled = notifier.IsEnabled(c);
            var (configuredOn, mode, minimal) = name switch
            {
                "Windows" => (c.Local.Enabled, L.T("Açık", "On"), false),
                "Telegram" => (c.Telegram.Mode != TelegramMode.Off, c.Telegram.Mode == TelegramMode.NotifyAndCommands ? L.T("Bildirim + komut", "Notifications + commands") : L.T("Sadece bildirim", "Notifications only"), c.Telegram.Minimal),
                "Discord" => (c.Discord.Mode != DiscordMode.Off, c.Discord.Mode == DiscordMode.Bot ? L.T("Bot: bildirim + komut", "Bot: notifications + commands") : "Webhook", c.Discord.Minimal),
                "E-posta" => (c.Email.Enabled, c.Email.PgpPublicKeyPath.Length > 0 ? L.T("Açık · PGP", "On · PGP") : L.T("Açık", "On"), c.Email.Minimal),
                _ => (c.Signal.Mode != SignalMode.Off, c.Signal.Mode == SignalMode.NotifyAndCommands ? L.T("Bildirim + komut", "Notifications + commands") : L.T("Sadece bildirim", "Notifications only"), c.Signal.Minimal),
            };
            var last = _app.Hub.LastResult(name);
            if (!configuredOn)
                (row.Status, row.DotColor, row.StatusColor) = (L.T("Kapalı", "Off"), Theme.Neutral, Theme.TextMuted);
            else if (!enabled)
                (row.Status, row.DotColor, row.StatusColor) = (L.T("Eksik ayar", "Incomplete"), amber, Theme.Warning);
            else if (last is { Error: not null } failed)
                (row.Status, row.DotColor, row.StatusColor) = (L.T($"Son gönderim başarısız ({failed.Time:HH:mm})", $"Last delivery failed ({failed.Time:HH:mm})"), amber, Theme.Warning);
            else
                (row.Status, row.DotColor, row.StatusColor) = (mode + (minimal ? L.T(" · az bilgi", " · minimal") : ""), Theme.SuccessFill, Theme.Success);
            row.Invalidate();
        }
    }

    private Panel SecurityPage()
    {
        var t = Table();
        SpanRow(t, Heading(L.T("Güvenlik", "Security"), Icons.Security));
        Row(t, "PIN", _pin);
        Row(t, "", _pinOn);
        Row(t, "", Note(L.T("PIN açıkken /kill ve /shutdown komutlarının sonuna PIN yazılması gerekir. 5 hatalı denemede bu komutlar 15 dakika kilitlenir.\n\n" +
                        "Komutlar sadece izinli Telegram sohbetinden, Discord kullanıcısından veya Signal numarasından kabul edilir. " +
                        "Mesajlaşma hesaplarınızda iki adımlı doğrulamayı açık tutun.", "When the PIN is on, /kill and /shutdown must end with the PIN. After 5 wrong attempts these commands are locked for 15 minutes.\n\n" +
                        "Commands are accepted only from the allowed Telegram chat, Discord user or Signal number. " +
                        "Keep two-step verification on for your messaging accounts.")));
        return Page(L.T("Güvenlik", "Security"), t);
    }

    private Panel HardwarePage()
    {
        var t = Table();
        SpanRow(t, Heading(L.T("Donanım", "Hardware"), Icons.Hardware));
        var hwPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, FlowDirection = FlowDirection.TopDown };
        foreach (var (key, _) in HardwareKeys) hwPanel.Controls.Add(_hw[key]);
        Row(t, L.T("Okunan donanım", "Hardware to read"), hwPanel);
        Row(t, "", Note(L.T("Az donanım = daha hafif. RAM her zaman, GPU ve disk varsayılan olarak Windows'tan okunur.\n" +
                        "\"Ayrıntılı\" seçenekler üretici kütüphanelerini / SMART okumayı yükler: daha çok sensör, her biri ~40 MB daha fazla bellek.", "Less hardware = lighter. RAM is always read from Windows, and so are GPU and disk by default.\n" +
                        "\"Detailed\" options load the vendor libraries / SMART reading: more sensors, about 40 MB more memory each.")));
        var pawn = _app.PawnIoInstalled
            ? Note(L.T("PawnIO sürücüsü kurulu.", "The PawnIO driver is installed."))
            : Link(L.T("PawnIO kurulu değil: CPU sıcaklığı okunamaz. İndirmek için tıklayın (pawnio.eu).", "PawnIO isn't installed, so CPU temperature can't be read. Click to download it (pawnio.eu)."), "https://pawnio.eu/");
        Row(t, L.T("Sensör sürücüsü", "Sensor driver"), pawn);
        return Page(L.T("Donanım", "Hardware"), t);
    }

    private static Control Heading(string text, string? icon = null)
    {
        var label = new Label
        {
            Text = text, AutoSize = true, Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, SystemFonts.MessageBoxFont.Size * 1.35f),
            Margin = new Padding(0, 0, 0, 10),
        };
        if (icon == null) return label;
        var size = (int)(label.Font.Height * 1.2);
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };
        row.Controls.Add(new PictureBox { Image = Icons.Get(icon, size), Size = new Size(size, size), Margin = new Padding(0, 0, 8, 0) });
        label.Margin = new Padding(0, (size - label.Font.Height) / 2, 0, 0);
        row.Controls.Add(label);
        return row;
    }

    private Panel ChannelHeader(string channel)
    {
        var title = channel == "Windows" ? L.T("Windows bildirimi", "Windows notifications") : L.Channel(channel);
        var bar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0, 0, 0, 8) };
        var back = new LinkLabel { Text = L.T("‹ Kanallar", "‹ Channels"), AutoSize = true, Margin = new Padding(0, 6, 12, 0) };
        back.LinkClicked += (_, _) => ShowPage(PageChannels);
        bar.Controls.Add(back);
        bar.Controls.Add(Heading(title, "brand:" + channel));
        return bar;
    }

    private Panel AppearancePage()
    {
        var t = Table();
        SpanRow(t, Heading(L.T("Görünüm", "Appearance"), Icons.Appearance));
        Row(t, L.T("Dil", "Language"), _language);
        Row(t, L.T("Tema", "Theme"), _uiTheme);
        Row(t, "", _reduceMotion);
        var readings = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, MaximumSize = new Size(Font.Height * 26, 0) };
        foreach (var cb in _trayReadings.Values) readings.Controls.Add(cb);
        Row(t, L.T("Tepside sayılar", "Tray numbers"), readings);
        Row(t, L.T("Sayı rengi", "Number color"), _trayColor);
        _trayColor.SelectedIndexChanged += (_, _) =>
        {
            if (_loading) return;
            if (_trayColor.SelectedIndex == TrayColorPresets.Length)
            {
                using var dlg = new ColorDialog { FullOpen = true, Color = ColorTranslator.FromHtml(_customTrayColor) };
                if (dlg.ShowDialog(this) == DialogResult.OK) _customTrayColor = ColorTranslator.ToHtml(dlg.Color);
                else
                {
                    _loading = true;
                    _trayColor.SelectedIndex = _trayColorIndex;
                    _loading = false;
                    return;
                }
            }
            _trayColorIndex = _trayColor.SelectedIndex;
        };
        Row(t, "", Note(L.T("Seçilen her okuma tepside ayrı bir sayı olarak görünür. Windows'un gizli simgeler menüsünden görev çubuğuna sürükleyebilirsiniz. Eşiği aşan okuma kırmızıya döner.", "Each selected reading appears as its own number in the tray. You can drag them onto the taskbar from Windows' hidden icons menu. A reading above its threshold turns red.")));
        return Page(L.T("Görünüm", "Appearance"), t);
    }

    private Panel AppPage()
    {
        var t = Table();
        SpanRow(t, Heading(L.T("Uygulama", "App"), Icons.App));
        Row(t, L.T("Ölçüm aralığı (sn)", "Measurement interval (s)"), _interval);
        Row(t, "", _hostname);
        Row(t, "", _startup);
        var log = new FlatButton(L.T("Günlüğü aç", "Open log")) { Image = Icons.Get(Icons.Log, Icons.SizeFor(Font) - 4) };
        log.Click += (_, _) => OpenLog();
        Row(t, L.T("Günlük", "Log"), log);
        Row(t, "", Note(L.T("Uygulamanın olay günlüğü: ölçüm hataları, gönderim sonuçları, gelen komutlar. 1 MB'ı geçince eskisi syswatch.log.old olarak saklanır.", "The app's event log: measurement errors, delivery results, incoming commands. Past 1 MB the old one is kept as syswatch.log.old.")));
        Row(t, "", Link(L.T("Ayar, geçmiş ve günlük klasörünü aç", "Open the settings, history and log folder"), Log.DataDir));
        var updateRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        var check = new FlatButton(L.T("Şimdi denetle", "Check now"));
        check.Click += async (_, _) => await CheckForUpdate(check);
        updateRow.Controls.Add(_autoUpdate);
        updateRow.Controls.Add(check);
        Row(t, L.T("Güncelleme", "Updates"), updateRow);
        if (Updates.Updater.Kind == Updates.InstallKind.None)
        {
            _autoUpdate.Enabled = check.Enabled = false;
            _updateStatus.Text = L.T("Bu derleme kurulu ya da taşınabilir sürüm olmadığı için kendini güncellemez.",
                "This build isn't the installed or portable edition, so it doesn't update itself.");
        }
        Row(t, "", _updateStatus);
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "";
        Row(t, L.T("Sürüm", "Version"), Link(L.T($"SysWatch {version}{(Log.Portable ? " (taşınabilir)" : "")} · GPL-3.0 özgür yazılım", $"SysWatch {version}{(Log.Portable ? " (portable)" : "")} · GPL-3.0 free software"), "https://github.com/zearkreast/SysWatch"));
        var notices = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt");
        Row(t, "", Link(L.T("Lisanslar ve üçüncü taraf bileşenler", "Licenses and third-party components"), File.Exists(notices) ? notices : "https://github.com/zearkreast/SysWatch/blob/main/THIRD-PARTY-NOTICES.md"));
        return Page(L.T("Uygulama", "App"), t);
    }

    private async Task CheckForUpdate(Button button)
    {
        button.Enabled = false;
        _updateStatus.Text = L.T("Denetleniyor…", "Checking…");
        try
        {
            var update = await Updates.Updater.CheckAsync(CancellationToken.None);
            if (IsDisposed) return;
            if (update == null)
            {
                _updateStatus.Text = L.T($"SysWatch güncel ({Updates.Updater.Current.ToString(3)}).", $"SysWatch is up to date ({Updates.Updater.Current.ToString(3)}).");
                return;
            }
            var version = update.Version.ToString(3);
            if (MessageBox.Show(this, L.T($"SysWatch {version} çıktı. Şimdi kurulsun mu?\n\nSysWatch birkaç saniyeliğine kapanıp yeniden açılır.",
                        $"SysWatch {version} is available. Install it now?\n\nSysWatch closes and reopens within a few seconds."),
                    "SysWatch", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                _updateStatus.Text = L.T($"SysWatch {version} kurulmayı bekliyor.", $"SysWatch {version} is waiting to be installed.");
                return;
            }
            _updateStatus.Text = L.T($"SysWatch {version} indiriliyor…", $"Downloading SysWatch {version}…");
            await _app.InstallUpdateAsync(update);
        }
        catch (Exception ex)
        {
            if (!IsDisposed) _updateStatus.Text = L.T("Güncelleme başarısız: ", "Update failed: ") + ex.Message;
        }
        finally
        {
            if (!IsDisposed) button.Enabled = Updates.Updater.Kind != Updates.InstallKind.None;
        }
    }

    internal static void OpenLog()
    {
        if (!File.Exists(Log.FilePath)) File.WriteAllText(Log.FilePath, "");
        Process.Start(new ProcessStartInfo(Log.FilePath) { UseShellExecute = true });
    }

    private Panel ThresholdTab()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(_grid);
        panel.Controls.Add(new Label
        {
            Dock = DockStyle.Top, Height = Font.Height * 5, Tag = "muted",
            Text = L.T("Süre: değer bu kadar saniye eşiğin üstünde kalırsa uyarı verilir.\n" +
                   "Tekrar: uyarı devam ediyorsa kaç dakikada bir hatırlatılacağı.\n" +
                   "Histerezis: \"normale döndü\" için değerin eşiğin bu kadar altına inmesi gerekir.",
                   "Duration: alert when the value stays above the threshold for this many seconds.\n" +
                   "Repeat: how often to remind, in minutes, while the alert lasts.\n" +
                   "Hysteresis: how far below the threshold the value must drop to count as back to normal."),
        });
        var heading = Heading(L.T("Uyarı eşikleri", "Alert thresholds"), Icons.Thresholds);
        heading.Dock = DockStyle.Top;
        panel.Controls.Add(heading);
        return Page(L.T("Eşikler", "Thresholds"), panel);
    }

    private Panel WindowsTab()
    {
        var t = Table();
        SpanRow(t, ChannelHeader("Windows"));
        Row(t, "", _local);
        Row(t, "", TestButton("Windows"));
        Row(t, "", Note(L.T("Uyarılar Windows'un bildirim balonu olarak, tepsi simgesinin yanında gösterilir. " +
                        "Odak Yardımı / Rahatsız Etmeyin açıkken Windows bunları sessizce bildirim merkezine koyabilir.", "Alerts appear as Windows notification banners next to the tray icon. " +
                        "With Focus Assist / Do Not Disturb on, Windows may put them silently in the notification center.")));
        return Page(L.T("Windows bildirimi", "Windows notifications"), t);
    }

    private Panel TelegramTab()
    {
        var t = Table();
        SpanRow(t, ChannelHeader("Telegram"));
        Row(t, L.T("Mod", "Mode"), _tgMode);
        Row(t, "Bot token", _tgToken);
        var chatRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        _tgChat.Width = 200;
        var find = new FlatButton(L.T("Chat ID bul", "Find chat ID"));
        find.Click += async (_, _) => await FindTelegramChat();
        chatRow.Controls.Add(_tgChat);
        chatRow.Controls.Add(find);
        Row(t, "Chat ID", chatRow);
        Row(t, "", _tgMinimal);
        Row(t, "", TestButton("Telegram"));
        Row(t, "", Note(L.T("1) Telegram'da @BotFather'a /newbot yazıp token alın.\n2) Botunuza özel mesajla /start yazın, sonra \"Chat ID bul\".\n" +
                        "Komutlar sadece bu özel sohbetten kabul edilir. Telegram mesajları uçtan uca şifreli değildir.", "1) In Telegram, send /newbot to @BotFather and copy the token.\n2) Send /start to your bot in a private chat, then click \"Find chat ID\".\n" +
                        "Commands are accepted only from this private chat. Telegram messages are not end-to-end encrypted.")));
        return Page("Telegram", t);
    }

    private Panel DiscordTab()
    {
        var t = Table();
        SpanRow(t, ChannelHeader("Discord"));
        Row(t, L.T("Mod", "Mode"), _dcMode);
        Row(t, "Webhook URL", _dcWebhook);
        Row(t, "Bot token", _dcToken);
        Row(t, L.T("Kanal ID", "Channel ID"), _dcChannel);
        Row(t, L.T("İzinli kullanıcı ID", "Allowed user ID"), _dcUser);
        Row(t, "", _dcMinimal);
        var addToAccount = new FlatButton(L.T("Hesabıma ekle (önerilen)", "Add to my account (recommended)"));
        addToAccount.Click += async (_, _) => await OpenDiscordInvite(addToAccount, userInstall: true);
        var addToServer = new FlatButton(L.T("Sunucuma ekle", "Add to my server"));
        addToServer.Click += async (_, _) => await OpenDiscordInvite(addToServer, userInstall: false);
        var discordButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        discordButtons.Controls.Add(TestButton("Discord"));
        discordButtons.Controls.Add(addToAccount);
        discordButtons.Controls.Add(addToServer);
        Row(t, "", discordButtons);
        Row(t, "", Note(L.T("Webhook: Kanal ayarları > Entegrasyonlar > Webhook > URL'yi kopyala. Bot gerekmez, sadece bildirim.\n\n" +
                        "Bot kurulumu (sunucu gerekmez):\n" +
                        "1) discord.com/developers'da uygulama + bot oluşturun, token'ı buraya yapıştırın.\n" +
                        "2) Developer Portal > Installation'da \"User Install\" kutusunu işaretleyin.\n" +
                        "3) \"Hesabıma ekle\" ile botu Discord hesabınıza ekleyin. Bot size DM'den yazabilir, " +
                        "ortak sunucuda olmanız gerekmez. Yetkiyi kaldırırsanız bot size artık yazamaz.\n" +
                        "4) Discord > Kullanıcı Ayarları > Gelişmiş > Geliştirici Modu'nu açın. Kendi adınıza sağ tıklayıp " +
                        "\"Kullanıcı ID'sini Kopyala\" ile izinli kullanıcı ID'sini alın.\n" +
                        "5) SysWatch'ı kaydedin: botla DM'de / yazınca status, top, kill, shutdown, set, mute komutları listelenir.\n\n" +
                        "Bildirimler bir sunucu kanalına gitsin isterseniz \"Sunucuma ekle\" ile botu sunucuya ekleyip kanala sağ tık > " +
                        "\"Kanal ID'sini Kopyala\" ile Kanal ID girin. Boşsa bot DM'den yazar.\n" +
                        "Komutlar sadece izinli kullanıcı ID'sinden kabul edilir, cevapları sadece siz görürsünüz. " +
                        "Discord mesajları uçtan uca şifreli değildir.", "Webhook: Channel settings > Integrations > Webhooks > Copy URL. No bot needed, notifications only.\n\n" +
                        "Bot setup (no server needed):\n" +
                        "1) Create an application and a bot at discord.com/developers and paste the token here.\n" +
                        "2) In Developer Portal > Installation, tick \"User Install\".\n" +
                        "3) Use \"Add to my account\" to add the bot to your Discord account. It can then DM you " +
                        "without a shared server. If you remove its authorization, it can no longer message you.\n" +
                        "4) In Discord, turn on User Settings > Advanced > Developer Mode. Right-click your own name and " +
                        "choose \"Copy User ID\" to get the allowed user ID.\n" +
                        "5) Save SysWatch: typing / in the bot's DM lists the status, top, kill, shutdown, set and mute commands.\n\n" +
                        "To send notifications to a server channel, add the bot with \"Add to my server\", then right-click the channel > " +
                        "\"Copy Channel ID\" and enter it. If it is empty, the bot sends DMs.\n" +
                        "Commands are accepted only from the allowed user ID, and only you see the replies. " +
                        "Discord messages are not end-to-end encrypted.")));
        return Page("Discord", t);
    }

    private Panel EmailTab()
    {
        var t = Table();
        SpanRow(t, ChannelHeader("E-posta"));
        Row(t, "", _mailOn);
        Row(t, L.T("SMTP sunucusu", "SMTP server"), _mailHost);
        Row(t, "Port", _mailPort);
        Row(t, L.T("Kullanıcı adı", "Username"), _mailUser);
        Row(t, L.T("Şifre", "Password"), _mailPass);
        Row(t, L.T("Gönderen", "From"), _mailFrom);
        Row(t, L.T("Alıcı(lar)", "To"), _mailTo);
        var pgpRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        _mailPgp.Width = 300;
        var browse = new FlatButton(L.T("Seç…", "Browse…"));
        browse.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = L.T("PGP açık anahtarı (*.asc;*.gpg;*.pgp)|*.asc;*.gpg;*.pgp|Tümü|*.*", "PGP public key (*.asc;*.gpg;*.pgp)|*.asc;*.gpg;*.pgp|All files|*.*") };
            if (dlg.ShowDialog(this) == DialogResult.OK) _mailPgp.Text = dlg.FileName;
        };
        pgpRow.Controls.Add(_mailPgp);
        pgpRow.Controls.Add(browse);
        Row(t, L.T("PGP açık anahtarı", "PGP public key"), pgpRow);
        Row(t, "", _mailMinimal);
        Row(t, "", TestButton("E-posta"));
        Row(t, "", Note(L.T("Gmail için uygulama şifresi kullanın (smtp.gmail.com, 587).\n" +
                        "PGP anahtarı seçilirse mail gövdesi uçtan uca şifrelenir; konu satırı genel kalır.", "Use an app password for Gmail (smtp.gmail.com, 587).\n" +
                        "With a PGP key selected, the message body is end-to-end encrypted; the subject line stays generic.")));
        return Page(L.Channel("E-posta"), t);
    }

    private Panel SignalTab()
    {
        var t = Table();
        SpanRow(t, ChannelHeader("Signal"));
        Row(t, L.T("Mod", "Mode"), _sgMode);
        var cliRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        _sgCli.Width = 300;
        var browse = new FlatButton(L.T("Seç…", "Browse…"));
        browse.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = L.T("signal-cli (signal-cli.bat)|signal-cli*.bat;signal-cli*.exe|Tümü|*.*", "signal-cli (signal-cli.bat)|signal-cli*.bat;signal-cli*.exe|All files|*.*") };
            if (dlg.ShowDialog(this) == DialogResult.OK) _sgCli.Text = dlg.FileName;
        };
        cliRow.Controls.Add(_sgCli);
        cliRow.Controls.Add(browse);
        Row(t, L.T("signal-cli yolu", "signal-cli path"), cliRow);
        Row(t, L.T("Hesap numarası", "Account number"), _sgAccount);
        Row(t, L.T("Alıcı numara", "Recipient number"), _sgRecipient);
        Row(t, "", _sgSelf);
        Row(t, L.T("Daemon portu", "Daemon port"), _sgPort);
        Row(t, "", _sgMinimal);
        Row(t, "", TestButton("Signal"));
        Row(t, "", Note(L.T("Uçtan uca şifreli. signal-cli ve Java 21+ gerekir.\n" +
                        "Numaralar uluslararası biçimde: +905xxxxxxxxx.\n" +
                        "Sadece bildirim: her uyarıda signal-cli kısa süre çalışıp kapanır, boşta yük yok.\n" +
                        "Bildirim + komut: signal-cli sürekli çalışır (~150-250 MB RAM).", "End-to-end encrypted. Needs signal-cli and Java 21+.\n" +
                        "Numbers in international format: +15551234567.\n" +
                        "Notifications only: signal-cli runs briefly for each alert, nothing stays loaded.\n" +
                        "Notifications + commands: signal-cli keeps running (~150-250 MB RAM).")));
        return Page("Signal", t);
    }

    private void LoadValues()
    {
        _interval.Value = _cfg.IntervalSeconds;
        _hostname.Checked = _cfg.IncludeHostname;
        _local.Checked = _cfg.Local.Enabled;
        _startup.Checked = Startup.IsEnabled();
        foreach (var (metric, cb) in _trayReadings) cb.Checked = _cfg.TrayReadings.Contains(metric);
        var preset = Array.FindIndex(TrayColorPresets, c => string.Equals(c, _cfg.TrayNumberColor, StringComparison.OrdinalIgnoreCase));
        if (preset < 0) _customTrayColor = _cfg.TrayNumberColor;
        _trayColorIndex = preset >= 0 ? preset : TrayColorPresets.Length;
        _trayColor.SelectedIndex = _trayColorIndex;
        _language.SelectedIndex = (int)_cfg.Language;
        _autoUpdate.Checked = _cfg.AutoUpdate;
        _uiTheme.SelectedIndex = (int)_cfg.UiTheme;
        _reduceMotion.Checked = _cfg.ReduceMotion;
        var hw = _cfg.Hardware;
        _hw["Cpu"].Checked = hw.Cpu; _hw["Gpu"].Checked = hw.Gpu; _hw["GpuDetailed"].Checked = hw.GpuDetailed; _hw["Storage"].Checked = hw.Storage; _hw["StorageDetailed"].Checked = hw.StorageDetailed;
        _hw["Motherboard"].Checked = hw.Motherboard; _hw["Memory"].Checked = hw.Memory;
        _hw["Controller"].Checked = hw.Controller; _hw["Network"].Checked = hw.Network; _hw["Psu"].Checked = hw.Psu;

        foreach (var r in _cfg.Rules)
        {
            var i = _grid.Rows.Add(r.Enabled, $"{MetricInfo.Label(r.Metric)} ({MetricInfo.Unit(r.Metric)})",
                r.Limit, r.DurationSec, r.CooldownMin, r.Hysteresis);
            _grid.Rows[i].Tag = r.Metric;
        }

        _tgMode.SelectedIndex = (int)_cfg.Telegram.Mode;
        _tgToken.Text = _cfg.Telegram.BotToken;
        _tgChat.Text = _cfg.Telegram.ChatId == 0 ? "" : _cfg.Telegram.ChatId.ToString();
        _tgMinimal.Checked = _cfg.Telegram.Minimal;

        _dcMode.SelectedIndex = (int)_cfg.Discord.Mode;
        _dcWebhook.Text = _cfg.Discord.WebhookUrl;
        _dcToken.Text = _cfg.Discord.BotToken;
        _dcChannel.Text = _cfg.Discord.ChannelId;
        _dcUser.Text = _cfg.Discord.AllowedUserId;
        _dcMinimal.Checked = _cfg.Discord.Minimal;

        var m = _cfg.Email;
        _mailOn.Checked = m.Enabled; _mailHost.Text = m.SmtpHost; _mailPort.Value = m.Port; _mailUser.Text = m.Username;
        _mailPass.Text = m.Password; _mailFrom.Text = m.From; _mailTo.Text = m.To; _mailPgp.Text = m.PgpPublicKeyPath;
        _mailMinimal.Checked = m.Minimal;

        var s = _cfg.Signal;
        _sgMode.SelectedIndex = (int)s.Mode; _sgCli.Text = s.CliPath; _sgAccount.Text = s.Account; _sgRecipient.Text = s.Recipient;
        _sgSelf.Checked = s.NoteToSelf; _sgPort.Value = s.DaemonPort; _sgMinimal.Checked = s.Minimal;

        _pinOn.Checked = _cfg.Security.PinEnabled;
        _pin.Text = _cfg.Security.Pin;
    }

    private AppConfig Collect()
    {
        var c = _cfg.Clone();
        c.IntervalSeconds = (int)_interval.Value;
        c.IncludeHostname = _hostname.Checked;
        c.Local.Enabled = _local.Checked;
        c.TrayReadings = _trayReadings.Where(kv => kv.Value.Checked).Select(kv => kv.Key).ToList();
        c.TrayNumberColor = _trayColorIndex < TrayColorPresets.Length ? TrayColorPresets[_trayColorIndex] : _customTrayColor;
        c.Language = (UiLanguage)Math.Max(0, _language.SelectedIndex);
        c.AutoUpdate = _autoUpdate.Checked;
        c.UiTheme = (UiTheme)Math.Max(0, _uiTheme.SelectedIndex);
        c.ReduceMotion = _reduceMotion.Checked;
        c.Hardware = new HardwareConfig
        {
            Cpu = _hw["Cpu"].Checked, Gpu = _hw["Gpu"].Checked, GpuDetailed = _hw["GpuDetailed"].Checked, Storage = _hw["Storage"].Checked, StorageDetailed = _hw["StorageDetailed"].Checked,
            Motherboard = _hw["Motherboard"].Checked, Memory = _hw["Memory"].Checked,
            Controller = _hw["Controller"].Checked, Network = _hw["Network"].Checked, Psu = _hw["Psu"].Checked,
        };

        foreach (DataGridViewRow row in _grid.Rows)
        {
            var r = c.RuleFor((Metric)row.Tag!);
            r.Enabled = row.Cells[0].Value is true;
            r.Limit = ParseD(row.Cells[2].Value, r.Limit);
            r.DurationSec = (int)ParseD(row.Cells[3].Value, r.DurationSec);
            r.CooldownMin = (int)ParseD(row.Cells[4].Value, r.CooldownMin);
            r.Hysteresis = ParseD(row.Cells[5].Value, r.Hysteresis);
        }

        c.Telegram.Mode = (TelegramMode)_tgMode.SelectedIndex;
        c.Telegram.BotToken = _tgToken.Text.Trim();
        c.Telegram.ChatId = long.TryParse(_tgChat.Text.Trim(), out var chat) ? chat : 0;
        c.Telegram.Minimal = _tgMinimal.Checked;

        c.Discord.Mode = (DiscordMode)_dcMode.SelectedIndex;
        c.Discord.WebhookUrl = _dcWebhook.Text.Trim();
        c.Discord.BotToken = _dcToken.Text.Trim();
        c.Discord.ChannelId = _dcChannel.Text.Trim();
        c.Discord.AllowedUserId = _dcUser.Text.Trim();
        c.Discord.Minimal = _dcMinimal.Checked;

        c.Email.Enabled = _mailOn.Checked; c.Email.SmtpHost = _mailHost.Text.Trim(); c.Email.Port = (int)_mailPort.Value;
        c.Email.Username = _mailUser.Text.Trim(); c.Email.Password = _mailPass.Text; c.Email.From = _mailFrom.Text.Trim();
        c.Email.To = _mailTo.Text.Trim(); c.Email.PgpPublicKeyPath = _mailPgp.Text.Trim(); c.Email.Minimal = _mailMinimal.Checked;

        c.Signal.Mode = (SignalMode)_sgMode.SelectedIndex; c.Signal.CliPath = _sgCli.Text.Trim();
        c.Signal.Account = _sgAccount.Text.Trim(); c.Signal.Recipient = _sgRecipient.Text.Trim();
        c.Signal.NoteToSelf = _sgSelf.Checked; c.Signal.DaemonPort = (int)_sgPort.Value; c.Signal.Minimal = _sgMinimal.Checked;

        c.Security.PinEnabled = _pinOn.Checked;
        c.Security.Pin = _pin.Text.Trim();
        return c;
    }

    private string? Validate(AppConfig c)
    {
        if (c.Security.PinEnabled && c.Security.Pin.Length < 4) return L.T("PIN en az 4 karakter olmalı.", "The PIN must be at least 4 characters.");
        if (c.Security.Pin.Contains(' ')) return L.T("PIN boşluk içeremez.", "The PIN can't contain spaces.");
        if (c.Discord.Mode == DiscordMode.Webhook && !c.Discord.WebhookUrl.StartsWith("https://discord.com/api/webhooks/", StringComparison.OrdinalIgnoreCase)
            && !c.Discord.WebhookUrl.StartsWith("https://discordapp.com/api/webhooks/", StringComparison.OrdinalIgnoreCase))
            return L.T("Discord webhook URL'si https://discord.com/api/webhooks/ ile başlamalı.", "The Discord webhook URL must start with https://discord.com/api/webhooks/.");
        if (c.Discord.Mode == DiscordMode.Bot && c.Discord.AllowedUserId.Length == 0)
            return L.T("Discord bot modunda izinli kullanıcı ID'si gerekli.", "Discord bot mode needs an allowed user ID.");
        if (c.Email.PgpPublicKeyPath.Length > 0)
        {
            try
            {
                Pgp.ReadEncryptionKey(c.Email.PgpPublicKeyPath);
            }
            catch (Exception ex)
            {
                return L.T("PGP anahtarı okunamadı: ", "Couldn't read the PGP key: ") + ex.Message;
            }
        }
        return null;
    }

    private void Save()
    {
        var c = Collect();
        var error = Validate(c);
        if (error != null)
        {
            MessageBox.Show(this, error, "SysWatch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            if (_startup.Checked && !Startup.IsEnabled()) Startup.Enable();
            if (!_startup.Checked) Startup.Disable();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, L.T("Başlangıç görevi", "Startup task"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        var languageChanged = c.Language != _app.Config.Language;
        _app.SaveAndApply(c);
        _cfg = c.Clone();
        _dirty = false;
        _dirtyLabel.Text = L.T("Kaydedildi", "Saved");
        RefreshChannelRows();
        if (languageChanged)
        {
            var page = Math.Max(0, _nav.Selected);
            BeginInvoke(() =>
            {
                Close();
                _app.OpenPage(page);
            });
        }
    }

    private void Revert()
    {
        _cfg = _app.Config.Clone();
        _loading = true;
        _grid.Rows.Clear();
        LoadValues();
        _loading = false;
        _dirty = false;
        _dirtyLabel.Text = "";
        RefreshChannelRows();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _statusTimer.Dispose();
        base.Dispose(disposing);
    }

    private Button TestButton(string notifierName)
    {
        var b = new FlatButton(L.T("Test bildirimi gönder", "Send test notification"));
        b.Click += async (_, _) =>
        {
            var c = Collect();
            var notifier = _app.Hub.Notifiers.First(n => n.Name == notifierName);
            if (!notifier.IsEnabled(c))
            {
                MessageBox.Show(this, L.T("Bu kanal kapalı ya da gerekli alanlar boş.", "This channel is off or required fields are empty."), "Test", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            b.Enabled = false;
            var error = await _app.Hub.SendToAsync(notifier, Notification.Info(L.T("Test bildirimi", "Test notification"), L.T("Bu bir SysWatch test mesajıdır.", "This is a SysWatch test message.")), c);
            b.Enabled = true;
            MessageBox.Show(this, error == null ? L.T("Gönderildi.", "Sent.") : L.T("Gönderilemedi: ", "Couldn't send: ") + error, "Test",
                MessageBoxButtons.OK, error == null ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        };
        return b;
    }

    private async Task OpenDiscordInvite(Button button, bool userInstall)
    {
        var token = _dcToken.Text.Trim();
        if (token.Length == 0)
        {
            MessageBox.Show(this, L.T("Önce bot token'ını girin.", "Enter the bot token first."), "Discord");
            return;
        }
        button.Enabled = false;
        try
        {
            var url = await DiscordApi.InviteUrlAsync(token, userInstall, CancellationToken.None);
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, L.T("Davet bağlantısı oluşturulamadı: ", "Couldn't create the invite link: ") + ex.Message, "Discord", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            button.Enabled = true;
        }
    }

    private async Task FindTelegramChat()
    {
        var token = _tgToken.Text.Trim();
        if (token.Length == 0)
        {
            MessageBox.Show(this, L.T("Önce bot token'ını girin.", "Enter the bot token first."), "Telegram");
            return;
        }
        try
        {
            var updates = await TelegramApi.GetUpdatesAsync(token, 0, 0, CancellationToken.None);
            var chat = updates.Reverse()
                .Select(u => u?["message"]?["chat"])
                .FirstOrDefault(ch => ch?["type"]?.GetValue<string>() == "private");
            if (chat == null)
            {
                MessageBox.Show(this, L.T("Mesaj bulunamadı. Botunuza özel mesajla /start yazıp tekrar deneyin.", "No message found. Send /start to your bot in a private chat and try again."), "Telegram");
                return;
            }
            _tgChat.Text = chat["id"]!.GetValue<long>().ToString();
            var who = chat["username"]?.GetValue<string>() ?? chat["first_name"]?.GetValue<string>() ?? "";
            MessageBox.Show(this, L.T($"Chat ID bulundu: {_tgChat.Text} ({who}). Bu sizseniz Kaydet'e basın.", $"Found chat ID {_tgChat.Text} ({who}). If that's you, click Save."), "Telegram");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, L.T("Telegram'a ulaşılamadı: ", "Couldn't reach Telegram: ") + ex.Message, "Telegram");
        }
    }

    private static double ParseD(object? v, double fallback) =>
        double.TryParse(Convert.ToString(v)?.Replace(',', '.'), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : fallback;

    private static TableLayoutPanel Table()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(0, 0, 8, 8) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return t;
    }

    private static void SpanRow(TableLayoutPanel t, Control c)
    {
        var row = t.RowCount++;
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(c, 0, row);
        t.SetColumnSpan(c, 2);
    }

    private static void Row(TableLayoutPanel t, string label, Control c)
    {
        var row = t.RowCount++;
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 8, 3) }, 0, row);
        if (c is TextBox or ComboBox) c.Dock = DockStyle.Fill;
        c.Margin = new Padding(3, 4, 3, 4);
        t.Controls.Add(c, 1, row);
    }

    private static Panel Page(string title, Control content)
    {
        var page = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Name = title };
        page.Controls.Add(content);
        return page;
    }

    private static Label Note(string text) => new()
    {
        Text = text, AutoSize = true, MaximumSize = new Size(460, 0), Tag = "muted",
    };

    private static LinkLabel Link(string text, string target)
    {
        var l = new LinkLabel { Text = text, AutoSize = true, MaximumSize = new Size(460, 0) };
        l.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        return l;
    }

    private static TextBox Secret() => new() { UseSystemPasswordChar = true };
    private static CheckBox MinimalBox() => new() { Text = L.T("Az bilgi modu (değer ve ayrıntı gönderme)", "Minimal mode (send no values or details)"), AutoSize = true };
    private static NumericUpDown Num(int min, int max) => new() { Minimum = min, Maximum = max, Width = 90 };

    private static ComboBox Combo(params string[] items)
    {
        var c = new AeroComboBox();
        c.Items.AddRange(items);
        return c;
    }
}
