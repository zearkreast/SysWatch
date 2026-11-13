using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace SysWatch.UI;

internal static class Draw
{
    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        var d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void Setup(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    }

    public static void Dot(Graphics g, Color c, float x, float y, float size = 8)
    {
        using var b = new SolidBrush(c);
        g.FillEllipse(b, x, y, size, size);
    }

    public static void Text(Graphics g, string s, Font f, Color c, Rectangle r,
        TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix) =>
        TextRenderer.DrawText(g, s, f, r, c, flags);

    public static void Sparkline(Graphics g, double[] series, RectangleF r, Color color, double? limit = null)
    {
        var raw = series.Select((v, i) => (v, i)).Where(p => !double.IsNaN(p.v)).ToList();
        if (raw.Count < 2 || r.Width < 4 || r.Height < 4) return;
        var smooth = raw.Select((p, k) =>
        {
            var lo = Math.Max(0, k - 1);
            var hi = Math.Min(raw.Count - 1, k + 1);
            var sum = 0.0;
            for (var j = lo; j <= hi; j++) sum += raw[j].v;
            return (v: sum / (hi - lo + 1), p.i);
        }).ToList();
        var max = Math.Max(smooth.Max(p => p.v), limit ?? double.MinValue);
        var min = Math.Min(smooth.Min(p => p.v), limit ?? double.MaxValue);
        var span = Math.Max(max - min, 6);
        max = (max + min) / 2 + span * 0.62;
        min = max - span * 1.24;
        float X(int i) => r.X + r.Width * i / Math.Max(1, series.Length - 1);
        float Y(double v) => r.Bottom - (float)((v - min) / (max - min)) * r.Height;

        using (var grid = new Pen(Color.FromArgb(Theme.Dark ? 40 : 55, Theme.Border)) { DashStyle = DashStyle.Dot })
            for (var q = 1; q <= 3; q++) g.DrawLine(grid, r.X, r.Y + r.Height * q / 4f, r.Right, r.Y + r.Height * q / 4f);

        var pts = smooth.Select(p => new PointF(X(p.i), Y(p.v))).ToArray();
        using (var area = new GraphicsPath())
        {
            area.AddLine(pts[0].X, r.Bottom, pts[0].X, pts[0].Y);
            area.AddCurve(pts, 0.35f);
            area.AddLine(pts[^1].X, pts[^1].Y, pts[^1].X, r.Bottom);
            area.CloseFigure();
            using var fill = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb(Theme.Dark ? 110 : 95, color), Color.FromArgb(0, color), 90f);
            g.FillPath(fill, area);
        }
        if (limit is { } l && Y(l) >= r.Y && Y(l) <= r.Bottom)
        {
            using var dash = new Pen(Color.FromArgb(150, color), 1.2f) { DashStyle = DashStyle.Dash };
            g.DrawLine(dash, r.X, Y(l), r.Right, Y(l));
        }
        using (var glow = new Pen(Color.FromArgb(50, color), 5f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawCurve(glow, pts, 0.35f);
        using (var pen = new Pen(color, 1.8f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawCurve(pen, pts, 0.35f);
        var last = pts[^1];
        using (var halo = new SolidBrush(Color.FromArgb(60, color))) g.FillEllipse(halo, last.X - 6, last.Y - 6, 12, 12);
        using (var ring = new SolidBrush(Color.White)) g.FillEllipse(ring, last.X - 3.5f, last.Y - 3.5f, 7, 7);
        using (var dot = new SolidBrush(color)) g.FillEllipse(dot, last.X - 2.2f, last.Y - 2.2f, 4.4f, 4.4f);
    }

    public static Font Icons(float size) => new(Theme.IconFont, size, GraphicsUnit.Point);

    // First opaque background up the parent chain; "Transparent" would clear the buffer to black.
    public static Color Backdrop(Control c)
    {
        for (var p = c.Parent; p != null; p = p.Parent)
            if (p.BackColor.A == 255) return p.BackColor;
        return Theme.Window;
    }
}

internal sealed class MetricCard : Control
{
    public string Label { get; set; } = "";
    public string Value { get; set; } = "—";
    public string Sub { get; set; } = "";
    public double[] Series { get; set; } = [];
    public double? Limit { get; set; }
    public bool Alarm { get; set; }
    public string Unit { get; set; } = "";

    private readonly Font _value;
    private readonly Font _small;

    public MetricCard(Font baseFont)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        _value = new Font(baseFont.FontFamily, baseFont.Size * 1.9f, FontStyle.Regular);
        _small = new Font(baseFont.FontFamily, baseFont.Size * 0.92f);
        Font = baseFont;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Draw.Setup(g);
        g.Clear(Draw.Backdrop(this));
        var fg = Alarm ? Theme.Danger : Theme.Text;
        var muted = Alarm ? Theme.Danger : Theme.TextSecondary;
        Glass.Gloss(g, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Alarm ? Theme.AlarmGloss : Theme.CardGloss, 5,
            Alarm ? Theme.AlarmBorder : Theme.Border);

        var pad = (int)(Font.Height * 0.7);
        Draw.Text(g, Label, _small, muted, new Rectangle(pad, pad - 2, Width - pad * 2, _small.Height));
        var valueTop = pad + _small.Height;
        const TextFormatFlags plain = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        var valueSize = TextRenderer.MeasureText(g, Value, _value, Size.Empty, plain);
        TextRenderer.DrawText(g, Value, _value, new Point(pad - 1, valueTop), fg, plain);
        if (Sub.Length > 0)
        {
            var subSize = TextRenderer.MeasureText(g, Sub, Font, Size.Empty, plain);
            TextRenderer.DrawText(g, Sub, Font, new Point(pad + valueSize.Width + 6, valueTop + valueSize.Height - subSize.Height - 3), muted, plain);
        }

        var chart = new RectangleF(pad, valueTop + _value.Height + 4, Width - pad * 2, Height - valueTop - _value.Height - pad - 4);
        if (chart.Height > 6)
        {
            Draw.Sparkline(g, Series, new RectangleF(chart.X, chart.Y + 2, chart.Width - 8, chart.Height - 2), Alarm ? Theme.DangerFill : Theme.Chart, Limit);
            var valid = Series.Where(v => !double.IsNaN(v)).ToArray();
            if (valid.Length > 1 && Unit.Length > 0 && Width > Font.Height * 15)
            {
                var range = L.T($"en düşük {valid.Min():0}{Unit} · en yüksek {valid.Max():0}{Unit}", $"low {valid.Min():0}{Unit} · high {valid.Max():0}{Unit}");
                TextRenderer.DrawText(g, range, _small, new Rectangle(pad, Height - pad - _small.Height + 4, Width - pad * 2, _small.Height), muted,
                    TextFormatFlags.Right | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _value.Dispose();
            _small.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class NavList : Control
{
    private sealed record Row(string Glyph, string Text, bool Header, int Page);

    private readonly List<Row> _rows = new();
    private readonly Font _icons;
    private readonly Font _header;
    private int _hover = -1;
    private int _selected;
    private float _highlightY = -1;

    public event Action<int>? SelectedChanged;
    public event Action? CollapseToggled;

    public static int CollapsedWidth(Font f) => (int)(f.Height * 3.6);
    private bool Collapsed => Width < Font.Height * 6;
    private int ToggleHeight => (int)(Font.Height * 2.3);
    private int _mouseX;

    // The menu button: drawn and hit-tested as the same rectangle.
    private Rectangle ToggleRect
    {
        get
        {
            var h = ToggleHeight - 8;
            var w = (int)(h * 1.5);
            return new Rectangle(Collapsed ? (Width - w) / 2 : 10, 6, w, h);
        }
    }
    private readonly ToolTip _tip = new();
    private int _tipRow = -3;

    public int Selected
    {
        get => _selected;
        set
        {
            if (value == _selected) return;
            var from = _highlightY < 0 ? RowTop(RowOf(value)) : _highlightY;
            _selected = value;
            var to = RowTop(RowOf(value));
            Motion.Run(this, 180, t => { _highlightY = from + (to - from) * t; Invalidate(); });
            SelectedChanged?.Invoke(value);
        }
    }

    private int ItemHeight => (int)(Font.Height * 2.3);
    private int HeaderHeight => (int)(Font.Height * 2.0);

    public NavList(Font font)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Font = font;
        _icons = Draw.Icons(font.SizeInPoints * 1.05f);
        _header = new Font(font.FontFamily, font.Size * 0.9f);
        Cursor = Cursors.Hand;
    }

    // Returns the page index the item selects.
    public int Add(string glyph, string text)
    {
        var page = _rows.Count(r => !r.Header);
        _rows.Add(new Row(glyph, text, false, page));
        return page;
    }

    public void AddHeader(string text) => _rows.Add(new Row("", text, true, -1));

    private int RowOf(int page) => _rows.FindIndex(r => r.Page == page);

    private int RowTop(int row)
    {
        var y = 6 + ToggleHeight;
        for (var i = 0; i < row; i++) y += _rows[i].Header ? HeaderHeight : ItemHeight;
        return y;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Draw.Setup(g);
        g.Clear(BackColor);
        var toggle = ToggleRect;
        using (var bars = new Pen(_hover == -2 ? Theme.Accent : Theme.TextSecondary, _hover == -2 ? 2.2f : 1.8f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
        {
            var cx = toggle.X + toggle.Width / 2f;
            var cy = toggle.Y + toggle.Height / 2f;
            for (var k = -1; k <= 1; k++) g.DrawLine(bars, cx - 7, cy + k * 5, cx + 7, cy + k * 5);
        }
        var selRow = RowOf(_selected);
        var hy = _highlightY < 0 ? RowTop(selRow) : _highlightY;
        if (selRow >= 0)
        {
            var r = new Rectangle(6, (int)hy, Width - 12, ItemHeight - 4);
            using var b = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(r.X, r.Y - 1, r.Width, r.Height + 2), Theme.SelectionGloss[0], Theme.SelectionGloss[1], 90f);
            using var p = Draw.Round(r, 3);
            g.FillPath(b, p);
            using var pen = new Pen(Theme.SelectionBorder);
            g.DrawPath(pen, p);
        }
        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            var top = RowTop(i);
            if (row.Header)
            {
                if (Collapsed)
                {
                    using var sep = new Pen(Theme.Border);
                    g.DrawLine(sep, 12, top + HeaderHeight / 2, Width - 12, top + HeaderHeight / 2);
                    continue;
                }
                var hr = new Rectangle(14, top, Width - 20, HeaderHeight);
                Draw.Text(g, row.Text, _header, Theme.TextMuted, new Rectangle(hr.X, hr.Y + HeaderHeight / 3, hr.Width, HeaderHeight * 2 / 3));
                using var line = new Pen(Theme.Border);
                var tw = TextRenderer.MeasureText(row.Text, _header).Width;
                g.DrawLine(line, hr.X + tw + 4, hr.Y + HeaderHeight * 2 / 3, Width - 10, hr.Y + HeaderHeight * 2 / 3);
                continue;
            }
            var r = new Rectangle(6, top, Width - 12, ItemHeight - 4);
            if (i == _hover && i != selRow)
            {
                using var hb = new SolidBrush(Color.FromArgb(90, Theme.SelectionGloss[1]));
                using var hp = Draw.Round(r, 3);
                g.FillPath(hb, hp);
            }
            var color = i == selRow ? Theme.Text : Theme.TextSecondary;
            var isz = Icons.SizeFor(Font);
            g.DrawImage(Icons.Get(row.Glyph, isz), Collapsed ? r.X + (r.Width - isz) / 2 : r.X + 10, r.Y + (r.Height - isz) / 2, isz, isz);
            if (!Collapsed) Draw.Text(g, row.Text, Font, color, new Rectangle(r.X + 42, r.Y, r.Width - 46, r.Height));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        _mouseX = e.X;
        var i = Hit(e.Y);
        Cursor = i == -1 ? Cursors.Default : Cursors.Hand;
        UpdateTip(i);
        if (i != _hover) { _hover = i; Invalidate(); }
    }

    // Labels are hidden when collapsed, so each icon gets a tooltip.
    private void UpdateTip(int row)
    {
        if (row == _tipRow) return;
        _tipRow = row;
        var text = row == -2 ? (Collapsed ? L.T("Menüyü genişlet", "Expand menu") : L.T("Menüyü daralt", "Collapse menu")) : Collapsed && row >= 0 ? _rows[row].Text : null;
        _tip.SetToolTip(this, text);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _tipRow = -3;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = -1;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        _mouseX = e.X;
        var i = Hit(e.Y);
        if (i == -2) CollapseToggled?.Invoke();
        else if (i >= 0) Selected = _rows[i].Page;
    }

    private int Hit(int y)
    {
        if (y >= ToggleRect.Top && y < ToggleRect.Bottom && _mouseX >= ToggleRect.Left && _mouseX < ToggleRect.Right) return -2;
        if (y < ToggleHeight + 6) return -1;
        for (var i = 0; i < _rows.Count; i++)
        {
            var top = RowTop(i);
            var h = _rows[i].Header ? HeaderHeight : ItemHeight;
            if (y >= top && y < top + h) return _rows[i].Header ? -1 : i;
        }
        return -1;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Motion.Stop(this);
            _icons.Dispose();
            _header.Dispose();
            _tip.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class StatusRow : Control
{
    public string Glyph { get; set; } = "";
    public string Title { get; set; } = "";
    public string Status { get; set; } = "";
    public Color DotColor { get; set; } = Theme.Neutral;
    public Color StatusColor { get; set; } = Theme.TextMuted;
    public bool Chevron { get; set; } = true;

    private readonly Font _icons;
    private bool _hover;

    public StatusRow(Font font)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Font = font;
        _icons = Draw.Icons(font.SizeInPoints * 1.05f);
        Height = (int)(font.Height * 2.6);
        Cursor = Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Draw.Setup(g);
        g.Clear(Draw.Backdrop(this));
        Glass.Gloss(g, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), _hover ? Theme.ButtonHoverGloss : Theme.CardGloss, 4,
            _hover ? Theme.SelectionBorder : Theme.Border);
        var isz = Math.Min(Icons.SizeFor(Font), Height - 8);
        g.DrawImage(Icons.Get(Glyph, isz), 12, (Height - isz) / 2, isz, isz);
        Draw.Text(g, Title, Font, Theme.Text, new Rectangle(42, 0, Width / 2 - 42, Height));

        var right = Width - 34; // same column with or without the chevron, so the dots line up
        Draw.Dot(g, DotColor, right - 9, Height / 2f - 4.5f, 9);
        var size = TextRenderer.MeasureText(g, Status, Font);
        Draw.Text(g, Status, Font, StatusColor, new Rectangle(right - 16 - size.Width, 0, size.Width, Height), TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPrefix);
        if (Chevron)
            Draw.Text(g, "\uE76C", _icons, Theme.TextMuted, new Rectangle(Width - 30, 0, 20, Height), TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _icons.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class FlatButton : Button
{
    public bool Primary { get; set; }
    private bool _down;
    private float _glow;

    public FlatButton(string text, bool primary = false)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        Text = text;
        Primary = primary;
        AutoSize = true;
        Padding = new Padding(12, 4, 12, 4);
        MinimumSize = new Size(0, 26);
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
    }

    // Room for the optional icon, and never shorter than a comfortable click target.
    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding);
        var w = text.Width + Padding.Horizontal + (Image != null ? Image.Width + (Text.Length > 0 ? 6 : 0) : 0) + 4;
        var h = Math.Max(Math.Max(text.Height, Image?.Height ?? 0) + Padding.Vertical + 6, (int)(Font.Height * 1.9));
        return new Size(w, h);
    }

    public void Restyle() => Invalidate();

    protected override void OnMouseEnter(EventArgs e) { Fade(1); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _down = false; Fade(0); base.OnMouseLeave(e); }

    private void Fade(float target)
    {
        var from = _glow;
        Motion.Run(this, 140, t => { if (!IsDisposed) { _glow = from + (target - from) * t; Invalidate(); } });
    }

    private static Color Mix(Color a, Color b, float t) =>
        Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    protected override void OnMouseDown(MouseEventArgs mevent) { _down = true; Invalidate(); base.OnMouseDown(mevent); }
    protected override void OnMouseUp(MouseEventArgs mevent) { _down = false; Invalidate(); base.OnMouseUp(mevent); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Draw.Setup(g);
        g.Clear(Draw.Backdrop(this));
        var stops = Primary
            ? Theme.PrimaryGloss.Select(c => Mix(c, Theme.Lighten(c, 0.15), _glow)).ToArray()
            : Theme.ButtonGloss.Zip(Theme.ButtonHoverGloss, (a, b) => Mix(a, b, _glow)).ToArray();
        if (_down) stops = stops.Select(c => Color.FromArgb(c.A, (int)(c.R * 0.88), (int)(c.G * 0.88), (int)(c.B * 0.88))).ToArray();
        Glass.Gloss(g, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), stops, 3.5f,
            Primary ? Color.FromArgb(20, 70, 140) : Mix(Theme.ButtonBorder, Theme.SelectionBorder, _glow));
        if (Focused && ShowFocusCues)
        {
            using var focus = new Pen(Color.FromArgb(120, Theme.Accent)) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
            g.DrawRectangle(focus, 3, 3, Width - 7, Height - 7);
        }
        var textColor = Primary ? Color.White : Theme.Text;
        if (Image == null)
        {
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, textColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            return;
        }
        var tw = Text.Length > 0 ? TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width : 0;
        var gapW = Text.Length > 0 ? 6 : 0;
        var x = (Width - Image.Width - gapW - tw) / 2;
        g.DrawImage(Image, x, (Height - Image.Height) / 2, Image.Width, Image.Height);
        if (tw > 0)
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x + Image.Width + gapW, 0, tw + 4, Height), textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
    }
}

internal sealed class AeroComboBox : ComboBox
{
    private bool _hover;

    public AeroComboBox()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        DrawMode = DrawMode.OwnerDrawFixed;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        ItemHeight = Math.Max(ItemHeight, (int)(Font.Height * 1.5));
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnSelectedIndexChanged(EventArgs e) { Invalidate(); base.OnSelectedIndexChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Draw.Setup(g);
        g.Clear(Draw.Backdrop(this));
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Glass.Gloss(g, r, _hover || DroppedDown ? Theme.ButtonHoverGloss : Theme.ButtonGloss, 3.5f, _hover ? Theme.SelectionBorder : Theme.ButtonBorder);
        var arrowW = Height;
        TextRenderer.DrawText(g, Text, Font, new Rectangle(8, 0, Width - arrowW - 8, Height), Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        using var sep = new Pen(Color.FromArgb(90, Theme.ButtonBorder));
        g.DrawLine(sep, Width - arrowW, 5, Width - arrowW, Height - 6);
        var cx = Width - arrowW / 2f;
        var cy = Height / 2f;
        using var pen = new Pen(Theme.TextSecondary, 1.6f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
        g.DrawLines(pen, new PointF[] { new(cx - 4, cy - 2), new(cx, cy + 2), new(cx + 4, cy - 2) });
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var g = e.Graphics;
        var selected = (e.State & DrawItemState.Selected) != 0;
        using (var bg = new SolidBrush(Theme.Surface)) g.FillRectangle(bg, e.Bounds);
        if (selected)
        {
            var r = new Rectangle(e.Bounds.X + 2, e.Bounds.Y + 1, e.Bounds.Width - 5, e.Bounds.Height - 3);
            using var b = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(r.X, r.Y - 1, r.Width, r.Height + 2), Theme.SelectionGloss[0], Theme.SelectionGloss[1], 90f);
            g.FillRectangle(b, r);
            using var pen = new Pen(Theme.SelectionBorder);
            g.DrawRectangle(pen, r);
        }
        TextRenderer.DrawText(g, GetItemText(Items[e.Index]), Font, new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height),
            Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}
