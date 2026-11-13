using System.Drawing.Drawing2D;
using SysWatch.Monitoring;

namespace SysWatch.UI;

// Explorer-style tree of every sensor: computer › hardware › sensor type › sensor, with value, min and max columns.
// In pick mode a click on a sensor raises SensorPicked instead of selecting it.
internal sealed class SensorTree : ScrollableControl
{
    private enum Kind { Root, Hardware, Type, Sensor, OffHeader, Off }

    private sealed class Node
    {
        public Kind Kind;
        public string Key = "";
        public string Text = "";
        public string Category = "";
        public string Group = "";
        public SensorItem? Item;
        public readonly List<Node> Children = new();
    }

    // Rails[k]: the ancestor at level k + 1 has siblings below it, so its parent's guide line runs through this row.
    private sealed record Row(Node Node, int Level, bool[] Rails, bool Last, bool Expanded, int Top, int Height);

    private readonly List<Row> _rows = new();
    private readonly HashSet<string> _collapsed = new();
    private readonly Font _bold, _headerFont;
    private readonly Action? _openHardware;
    private List<SensorGroup> _groups = new();
    private List<(string Name, string Icon)> _off = new();
    private string _filter = "";
    private string _selectedKey = "";
    private int _hover = -1;
    private int _columnWidth;
    private int _contentHeight;

    public bool Pickable { get; init; }
    public event Action<SensorItem, string>? SensorPicked;

    private int HeaderHeight => (int)(Font.Height * 1.9);
    private int Indent => (int)(Font.Height * 1.35);
    private const int Pad = 6;

    public SensorTree(Font font, Action? openHardware = null)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Font = font;
        _bold = new Font(font, FontStyle.Bold);
        _headerFont = new Font(font.FontFamily, font.Size * 0.95f);
        _openHardware = openHardware;
        AutoScroll = true;
    }

    public string Filter
    {
        get => _filter;
        set
        {
            var next = value.Trim();
            if (next == _filter) return;
            _filter = next;
            AutoScrollPosition = Point.Empty;
            Rebuild();
        }
    }

    public void SetData(List<SensorGroup> groups, List<(string Name, string Icon)>? off = null)
    {
        _groups = groups;
        _off = off ?? new();
        Rebuild();
    }

    private bool Matches(SensorGroup grp, SensorItem item) =>
        _filter.Length == 0
        || item.Name.Contains(_filter, StringComparison.CurrentCultureIgnoreCase)
        || grp.Name.Contains(_filter, StringComparison.CurrentCultureIgnoreCase)
        || SensorGlyphs.GroupLabel(item.Type).Contains(_filter, StringComparison.CurrentCultureIgnoreCase);

    private static string TypeGroup(string type) => type == "SmallData" ? "Data" : type;

    private void Rebuild()
    {
        var root = new Node { Kind = Kind.Root, Key = "root", Text = Environment.MachineName };
        foreach (var grp in _groups)
        {
            var items = grp.Items.Where(i => Matches(grp, i)).ToList();
            if (items.Count == 0) continue;
            var hw = new Node { Kind = Kind.Hardware, Key = $"hw:{grp.Category}:{grp.Name}", Text = grp.Name, Category = grp.Category };
            foreach (var byType in items.GroupBy(i => TypeGroup(i.Type)).OrderBy(t => SensorGlyphs.TypeRank(t.Key)))
            {
                var type = new Node { Kind = Kind.Type, Key = $"{hw.Key}/{byType.Key}", Text = SensorGlyphs.GroupLabel(byType.Key), Category = byType.Key };
                foreach (var item in byType)
                    type.Children.Add(new Node { Kind = Kind.Sensor, Key = "s:" + item.Id, Text = item.Name, Item = item, Group = grp.Name });
                hw.Children.Add(type);
            }
            root.Children.Add(hw);
        }

        _rows.Clear();
        var y = HeaderHeight + 2;
        void Add(Node n, int level, bool[] rails, bool last)
        {
            var expanded = n.Children.Count > 0 && (_filter.Length > 0 || !_collapsed.Contains(n.Key));
            var h = RowHeight(n.Kind);
            _rows.Add(new Row(n, level, rails, last, expanded, y, h));
            y += h;
            if (!expanded) return;
            var childRails = level == 0 ? [] : rails.Append(!last).ToArray();
            for (var i = 0; i < n.Children.Count; i++) Add(n.Children[i], level + 1, childRails, i == n.Children.Count - 1);
        }
        if (root.Children.Count > 0) Add(root, 0, [], true);
        if (_off.Count > 0 && _filter.Length == 0)
        {
            Add(new Node { Kind = Kind.OffHeader, Text = L.T("Ayarlarda kapalı olan sensör grupları", "Sensor groups switched off in settings") }, 0, [], true);
            foreach (var (name, icon) in _off) Add(new Node { Kind = Kind.Off, Text = name, Category = icon }, 0, [], true);
        }
        _contentHeight = y + 8;

        var widest = new[] { L.T("Değer", "Value"), "Min", L.T("Maks", "Max") }.Max(s => TextRenderer.MeasureText(s, _headerFont).Width);
        foreach (var item in _rows.Select(r => r.Node.Item).OfType<SensorItem>())
            widest = Math.Max(widest, Math.Max(TextRenderer.MeasureText(item.Value, Font).Width, TextRenderer.MeasureText(item.Max, Font).Width));
        _columnWidth = Math.Max((int)(Font.Height * 5), widest + Font.Height);
        AutoScrollMinSize = new Size(0, _contentHeight);
        if (_hover >= _rows.Count) _hover = -1;
        Invalidate();
    }

    private int RowHeight(Kind kind) => kind switch
    {
        Kind.Root => (int)(Font.Height * 1.8),
        Kind.Hardware => (int)(Font.Height * 2.05),
        Kind.Type => (int)(Font.Height * 1.6),
        Kind.OffHeader => (int)(Font.Height * 2.6),
        Kind.Off => (int)(Font.Height * 1.9),
        _ => (int)(Font.Height * 1.45),
    };

    private int X(int level) => Pad + level * Indent;
    private int ContentRight => ClientSize.Width - Pad;
    private Rectangle Column(int index) => new(ContentRight - (3 - index) * _columnWidth, 0, _columnWidth, 0);

    private static string HardwareIcon(string category) => category switch
    {
        "GpuNvidia" or "GpuAmd" or "GpuIntel" or "Gpu" => Icons.Gpu,
        "Storage" => Icons.Disk,
        "Memory" => Icons.Memory,
        "Motherboard" or "SuperIO" or "EmbeddedController" or "Cooler" => Icons.Board,
        "Network" => Icons.Network,
        "Psu" or "Battery" => Icons.Power,
        _ => Icons.Hardware,
    };

    private string LinkText => L.T("Donanım ayarlarında aç ›", "Turn on in Hardware settings ›");

    private Rectangle LinkRect(Rectangle row)
    {
        var w = TextRenderer.MeasureText(LinkText, Font).Width;
        return new Rectangle(row.Right - w - 4, row.Y, w, row.Height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Draw.Setup(g);
        g.Clear(Draw.Backdrop(this));
        var scroll = AutoScrollPosition.Y;
        var header = HeaderHeight;

        if (_rows.Count == 0)
        {
            Draw.Text(g, _filter.Length > 0 ? L.T("Eşleşen sensör yok.", "No matching sensors.") : L.T("Sensörler okunuyor…", "Reading the sensors…"),
                Font, Theme.TextMuted, new Rectangle(0, header, Width, Height - header), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        g.SetClip(new Rectangle(0, header, Width, Height - header));
        var zebra = false;
        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            if (row.Node.Kind != Kind.Sensor) zebra = false;
            var top = row.Top + scroll;
            if (top + row.Height < header || top > Height) { if (row.Node.Kind == Kind.Sensor) zebra = !zebra; continue; }
            PaintRow(g, row, new Rectangle(Pad, top, ContentRight - Pad, row.Height), i == _hover, row.Node.Kind == Kind.Sensor && zebra);
            if (row.Node.Kind == Kind.Sensor) zebra = !zebra;
        }
        g.ResetClip();
        PaintHeader(g, header);
    }

    private void PaintHeader(Graphics g, int h)
    {
        Glass.Gloss(g, new RectangleF(0.5f, 0.5f, Width - 1.5f, h - 1.5f), Theme.ButtonGloss, 3, Theme.Border);
        using var sep = new Pen(Color.FromArgb(Theme.Dark ? 70 : 110, Theme.Border));
        Draw.Text(g, L.T("Sensör", "Sensor"), _headerFont, Theme.TextSecondary, new Rectangle(Pad + 6, 0, Column(0).X - Pad - 10, h));
        var labels = new[] { L.T("Değer", "Value"), "Min", L.T("Maks", "Max") };
        for (var c = 0; c < 3; c++)
        {
            var col = Column(c);
            g.DrawLine(sep, col.X, 5, col.X, h - 5);
            Draw.Text(g, labels[c], _headerFont, Theme.TextSecondary, new Rectangle(col.X, 0, col.Width - 8, h), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }
    }

    private void PaintRow(Graphics g, Row row, Rectangle r, bool hover, bool zebra)
    {
        var n = row.Node;
        var selected = n.Key.Length > 0 && n.Key == _selectedKey;
        if (n.Kind is Kind.OffHeader or Kind.Off)
        {
            PaintOff(g, row, r, hover);
            return;
        }

        if (n.Kind == Kind.Hardware)
        {
            var band = new RectangleF(X(row.Level) + 1, r.Y + 3, r.Right - X(row.Level) - 1, r.Height - 5);
            Glass.Gloss(g, band, hover || selected ? Theme.ButtonHoverGloss : Theme.CardGloss, 4, hover || selected ? Theme.SelectionBorder : Theme.Border);
        }
        else if (selected || hover)
        {
            var hl = new Rectangle(X(row.Level) - 2, r.Y + 1, r.Right - X(row.Level) + 2, r.Height - 2);
            using var path = Draw.Round(hl, 3);
            using var b = new LinearGradientBrush(new Rectangle(hl.X, hl.Y - 1, hl.Width, hl.Height + 2),
                Color.FromArgb(selected ? 255 : 150, Theme.SelectionGloss[0]), Color.FromArgb(selected ? 255 : 150, Theme.SelectionGloss[1]), 90f);
            g.FillPath(b, path);
            using var pen = new Pen(Color.FromArgb(selected ? 255 : 150, Theme.SelectionBorder));
            g.DrawPath(pen, path);
        }
        else if (zebra)
        {
            using var z = new SolidBrush(Color.FromArgb(Theme.Dark ? 14 : 90, Theme.SelectionGloss[0]));
            g.FillRectangle(z, X(row.Level), r.Y, r.Right - X(row.Level), r.Height);
        }

        PaintRails(g, row, r);

        var x = X(row.Level);
        var mid = r.Y + r.Height / 2;
        if (n.Children.Count > 0) PaintExpander(g, new Rectangle(x, r.Y, Indent, r.Height), row.Expanded, hover);

        switch (n.Kind)
        {
            case Kind.Root:
            {
                var isz = (int)(Font.Height * 1.25);
                g.DrawImage(Icons.Get(Icons.Hardware, isz), x + Indent, mid - isz / 2);
                Draw.Text(g, n.Text, _bold, Theme.Text, new Rectangle(x + Indent + isz + 6, r.Y, r.Right - x - Indent - isz - 6, r.Height));
                break;
            }
            case Kind.Hardware:
            {
                var isz = (int)(Font.Height * 1.2);
                var icon = new Rectangle(x + Indent, mid - isz / 2, isz, isz);
                if (n.Category == "Cpu") SensorGlyphs.Chip(g, icon);
                else g.DrawImage(Icons.Get(HardwareIcon(n.Category), isz), icon.Location);
                Draw.Text(g, n.Text, _bold, Theme.Text, new Rectangle(icon.Right + 6, r.Y, r.Right - icon.Right - 12, r.Height));
                break;
            }
            case Kind.Type:
            {
                var isz = (int)(Font.Height * 0.95);
                SensorGlyphs.TypeBadge(g, n.Category, new RectangleF(x + Indent, mid - isz / 2f, isz, isz));
                Draw.Text(g, n.Text, Font, Theme.TextSecondary, new Rectangle(x + Indent + isz + 6, r.Y, Column(0).X - x - Indent - isz - 10, r.Height));
                break;
            }
            case Kind.Sensor:
            {
                var item = n.Item!;
                var textX = x + Indent / 2;
                Draw.Text(g, n.Text, Font, Theme.Text, new Rectangle(textX, r.Y, Column(0).X - textX - 4, r.Height));
                var flags = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
                Draw.Text(g, item.Value, Font, Theme.Text, Cell(0, r), flags);
                Draw.Text(g, item.Min, Font, Theme.TextSecondary, Cell(1, r), flags);
                Draw.Text(g, item.Max, Font, Theme.TextSecondary, Cell(2, r), flags);
                break;
            }
        }
    }

    private Rectangle Cell(int column, Rectangle row)
    {
        var c = Column(column);
        return new Rectangle(c.X, row.Y, c.Width - 8, row.Height);
    }

    private void PaintRails(Graphics g, Row row, Rectangle r)
    {
        if (row.Level == 0) return;
        using var pen = new Pen(Color.FromArgb(Theme.Dark ? 90 : 150, Theme.Border)) { DashStyle = DashStyle.Dot };
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.None;
        for (var k = 0; k < row.Rails.Length; k++)
        {
            if (!row.Rails[k]) continue;
            var rx = X(k) + Indent / 2;
            g.DrawLine(pen, rx, r.Y, rx, r.Bottom);
        }
        var cx = X(row.Level - 1) + Indent / 2;
        var mid = r.Y + r.Height / 2;
        g.DrawLine(pen, cx, r.Y, cx, row.Last ? mid : r.Bottom);
        var end = row.Node.Children.Count > 0 ? X(row.Level) + Indent / 2 - (int)(Font.Height * 0.3) : X(row.Level) + Indent / 2 - 3;
        g.DrawLine(pen, cx, mid, end, mid);
        g.Restore(state);
    }

    private void PaintExpander(Graphics g, Rectangle box, bool expanded, bool hover)
    {
        var s = Font.Height * 0.36f;
        var cx = box.X + box.Width / 2f;
        var cy = box.Y + box.Height / 2f;
        PointF[] tri = expanded
            ? [new(cx + s * 0.55f, cy - s * 0.55f), new(cx + s * 0.55f, cy + s * 0.55f), new(cx - s * 0.55f, cy + s * 0.55f)]
            : [new(cx - s * 0.4f, cy - s * 0.75f), new(cx + s * 0.55f, cy), new(cx - s * 0.4f, cy + s * 0.75f)];
        var color = hover ? Theme.Accent : expanded ? Theme.TextSecondary : Theme.TextMuted;
        if (expanded)
        {
            using var b = new SolidBrush(color);
            g.FillPolygon(b, tri);
        }
        else
        {
            using var p = new Pen(color, 1.2f) { LineJoin = LineJoin.Round };
            g.DrawPolygon(p, tri);
        }
    }

    private void PaintOff(Graphics g, Row row, Rectangle r, bool hover)
    {
        if (row.Node.Kind == Kind.OffHeader)
        {
            Draw.Text(g, row.Node.Text, _bold, Theme.TextSecondary, new Rectangle(r.X + 4, r.Y + r.Height / 3, r.Width, r.Height * 2 / 3));
            return;
        }
        var osz = Math.Min(20, r.Height - 8);
        using (var attrs = new System.Drawing.Imaging.ImageAttributes())
        {
            attrs.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.45f });
            g.DrawImage(Icons.Get(row.Node.Category, osz), new Rectangle(r.X + 10, r.Y + (r.Height - osz) / 2, osz, osz), 0, 0, osz, osz, GraphicsUnit.Pixel, attrs);
        }
        var link = LinkRect(r);
        Draw.Text(g, row.Node.Text, Font, Theme.TextMuted, new Rectangle(r.X + osz + 18, r.Y, link.X - r.X - osz - 24, r.Height));
        using var lf = new Font(Font, hover ? FontStyle.Underline : FontStyle.Regular);
        Draw.Text(g, LinkText, lf, Theme.Accent, link, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
    }

    private int RowAt(Point p)
    {
        if (p.Y < HeaderHeight) return -1;
        var y = p.Y - AutoScrollPosition.Y;
        for (var i = 0; i < _rows.Count; i++)
            if (y >= _rows[i].Top && y < _rows[i].Top + _rows[i].Height) return i;
        return -1;
    }

    private bool Interactive(int i)
    {
        if (i < 0) return false;
        var row = _rows[i];
        return row.Node.Kind switch
        {
            Kind.OffHeader => false,
            Kind.Off => LinkRect(new Rectangle(Pad, row.Top + AutoScrollPosition.Y, ContentRight - Pad, row.Height)).Contains(PointToClient(MousePosition)),
            _ => true,
        };
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var i = RowAt(e.Location);
        if (!Interactive(i)) i = -1;
        Cursor = i >= 0 && (_rows[i].Node.Children.Count > 0 || _rows[i].Node.Kind == Kind.Off || Pickable) ? Cursors.Hand : Cursors.Default;
        if (i != _hover) { _hover = i; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = -1;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        var i = RowAt(e.Location);
        if (!Interactive(i)) return;
        var n = _rows[i].Node;
        if (n.Kind == Kind.Off)
        {
            _openHardware?.Invoke();
            return;
        }
        if (n.Children.Count > 0)
        {
            if (_filter.Length > 0) return;
            if (!_collapsed.Remove(n.Key)) _collapsed.Add(n.Key);
            Rebuild();
            return;
        }
        _selectedKey = n.Key;
        Invalidate();
        if (Pickable && n.Item != null) SensorPicked?.Invoke(n.Item, n.Group);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        StyleScrollBar();
        Theme.Changed += OnThemeChanged;
    }

    private void OnThemeChanged()
    {
        if (IsHandleCreated && !IsDisposed) BeginInvoke(StyleScrollBar);
    }

    private void StyleScrollBar()
    {
        Theme.SetWindowTheme(Handle, Theme.Dark ? "DarkMode_Explorer" : "Explorer", null);
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
            Theme.Changed -= OnThemeChanged;
            _bold.Dispose();
            _headerFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
