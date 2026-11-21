using System.Globalization;
using SysWatch.Config;
using SysWatch.Monitoring;

namespace SysWatch.UI;

// "Durum" page: the chosen cards (built-in or any sensor), a mute button and the last-reading note. "Customize" edits
// the cards in place.
internal sealed class DashboardView : Panel
{
    private readonly IUiHost _app;
    private readonly Panel _grid = new() { Dock = DockStyle.Fill };
    private readonly Panel _bottom;
    private readonly List<(string Key, MetricCard Card)> _cards = new();
    private readonly AddChartTile _add;
    private readonly FlatButton _customize;
    private readonly Font _font;
    private readonly Label _note = new() { AutoSize = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Tag = "muted" };
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 1000 };
    // Name, hardware and type of each charted sensor, so a card is labelled even before its first reading.
    private readonly Dictionary<string, SensorSample> _meta = new();
    private string _layoutKey = "\0";
    private (Snapshot?, AppConfig, string) _filled;
    private bool _editing;

    public DashboardView(IUiHost app, Font font, Action openDetails)
    {
        _app = app;
        _font = font;
        Dock = DockStyle.Fill;
        var unit = font.Height;
        _add = new AddChartTile(font);
        _add.Click += (_, _) => AddChart();
        _grid.Layout += (_, _) => LayoutCards();
        _grid.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) ShowMenu(-1, _grid, e.Location); };

        _bottom = new Panel { Dock = DockStyle.Bottom, Height = (int)(unit * 2.8) };
        var iconSize = Icons.SizeFor(font) - 4;
        var mute = new FlatButton(L.T("Bildirimleri sustur", "Mute notifications")) { Image = Icons.Get(Icons.Mute, iconSize) };
        var details = new FlatButton(L.T("Detaylar", "Details")) { Image = Icons.Get(Icons.Details, iconSize) };
        _customize = new FlatButton(L.T("Özelleştir", "Customize")) { Image = Icons.Get(Icons.Appearance, iconSize) };
        details.Click += (_, _) => openDetails();
        _customize.Click += (_, _) => SetEditing(!_editing);
        _bottom.Controls.Add(_note);
        _bottom.Controls.Add(mute);
        _bottom.Controls.Add(details);
        _bottom.Controls.Add(_customize);
        _bottom.Layout += (_, _) =>
        {
            mute.Location = new Point(_bottom.Width - mute.Width - 5, (_bottom.Height - mute.Height) / 2);
            details.Location = new Point(mute.Left - details.Width - 6, mute.Top);
            _customize.Location = new Point(details.Left - _customize.Width - 6, mute.Top);
            _note.Bounds = new Rectangle(6, 0, Math.Max(0, _customize.Left - 14), _bottom.Height);
        };
        mute.Click += (_, _) => ShowMuteMenu(mute);

        Controls.Add(_grid);
        Controls.Add(_bottom);
        _refresh.Tick += (_, _) => { if (!this.Minimized()) RefreshValues(); };
        VisibleChanged += (_, _) =>
        {
            if (Visible) { RefreshValues(); _refresh.Start(); }
            else { _refresh.Stop(); SetEditing(false); }
        };
    }

    private List<string> Charts => _app.Config.Charts;

    private void SetEditing(bool on)
    {
        if (_editing == on) return;
        _editing = on;
        _customize.Text = on ? L.T("Bitti", "Done") : L.T("Özelleştir", "Customize");
        _customize.Primary = on;
        _customize.Invalidate();
        _bottom.PerformLayout();
        Rebuild();
    }

    private void Rebuild()
    {
        var keys = Charts.ToList();
        _grid.SuspendLayout();
        foreach (var (_, card) in _cards)
        {
            _grid.Controls.Remove(card);
            card.Dispose();
        }
        _cards.Clear();
        _grid.Controls.Remove(_add);

        var missing = keys.Select(SensorId).OfType<string>().Where(id => !_meta.ContainsKey(id)).ToList();
        if (missing.Count > 0)
            foreach (var grp in _app.Sensors())
                foreach (var item in grp.Items.Where(i => missing.Contains(i.Id)))
                    _meta[item.Id] = new SensorSample(item.Name, grp.Name, item.Type, double.NaN);

        for (var i = 0; i < keys.Count; i++)
        {
            var index = i;
            var card = new MetricCard(_font) { Editing = _editing };
            card.ActionClicked += a => OnCardAction(index, a);
            card.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) ShowMenu(index, card, e.Location); };
            _cards.Add((keys[i], card));
            _grid.Controls.Add(card);
        }
        if (ShowAddTile) _grid.Controls.Add(_add);
        _layoutKey = string.Join("\n", keys);
        _grid.ResumeLayout();
        LayoutCards();
        Fill();
    }

    private bool ShowAddTile => _cards.Count < AppConfig.MaxCharts && (_editing || _cards.Count == 0);

    private static string? SensorId(string key) =>
        key.StartsWith(AppConfig.SensorChartPrefix, StringComparison.Ordinal) ? key[AppConfig.SensorChartPrefix.Length..] : null;

    private void LayoutCards()
    {
        var cells = _cards.Select(c => (Control)c.Card).ToList();
        if (ShowAddTile) cells.Add(_add);
        if (cells.Count == 0) return;
        var cols = cells.Count <= 1 ? 1 : cells.Count <= 4 ? 2 : 3;
        var rows = (cells.Count + cols - 1) / cols;
        var w = _grid.ClientSize.Width / (float)cols;
        var h = _grid.ClientSize.Height / (float)rows;
        for (var i = 0; i < cells.Count; i++)
        {
            var x = (int)(i % cols * w);
            var y = (int)(i / cols * h);
            cells[i].Bounds = new Rectangle(x + 5, y + 5, (int)((i % cols + 1) * w) - x - 10, (int)((i / cols + 1) * h) - y - 10);
        }
    }

    public void RefreshValues()
    {
        if (string.Join("\n", Charts) != _layoutKey) Rebuild();
        else if (FillState() != _filled) Fill();
    }

    // Readings arrive every few seconds but the timer ticks every second; nothing is redrawn until one of these changes.
    private (Snapshot?, AppConfig, string) FillState() => (_app.LastSnapshot, _app.Config, string.Join(",", _app.ActiveAlerts));

    private void Fill()
    {
        _filled = FillState();
        var (snap, cfg, _) = _filled;
        var active = _app.ActiveAlerts;
        var hist = _app.History;

        void FillBuiltIn(MetricCard card, string label, Metric main, Metric? secondary)
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
        }

        void FillSensor(MetricCard card, string id)
        {
            var sample = snap?.Watched.GetValueOrDefault(id);
            if (sample != null) _meta[id] = sample;
            var meta = _meta.GetValueOrDefault(id);
            var type = meta?.Type ?? "";
            card.Badge = type;
            card.Label = meta == null ? L.T("Sensör bulunamadı", "Sensor not found") : $"{meta.Name} · {meta.Hardware}";
            card.Value = sample == null ? "—" : SensorFormat.Format(type, sample.Value);
            card.Sub = "";
            card.Unit = "";
            card.RangeFormat = v => SensorFormat.Format(type, v);
            card.MinSpan = MinSpan(type);
            card.Series = hist.Series(id);
            card.Alarm = false;
            card.Limit = null;
        }

        for (var i = 0; i < _cards.Count; i++)
        {
            var (key, card) = _cards[i];
            switch (key)
            {
                case "cpu": FillBuiltIn(card, "CPU", Metric.CpuTemp, Metric.CpuLoad); break;
                case "gpu": FillBuiltIn(card, "GPU", Metric.GpuTemp, Metric.GpuLoad); break;
                case "ram": FillBuiltIn(card, "RAM", Metric.Ram, null); break;
                case "disk": FillBuiltIn(card, "Disk", Metric.DiskTemp, null); break;
                default:
                    if (SensorId(key) is { } id) FillSensor(card, id);
                    break;
            }
            card.CanMoveLeft = i > 0;
            card.CanMoveRight = i < _cards.Count - 1;
            card.Invalidate();
        }

        _note.Text = _editing
            ? L.T("Kartları taşıyın, değiştirin ya da kaldırın.", "Move, change or remove the cards.")
            : snap?.Get(Metric.CpuTemp) == null && cfg.Hardware.Cpu && Charts.Contains("cpu")
                ? L.T("CPU sıcaklığı için PawnIO sürücüsü gerekli", "CPU temperature needs the PawnIO driver")
                : snap == null ? L.T("İlk ölçüm bekleniyor…", "Waiting for the first reading…") : L.T($"Son ölçüm {snap.Time:HH:mm:ss}", $"Last reading {snap.Time:HH:mm:ss}");
    }

    // The smallest range a chart stretches to, in the sensor's own unit (throughput is in bytes per second).
    private static double MinSpan(string type) => type switch
    {
        "Temperature" or "Load" or "Control" or "Level" => 6,
        "Clock" => 200,
        "Power" => 2,
        "Voltage" => 0.05,
        "Current" => 0.5,
        "Fan" => 300,
        "Data" => 0.5,
        "SmallData" => 64,
        "Throughput" => 100 * 1024,
        _ => 1,
    };

    private void SetCharts(List<string> charts)
    {
        _app.UpdateConfig(c => c.Charts = charts);
        RefreshValues();
    }

    private void OnCardAction(int index, CardAction action)
    {
        var list = Charts.ToList();
        if (index >= list.Count) return;
        switch (action)
        {
            case CardAction.MoveLeft when index > 0:
                (list[index - 1], list[index]) = (list[index], list[index - 1]);
                break;
            case CardAction.MoveRight when index < list.Count - 1:
                (list[index + 1], list[index]) = (list[index], list[index + 1]);
                break;
            case CardAction.Remove:
                list.RemoveAt(index);
                break;
            case CardAction.Change:
                if (Pick(list, list[index]) is not { } chosen) return;
                list[index] = chosen;
                break;
            default:
                return;
        }
        SetCharts(list);
    }

    private void AddChart()
    {
        var list = Charts.ToList();
        if (list.Count >= AppConfig.MaxCharts || Pick(list, null) is not { } chosen) return;
        list.Add(chosen);
        SetCharts(list);
    }

    private string? Pick(List<string> taken, string? current)
    {
        using var picker = new ChartPicker(_app, taken, current);
        if (picker.ShowDialog(FindForm()) != DialogResult.OK || picker.Chosen == null) return null;
        if (picker.ChosenSensor != null && SensorId(picker.Chosen) is { } id) _meta[id] = picker.ChosenSensor;
        return picker.Chosen;
    }

    private void ShowMenu(int index, Control anchor, Point at)
    {
        var menu = new ContextMenuStrip();
        var count = Charts.Count;
        if (index >= 0)
        {
            menu.Items.Add(L.T("Değiştir…", "Change…"), null, (_, _) => OnCardAction(index, CardAction.Change));
            menu.Items.Add(L.T("Sola taşı", "Move left"), null, (_, _) => OnCardAction(index, CardAction.MoveLeft)).Enabled = index > 0;
            menu.Items.Add(L.T("Sağa taşı", "Move right"), null, (_, _) => OnCardAction(index, CardAction.MoveRight)).Enabled = index < count - 1;
            menu.Items.Add(L.T("Kaldır", "Remove"), null, (_, _) => OnCardAction(index, CardAction.Remove));
            menu.Items.Add(new ToolStripSeparator());
        }
        menu.Items.Add(L.T("Grafik ekle…", "Add chart…"), null, (_, _) => AddChart()).Enabled = count < AppConfig.MaxCharts;
        menu.Items.Add(_editing ? L.T("Düzenlemeyi bitir", "Finish editing") : L.T("Kartları düzenle", "Edit cards"), null, (_, _) => SetEditing(!_editing));
        menu.Items.Add(L.T("Varsayılan kartlara dön", "Back to the default cards"), null, (_, _) => SetCharts(AppConfig.DefaultCharts()))
            .Enabled = !Charts.SequenceEqual(AppConfig.DefaultCharts());
        Theme.StyleMenu(menu);
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(anchor, at);
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

// "Detaylar": every sensor of every enabled group as a tree, refreshed while visible, plus the groups switched off in
// the settings with a link to the hardware page instead of a switch here.
internal sealed class DetailsView : Panel
{
    private readonly IUiHost _app;
    private readonly SensorTree _tree;
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 2000 };
    private (Snapshot?, AppConfig) _loaded;

    public DetailsView(IUiHost app, Font font, Action back, Action openHardware)
    {
        _app = app;
        Dock = DockStyle.Fill;
        _tree = new SensorTree(font, openHardware) { Dock = DockStyle.Fill };
        var header = new Panel { Dock = DockStyle.Top, Height = (int)(font.Height * 2.9) };
        var backLink = new LinkLabel { Text = L.T("‹ Durum", "‹ Status"), AutoSize = true };
        backLink.LinkClicked += (_, _) => back();
        var title = new Label { Text = L.T("Tüm okumalar", "All readings"), AutoSize = true, Font = new Font(font.FontFamily, font.Size * 1.35f) };
        var search = SearchBox(font);
        search.TextChanged += (_, _) => _tree.Filter = search.Text;
        header.Controls.Add(backLink);
        header.Controls.Add(title);
        header.Controls.Add(search);
        header.Layout += (_, _) =>
        {
            var cy = (header.Height - 6) / 2;
            backLink.Location = new Point(0, cy - backLink.Height / 2);
            title.Location = new Point(backLink.Right + 12, cy - title.Height / 2);
            search.Width = Math.Min((int)(font.Height * 14), Math.Max(font.Height * 6, header.Width - title.Right - 16));
            search.Location = new Point(header.Width - search.Width - 2, cy - search.Height / 2);
        };
        Controls.Add(_tree);
        Controls.Add(header);
        _refresh.Tick += (_, _) => { if (!this.Minimized() && (_app.LastSnapshot, _app.Config) != _loaded) Reload(); };
        VisibleChanged += (_, _) =>
        {
            if (Visible) { Reload(); _refresh.Start(); }
            else _refresh.Stop();
        };
    }

    public static TextBox SearchBox(Font font)
    {
        var box = new TextBox { Font = font, BorderStyle = BorderStyle.FixedSingle };
        box.HandleCreated += (_, _) => SendMessage(box.Handle, 0x1501, 1, L.T("Sensör ara…", "Search sensors…")); // EM_SETCUEBANNER
        return box;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, string lParam);

    private void Reload()
    {
        _loaded = (_app.LastSnapshot, _app.Config);
        var hw = _loaded.Item2.Hardware;
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
        _tree.SetData(_app.Sensors(), off);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _refresh.Dispose();
        base.Dispose(disposing);
    }
}
