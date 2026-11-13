using System.Globalization;
using SysWatch.Config;
using SysWatch.Monitoring;

namespace SysWatch.UI;

// "Durum" page: four live cards with sparklines, a mute button and the last-reading note.
internal sealed class DashboardView : Panel
{
    private readonly IUiHost _app;
    private readonly MetricCard _cpu, _gpu, _ram, _disk;
    private readonly Label _note = new() { AutoSize = true, Tag = "muted" };
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 1000 };

    public DashboardView(IUiHost app, Font font, Action openDetails)
    {
        _app = app;
        Dock = DockStyle.Fill;
        var unit = font.Height;
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        MetricCard Card(string label)
        {
            var c = new MetricCard(font) { Label = label, Dock = DockStyle.Fill, Margin = new Padding(5) };
            grid.Controls.Add(c);
            return c;
        }
        _cpu = Card("CPU");
        _gpu = Card("GPU");
        _ram = Card("RAM");
        _disk = Card("Disk");

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = (int)(unit * 2.8) };
        var mute = new FlatButton(L.T("Bildirimleri sustur", "Mute notifications")) { Image = Icons.Get(Icons.Mute, Icons.SizeFor(font) - 4) };
        var details = new FlatButton(L.T("Detaylar", "Details")) { Image = Icons.Get(Icons.Details, Icons.SizeFor(font) - 4) };
        details.Click += (_, _) => openDetails();
        bottom.Controls.Add(_note);
        bottom.Controls.Add(mute);
        bottom.Controls.Add(details);
        bottom.Layout += (_, _) =>
        {
            mute.Location = new Point(bottom.Width - mute.Width - 5, (bottom.Height - mute.Height) / 2);
            details.Location = new Point(mute.Left - details.Width - 6, mute.Top);
            _note.Location = new Point(6, (bottom.Height - _note.Height) / 2);
        };
        mute.Click += (_, _) => ShowMuteMenu(mute);

        Controls.Add(grid);
        Controls.Add(bottom);
        _refresh.Tick += (_, _) => RefreshValues();
        VisibleChanged += (_, _) =>
        {
            if (Visible) { RefreshValues(); _refresh.Start(); }
            else _refresh.Stop();
        };
    }

    public void RefreshValues()
    {
        var snap = _app.LastSnapshot;
        var active = _app.ActiveAlerts;
        var cfg = _app.Config;
        var hist = _app.History;

        void Fill(MetricCard card, string label, Metric main, Metric? secondary)
        {
            var v = snap?.Get(main);
            var s = secondary is { } sm ? snap?.Get(sm) : null;
            var showSecondaryAsMain = v == null && s != null;
            var metric = showSecondaryAsMain ? secondary!.Value : main;
            var value = showSecondaryAsMain ? s : v;
            card.Value = value == null ? "—" : MetricInfo.Format(metric, value.Value);
            card.Unit = MetricInfo.Unit(metric);
            card.Sub = !showSecondaryAsMain && s != null ? "· " + MetricInfo.Format(secondary!.Value, s.Value) : "";
            card.Series = hist.Series(metric);
            var rule = cfg.RuleFor(metric);
            card.Alarm = active.Contains(main) || (secondary is { } sec && active.Contains(sec));
            card.Limit = card.Alarm && rule.Enabled ? rule.Limit : null;
            card.Label = card.Alarm && rule.Enabled ? L.T($"{label} · eşik {MetricInfo.Format(metric, rule.Limit)}", $"{label} · threshold {MetricInfo.Format(metric, rule.Limit)}") : label;
            card.Invalidate();
        }

        Fill(_cpu, "CPU", Metric.CpuTemp, Metric.CpuLoad);
        Fill(_gpu, "GPU", Metric.GpuTemp, Metric.GpuLoad);
        Fill(_ram, "RAM", Metric.Ram, null);
        Fill(_disk, "Disk", Metric.DiskTemp, null);

        _note.Text = snap?.Get(Metric.CpuTemp) == null && cfg.Hardware.Cpu
            ? L.T("CPU sıcaklığı için PawnIO sürücüsü gerekli", "CPU temperature needs the PawnIO driver")
            : snap == null ? L.T("İlk ölçüm bekleniyor…", "Waiting for the first reading…") : L.T($"Son ölçüm {snap.Time:HH:mm:ss}", $"Last reading {snap.Time:HH:mm:ss}");
    }

    private void ShowMuteMenu(Control anchor)
    {
        var menu = new ContextMenuStrip();
        foreach (var (label, minutes) in new[] { (L.T("30 dakika", "30 minutes"), 30), (L.T("1 saat", "1 hour"), 60), (L.T("4 saat", "4 hours"), 240) })
            menu.Items.Add(label, null, (_, _) => _app.Mute(minutes));
        menu.Items.Add(L.T("Susturmayı kaldır", "Unmute"), null, (_, _) => _app.Mute(0));
        Theme.StyleMenu(menu);
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(anchor, new Point(0, anchor.Height));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _refresh.Dispose();
        base.Dispose(disposing);
    }
}

// "Geçmiş" page: event cards grouped by day, summary and a clear button.
internal sealed class HistoryView : Panel
{
    private readonly IUiHost _app;
    private readonly EventList _list;
    private readonly Label _summary = new() { AutoSize = true, Tag = "muted" };

    public HistoryView(IUiHost app, Font font)
    {
        _app = app;
        Dock = DockStyle.Fill;
        var unit = font.Height;
        _list = new EventList(font) { Dock = DockStyle.Fill };
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = (int)(unit * 2.8) };
        var clear = new FlatButton(L.T("Geçmişi temizle", "Clear history"));
        var log = new FlatButton(L.T("Günlük", "Log")) { Image = Icons.Get(Icons.Log, Icons.SizeFor(font) - 4) };
        log.Click += (_, _) => MainWindow.OpenLog();
        bottom.Controls.Add(_summary);
        bottom.Controls.Add(clear);
        bottom.Controls.Add(log);
        bottom.Layout += (_, _) =>
        {
            clear.Location = new Point(bottom.Width - clear.Width - 5, (bottom.Height - clear.Height) / 2);
            log.Location = new Point(clear.Left - log.Width - 6, clear.Top);
            _summary.Location = new Point(6, (bottom.Height - _summary.Height) / 2);
        };
        clear.Click += (_, _) =>
        {
            if (MessageBox.Show(FindForm(), L.T("Tüm uyarı geçmişi silinsin mi? Bu geri alınamaz.", "Delete the whole alert history? This can't be undone."), L.T("Geçmişi temizle", "Clear history"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            _app.Events.Clear();
            Reload();
        };
        Controls.Add(_list);
        Controls.Add(bottom);
        _app.Events.Added += OnAdded;
        VisibleChanged += (_, _) => { if (Visible) Reload(); };
    }

    public void Reload()
    {
        var entries = _app.Events.ReadAll();
        _list.SetEntries(entries);
        var alerts = entries.Count(e => e.Kind == EventKind.Alert);
        _summary.Text = entries.Count == 0 ? L.T("Son 7 gün", "Last 7 days") : L.T($"Son 7 gün · {entries.Count} kayıt, {alerts} uyarı", $"Last 7 days · {entries.Count} entries, {alerts} alerts");
    }

    private void OnAdded(EventEntry _)
    {
        if (IsHandleCreated && Visible) BeginInvoke(Reload);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _app.Events.Added -= OnAdded;
        base.Dispose(disposing);
    }
}

internal sealed class EventList : ScrollableControl
{
    private readonly List<(string? Header, EventEntry? Entry)> _rows = new();
    private readonly Font _header, _title, _icons;
    private int _hover = -1;

    private int RowHeight => (int)(Font.Height * 3.1);
    private int HeaderHeight => (int)(Font.Height * 2.2);

    public EventList(Font font)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Font = font;
        _header = new Font(font.FontFamily, font.Size * 0.95f, FontStyle.Bold);
        _title = new Font(font.FontFamily, font.Size * 1.02f);
        _icons = Draw.Icons(font.SizeInPoints * 0.95f);
        AutoScroll = true;
    }

    public void SetEntries(List<EventEntry> entries)
    {
        _rows.Clear();
        DateTime? day = null;
        foreach (var e in entries)
        {
            if (day != e.Time.Date)
            {
                day = e.Time.Date;
                _rows.Add((DayName(e.Time.Date), null));
            }
            _rows.Add((null, e));
        }
        AutoScrollMinSize = new Size(0, TotalHeight() + 12);
        Invalidate();
    }

    private int TotalHeight() => _rows.Sum(r => r.Header != null ? HeaderHeight : RowHeight);

    private static string DayName(DateTime d) =>
        d == DateTime.Today ? L.T("Bugün", "Today") :
        d == DateTime.Today.AddDays(-1) ? L.T("Dün", "Yesterday") :
        d.ToString(L.T("d MMMM dddd", "dddd, MMMM d"), L.Culture);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Draw.Setup(g);
        g.Clear(Draw.Backdrop(this));
        if (_rows.Count == 0)
        {
            Draw.Text(g, L.T("Henüz uyarı yok. Bir eşik aşıldığında burada görünecek.", "No alerts yet. They show up here when a threshold is crossed."), Font, Theme.TextMuted,
                ClientRectangle, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        var offset = -AutoScrollPosition.Y;
        var y = 6 - offset;
        for (var i = 0; i < _rows.Count; i++)
        {
            var (header, entry) = _rows[i];
            var h = header != null ? HeaderHeight : RowHeight;
            if (y + h >= 0 && y <= Height)
            {
                if (header != null) PaintHeader(g, header, new Rectangle(4, y, Width - 8, h));
                else PaintEntry(g, entry!, new Rectangle(4, y + 3, Width - 8 - (VerticalScroll.Visible ? 2 : 0), h - 6), i == _hover);
            }
            y += h;
        }
    }

    private void PaintHeader(Graphics g, string text, Rectangle r)
    {
        var tw = TextRenderer.MeasureText(text, _header).Width;
        Draw.Text(g, text, _header, Theme.TextSecondary, new Rectangle(r.X + 4, r.Y + r.Height / 3, tw + 8, r.Height * 2 / 3));
        using var line = new Pen(Theme.Border);
        var ly = r.Y + r.Height * 2 / 3;
        g.DrawLine(line, r.X + tw + 16, ly, r.Right - 4, ly);
    }

    private void PaintEntry(Graphics g, EventEntry entry, Rectangle r, bool hover)
    {
        Glass.Gloss(g, r, hover ? Theme.ButtonHoverGloss : Theme.CardGloss, 5, hover ? Theme.SelectionBorder : Theme.Border);

        var icon = entry.Kind switch
        {
            EventKind.Alert => Icons.Thresholds,
            EventKind.Recovered => Icons.Recovered,
            EventKind.Command => Icons.Terminal,
            _ when entry.Text.Contains("sustur", StringComparison.OrdinalIgnoreCase) || entry.Text.Contains("mute", StringComparison.OrdinalIgnoreCase) => Icons.Mute,
            _ => Icons.Info,
        };
        var d = Math.Min(32, (int)(r.Height * 0.62));
        var orb = new Rectangle(r.X + 10, r.Y + (r.Height - d) / 2, d, d);
        g.DrawImage(Icons.Get(icon, d), orb.Location);

        var parts = entry.Text.Split(" · ", 2);
        var time = entry.Time.ToString("HH:mm");
        var tw = TextRenderer.MeasureText(time, Font).Width;
        var textX = orb.Right + 10;
        var textW = r.Right - tw - 16 - textX;
        if (parts.Length > 1)
        {
            Draw.Text(g, parts[0], _title, Theme.Text, new Rectangle(textX, r.Y + 4, textW, r.Height / 2 - 2), TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            Draw.Text(g, parts[1], Font, Theme.TextSecondary, new Rectangle(textX, r.Y + r.Height / 2 + 1, textW, r.Height / 2 - 5), TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
        else
        {
            Draw.Text(g, entry.Text, _title, Theme.Text, new Rectangle(textX, r.Y, textW, r.Height));
        }
        Draw.Text(g, time, Font, Theme.TextMuted, new Rectangle(r.Right - tw - 10, r.Y, tw, r.Height), TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var offset = -AutoScrollPosition.Y;
        var y = 6 - offset;
        var hit = -1;
        for (var i = 0; i < _rows.Count; i++)
        {
            var h = _rows[i].Header != null ? HeaderHeight : RowHeight;
            if (e.Y >= y && e.Y < y + h && _rows[i].Header == null) hit = i;
            y += h;
        }
        if (hit != _hover) { _hover = hit; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = -1;
        Invalidate();
    }

    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _header.Dispose();
            _title.Dispose();
            _icons.Dispose();
        }
        base.Dispose(disposing);
    }
}

// "Detaylar": every sensor of every enabled group, refreshed while visible, plus the groups switched off in the
// settings with a link to the hardware page instead of a switch here.
internal sealed class DetailsView : Panel
{
    private readonly IUiHost _app;
    private readonly SensorList _list;
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 2000 };

    public DetailsView(IUiHost app, Font font, Action back, Action openHardware)
    {
        _app = app;
        Dock = DockStyle.Fill;
        _list = new SensorList(font, openHardware) { Dock = DockStyle.Fill };
        var header = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(0, 0, 0, 6) };
        var backLink = new LinkLabel { Text = L.T("‹ Durum", "‹ Status"), AutoSize = true, Margin = new Padding(0, 8, 12, 0) };
        backLink.LinkClicked += (_, _) => back();
        var title = new Label
        {
            Text = L.T("Tüm okumalar", "All readings"), AutoSize = true,
            Font = new Font(font.FontFamily, font.Size * 1.35f), Margin = new Padding(0, 0, 0, 0),
        };
        header.Controls.Add(backLink);
        header.Controls.Add(title);
        Controls.Add(_list);
        Controls.Add(header);
        _refresh.Tick += (_, _) => Reload();
        VisibleChanged += (_, _) =>
        {
            if (Visible) { Reload(); _refresh.Start(); }
            else _refresh.Stop();
        };
    }

    private void Reload()
    {
        var hw = _app.Config.Hardware;
        var off = new List<(string Name, string Icon)>();
        if (!hw.Cpu) off.Add((L.T("İşlemci (CPU)", "Processor (CPU)"), Icons.Hardware));
        if (!hw.Gpu) off.Add((L.T("Ekran kartı (GPU)", "Graphics card (GPU)"), Icons.Gpu));
        else if (!hw.GpuDetailed) off.Add((L.T("GPU ayrıntılı sensörler (saat hızı, güç, fan…)", "GPU detailed sensors (clocks, power, fans…)"), Icons.Gpu));
        if (!hw.Storage) off.Add((L.T("Diskler", "Disks"), Icons.Disk));
        else if (!hw.StorageDetailed) off.Add((L.T("Disk ayrıntılı sensörler (SMART)", "Disk detailed sensors (SMART)"), Icons.Disk));
        if (!hw.Motherboard) off.Add((L.T("Anakart (voltaj, fan, sıcaklık)", "Motherboard (voltages, fans, temperatures)"), Icons.Board));
        if (!hw.Memory) off.Add((L.T("RAM modülleri", "Memory modules"), Icons.Memory));
        if (!hw.Controller) off.Add((L.T("Fan kontrolcüleri", "Fan controllers"), Icons.Board));
        if (!hw.Network) off.Add((L.T("Ağ bağdaştırıcıları", "Network adapters"), Icons.Network));
        if (!hw.Psu) off.Add((L.T("Güç kaynağı (PSU)", "Power supply (PSU)"), Icons.Power));
        _list.SetData(_app.Sensors(), off);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _refresh.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class SensorList : ScrollableControl
{
    private enum RowKind { Group, Item, OffHeader, Off }

    private sealed record Row(RowKind Kind, string Text, string Kind2 = "", string Value = "", string Icon = "");

    private readonly List<Row> _rows = new();
    private readonly Font _bold, _small;
    private readonly Action _openHardware;
    private int _hoverLink = -1;

    private int RowHeight(Row r) => r.Kind switch
    {
        RowKind.Group => (int)(Font.Height * 2.4),
        RowKind.OffHeader => (int)(Font.Height * 2.8),
        RowKind.Off => (int)(Font.Height * 2.0),
        _ => (int)(Font.Height * 1.55),
    };

    public SensorList(Font font, Action openHardware)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Font = font;
        _bold = new Font(font, FontStyle.Bold);
        _small = new Font(font.FontFamily, font.Size * 0.92f);
        _openHardware = openHardware;
        AutoScroll = true;
    }

    public void SetData(List<SensorGroup> groups, List<(string Name, string Icon)> off)
    {
        _rows.Clear();
        foreach (var grp in groups)
        {
            _rows.Add(new Row(RowKind.Group, grp.Name, Icon: IconFor(grp.Category)));
            foreach (var item in grp.Items) _rows.Add(new Row(RowKind.Item, item.Name, item.Kind, item.Value));
        }
        if (off.Count > 0)
        {
            _rows.Add(new Row(RowKind.OffHeader, L.T("Ayarlarda kapalı olan sensör grupları", "Sensor groups switched off in settings")));
            foreach (var (name, icon) in off) _rows.Add(new Row(RowKind.Off, name, Icon: icon));
        }
        AutoScrollMinSize = new Size(0, _rows.Sum(RowHeight) + 12);
        Invalidate();
    }

    private static string IconFor(string category) => category switch
    {
        "Cpu" => Icons.Hardware,
        "GpuNvidia" or "GpuAmd" or "GpuIntel" or "Gpu" => Icons.Gpu,
        "Storage" => Icons.Disk,
        "Memory" => Icons.Memory,
        "Motherboard" or "SuperIO" or "EmbeddedController" or "Cooler" => Icons.Board,
        "Network" => Icons.Network,
        "Psu" or "Battery" => Icons.Power,
        _ => Icons.Hardware,
    };

    private Rectangle LinkRect(Rectangle row)
    {
        var w = TextRenderer.MeasureText(L.T("Donanım ayarlarında aç ›", "Turn on in Hardware settings ›"), Font).Width;
        return new Rectangle(row.Right - w - 10, row.Y, w, row.Height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Draw.Setup(g);
        g.Clear(Draw.Backdrop(this));
        var y = 4 + AutoScrollPosition.Y;
        var w = Width - (VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0) - 8;
        var zebra = false;
        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            var h = RowHeight(row);
            var r = new Rectangle(4, y, w, h);
            if (y + h >= 0 && y <= Height)
            {
                switch (row.Kind)
                {
                    case RowKind.Group:
                        var groupRect = new Rectangle(r.X, r.Y + 6, r.Width, r.Height - 8);
                        Glass.Gloss(g, groupRect, Theme.CardGloss, 4, Theme.Border);
                        var isz = Math.Min(24, groupRect.Height - 6);
                        g.DrawImage(Icons.Get(row.Icon, isz), groupRect.X + 8, groupRect.Y + (groupRect.Height - isz) / 2);
                        Draw.Text(g, row.Text, _bold, Theme.Text, new Rectangle(groupRect.X + isz + 16, groupRect.Y, groupRect.Width - isz - 24, groupRect.Height));
                        zebra = false;
                        break;
                    case RowKind.Item:
                        if (zebra)
                        {
                            using var z = new SolidBrush(Color.FromArgb(Theme.Dark ? 18 : 120, Theme.SelectionGloss[0]));
                            g.FillRectangle(z, r.X + 2, r.Y, r.Width - 4, r.Height);
                        }
                        zebra = !zebra;
                        var valueW = TextRenderer.MeasureText(row.Value, _bold).Width;
                        Draw.Text(g, row.Text, Font, Theme.Text, new Rectangle(r.X + 20, r.Y, r.Width - valueW - 150, r.Height));
                        Draw.Text(g, row.Kind2, _small, Theme.TextMuted, new Rectangle(r.Right - valueW - 130, r.Y, 110, r.Height), TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
                        Draw.Text(g, row.Value, _bold, Theme.Text, new Rectangle(r.Right - valueW - 10, r.Y, valueW + 4, r.Height), TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
                        break;
                    case RowKind.OffHeader:
                        Draw.Text(g, row.Text, _bold, Theme.TextSecondary, new Rectangle(r.X + 4, r.Y + r.Height / 3, r.Width, r.Height * 2 / 3));
                        break;
                    case RowKind.Off:
                        var osz = Math.Min(20, r.Height - 8);
                        using (var attrs = new System.Drawing.Imaging.ImageAttributes())
                        {
                            attrs.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.45f });
                            var img = Icons.Get(row.Icon, osz);
                            g.DrawImage(img, new Rectangle(r.X + 10, r.Y + (r.Height - osz) / 2, osz, osz), 0, 0, osz, osz, GraphicsUnit.Pixel, attrs);
                        }
                        Draw.Text(g, row.Text, Font, Theme.TextMuted, new Rectangle(r.X + osz + 18, r.Y, r.Width - osz - 200, r.Height));
                        var link = LinkRect(r);
                        using (var lf = new Font(Font, i == _hoverLink ? FontStyle.Underline : FontStyle.Regular))
                            Draw.Text(g, L.T("Donanım ayarlarında aç ›", "Turn on in Hardware settings ›"), lf, Theme.Accent, link, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
                        break;
                }
            }
            y += h;
        }
    }

    private int LinkAt(Point p)
    {
        var y = 4 + AutoScrollPosition.Y;
        var w = Width - (VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0) - 8;
        for (var i = 0; i < _rows.Count; i++)
        {
            var h = RowHeight(_rows[i]);
            if (_rows[i].Kind == RowKind.Off && LinkRect(new Rectangle(4, y, w, h)).Contains(p)) return i;
            y += h;
        }
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var i = LinkAt(e.Location);
        Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
        if (i != _hoverLink) { _hoverLink = i; Invalidate(); }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (LinkAt(e.Location) >= 0) _openHardware();
    }

    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bold.Dispose();
            _small.Dispose();
        }
        base.Dispose(disposing);
    }
}

