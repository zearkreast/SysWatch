using SysWatch.Config;
using SysWatch.Monitoring;

namespace SysWatch.UI;

// Right-click summary; closes on focus loss.
public sealed class TrayPopup : AeroForm
{
    private readonly IUiHost _app;
    private readonly Action<int> _open;
    private readonly MetricCard[] _tiles;
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 1000 };
    private string _status = "";
    private Color _statusColor = Theme.SuccessFill;

    protected override string GlassTitle => "SysWatch";
    protected override string GlassStatus => _status;
    protected override Color GlassStatusColor => _statusColor;

    public TrayPopup(IUiHost app, Action<int> open, Action exit)
    {
        _app = app;
        _open = open;
        Draggable = false;
        Resizable = false;
        CaptionButtons = false;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        StartPosition = FormStartPosition.Manual;
        Text = "SysWatch";
        ApplyGlassPadding();
        var unit = Font.Height;
        ClientSize = new Size(unit * 24, unit * 24);

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(unit / 2, unit / 2, unit / 2, 0) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        _tiles = new[] { "CPU", "GPU", "RAM", "Disk" }.Select(label =>
        {
            var t = new MetricCard(Font) { Label = label, Dock = DockStyle.Fill, Margin = new Padding(4) };
            grid.Controls.Add(t);
            return t;
        }).ToArray();

        var actions = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = (int)(unit * 5.6), ColumnCount = 3, RowCount = 2, Padding = new Padding(unit / 2, 4, unit / 2, unit / 2) };
        for (var i = 0; i < 3; i++) actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var iconSize = Icons.SizeFor(Font) - 4;
        FlatButton Action(string text, bool primary, EventHandler click, string icon)
        {
            var b = new FlatButton(text, primary) { AutoSize = false, Dock = DockStyle.Fill, Margin = new Padding(3), Image = Icons.Get(icon, iconSize) };
            b.Click += click;
            actions.Controls.Add(b);
            return b;
        }
        Action("Aç", true, (_, _) => Go(MainWindow.PageStatus), Icons.Status);
        Action("Geçmiş", false, (_, _) => Go(MainWindow.PageHistory), Icons.History);
        Action("Ayarlar", false, (_, _) => Go(MainWindow.PageSettings), Icons.App);
        var mute = Action(app.Hub.Muted ? "Sesi aç" : "Sustur", false, (_, _) => { }, Icons.Mute);
        mute.Click += (_, _) =>
        {
            if (_app.Hub.Muted) { _app.Mute(0); Close(); }
            else ShowMuteMenu(mute);
        };
        Action("Günlük", false, (_, _) =>
        {
            Close();
            var path = Log.FilePath;
            if (!File.Exists(path)) File.WriteAllText(path, "");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }, Icons.Log);
        Action("Çıkış", false, (_, _) => { Close(); exit(); }, Icons.Exit);

        Controls.Add(grid);
        Controls.Add(actions);
        _refresh.Tick += (_, _) => RefreshValues();
    }

    private void Go(int page)
    {
        Close();
        _open(page);
    }

    // Opens above the tray overflow flyout (the "^" hidden-icons window) when it is open, so it never covers it;
    // otherwise right above the clicked icon. Topmost and in the foreground so nothing hides it.
    public void ShowAboveTray(Point click)
    {
        _ = Handle;
        PerformLayout();
        var screen = Screen.FromPoint(click);
        var area = screen.WorkingArea;
        var bounds = screen.Bounds;
        const int gap = 10;
        var anchor = OverflowFlyout(screen) ?? new Rectangle(click.X, area.Bottom, 1, 0);
        int x, y, dx = 0, dy = 0;
        if (area.Bottom < bounds.Bottom || anchor.Height > 0)
        {
            x = anchor.Left + anchor.Width / 2 - Width / 2;
            y = anchor.Top - Height - gap;
            dy = 14;
            if (y < area.Top + gap) { y = area.Top + gap; x = anchor.Left - Width - gap; dy = 0; dx = 14; }
        }
        else if (area.Top > bounds.Top) { x = click.X - Width / 2; y = area.Top + gap; dy = -14; }
        else if (area.Left > bounds.Left) { x = area.Left + gap; y = click.Y - Height / 2; dx = -14; }
        else { x = area.Right - Width - gap; y = click.Y - Height / 2; dx = 14; }
        var target = new Point(Math.Clamp(x, area.Left + gap, area.Right - Width - gap), Math.Clamp(y, area.Top + gap, area.Bottom - Height - gap));
        Location = new Point(target.X + dx, target.Y + dy);
        RefreshValues();
        Show();
        Activate();
        SetForegroundWindow(Handle);
        _refresh.Start();
        Motion.Run(this, 180, t =>
        {
            if (!IsDisposed) Location = new Point((int)(target.X + dx * (1 - t)), (int)(target.Y + dy * (1 - t)));
        });
    }

    // The hidden-icons flyout: Windows 11 uses a XAML island window, Windows 10 the classic overflow window.
    private static Rectangle? OverflowFlyout(Screen screen)
    {
        foreach (var cls in new[] { "TopLevelWindowForOverflowXamlIsland", "NotifyIconOverflowWindow" })
        {
            var h = FindWindow(cls, null);
            if (h == IntPtr.Zero || !IsWindowVisible(h) || !GetWindowRect(h, out var r)) continue;
            var rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            if (rect.Width > 0 && rect.Height > 0 && screen.Bounds.IntersectsWith(rect)) return rect;
        }
        return null;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr FindWindow(string cls, string? title);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    private void RefreshValues()
    {
        var snap = _app.LastSnapshot;
        var active = _app.ActiveAlerts;
        var cfg = _app.Config;
        (Metric main, Metric? second)[] map = [(Metric.CpuTemp, Metric.CpuLoad), (Metric.GpuTemp, Metric.GpuLoad), (Metric.Ram, null), (Metric.DiskTemp, null)];
        for (var i = 0; i < _tiles.Length; i++)
        {
            var (main, second) = map[i];
            var tile = _tiles[i];
            var v = snap?.Get(main);
            var s = second is { } sm ? snap?.Get(sm) : null;
            var metric = v == null && s != null ? second!.Value : main;
            var value = v ?? s;
            tile.Value = value == null ? "—" : MetricInfo.Format(metric, value.Value);
            tile.Sub = v != null && s != null ? "· " + MetricInfo.Format(second!.Value, s.Value) : "";
            tile.Unit = MetricInfo.Unit(metric);
            tile.Series = _app.History.Series(metric);
            tile.Alarm = active.Contains(main) || (second is { } sec && active.Contains(sec));
            var rule = cfg.RuleFor(metric);
            tile.Limit = tile.Alarm && rule.Enabled ? rule.Limit : null;
            tile.Invalidate();
        }
        var (status, color) = _app.Hub.Muted
            ? ($"Susturuldu · {_app.Hub.MutedUntil:HH:mm}", Theme.Neutral)
            : active.Count > 0 ? (active.Count == 1 ? "1 uyarı" : $"{active.Count} uyarı", Theme.DangerFill) : ("Normal", Theme.SuccessFill);
        if (status == _status && color == _statusColor) return;
        _status = status;
        _statusColor = color;
        Invalidate(new Rectangle(0, 0, Width, GlassTop));
    }

    private void ShowMuteMenu(Control anchor)
    {
        var menu = new ContextMenuStrip();
        foreach (var (label, minutes) in new[] { ("30 dakika", 30), ("1 saat", 60), ("4 saat", 240) })
            menu.Items.Add(label, null, (_, _) => { _app.Mute(minutes); Close(); });
        Theme.StyleMenu(menu);
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(anchor, new Point(0, anchor.Height));
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        BeginInvoke(() =>
        {
            if (!IsDisposed && !ContainsFocus && ActiveForm != this) Close();
        });
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) Close();
        base.OnKeyDown(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _refresh.Dispose();
        base.Dispose(disposing);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
}
