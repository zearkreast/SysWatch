using System.Drawing.Drawing2D;

namespace SysWatch.UI;

// The SysWatch mark: a gear and a bell sharing one centre, split along the "/" diagonal with a gap
// (gear top-left, bell bottom-right). Built from measured proportions in a 1024 design space.
public static class Logo
{
    private const float S = 1024f;
    private const float Cx = 512f, Cy = 512f;
    private const float Gap = 30f;

    private static readonly Color[] GearStops = [Color.FromArgb(222, 244, 255), Color.FromArgb(118, 198, 242), Color.FromArgb(26, 112, 192)];
    private static readonly Color GearEdge = Color.FromArgb(210, 11, 60, 116);
    private static readonly Color[] BellStops = [Color.FromArgb(255, 238, 170), Color.FromArgb(255, 178, 36), Color.FromArgb(190, 92, 0)];
    private static readonly Color BellEdge = Color.FromArgb(190, 104, 48, 0);

    // Small sizes (taskbar, tray, title bar) get flatter, more saturated fills and an opaque outline:
    // soft gradients and thin highlights only turn into blur at 16-32 px.
    private static readonly Color[] GearStopsSmall = [Color.FromArgb(150, 214, 250), Color.FromArgb(60, 156, 228), Color.FromArgb(22, 100, 180)];
    private static readonly Color GearEdgeSmall = Color.FromArgb(8, 46, 96);
    private static readonly Color[] BellStopsSmall = [Color.FromArgb(255, 214, 90), Color.FromArgb(250, 164, 20), Color.FromArgb(200, 104, 0)];
    private static readonly Color BellEdgeSmall = Color.FromArgb(104, 46, 0);

    public static Bitmap Render(int size)
    {
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        Draw(g, new RectangleF(0, 0, size, size));
        return bmp;
    }

    public static Icon RenderIcon(int size)
    {
        using var bmp = Render(size);
        var h = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(h).Clone();
        DestroyIcon(h);
        return icon;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    public static void Draw(Graphics g, RectangleF bounds)
    {
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        var px = bounds.Width;
        var small = px <= 32;
        var inset = small ? 0.01f : 0.02f;
        var scale = px * (1 - inset * 2) / S;
        g.TranslateTransform(bounds.X + px * inset, bounds.Y + px * inset);
        g.ScaleTransform(scale, scale);
        // Outline at least ~1 device pixel, the diagonal gap at least ~1.5 px so it survives downsizing.
        var edge = small ? 1.05f / scale : Math.Max(10f, 1.2f / scale);
        var gap = Math.Max(Gap, 0.8f / scale);

        using var gear = GearPath();
        using var bell = BellPath();
        using var body = BodyPath();
        using var upper = Half(upperLeft: true, gap);
        using var lower = Half(upperLeft: false, gap);

        g.SetClip(upper, CombineMode.Replace);
        using (var brush = Gradient(gear.GetBounds(), small ? GearStopsSmall : GearStops, 0.7f, 0.55f)) g.FillPath(brush, gear);
        if (!small) Highlight(g, gear, new RectangleF(90, 40, 660, 380), 0.6f);
        using (var pen = new Pen(small ? GearEdgeSmall : GearEdge, edge) { LineJoin = LineJoin.Round }) g.DrawPath(pen, gear);

        g.SetClip(lower, CombineMode.Replace);
        using (var brush = Gradient(bell.GetBounds(), small ? BellStopsSmall : BellStops, 0.3f, 0.5f)) g.FillPath(brush, bell);
        if (small)
        {
            using var pen = new Pen(BellEdgeSmall, edge) { LineJoin = LineJoin.Round };
            g.DrawPath(pen, bell);
            g.Restore(state);
            return;
        }
        using (var hl = new GraphicsPath())
        {
            hl.AddLine(330, 640, 338, 450);
            hl.AddBezier(338, 450, 340, 350, 410, 290, 500, 280);
            hl.AddBezier(500, 280, 430, 320, 400, 380, 398, 460);
            hl.AddLine(398, 460, 392, 640);
            hl.CloseFigure();
            var st = g.Save();
            g.SetClip(body, CombineMode.Intersect);
            using var hb = new LinearGradientBrush(new RectangleF(320, 270, 200, 380), Color.FromArgb(180, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f);
            g.FillPath(hb, hl);
            g.Restore(st);
        }
        using (var rimHi = new SolidBrush(Color.FromArgb(128, 255, 255, 255)))
        using (var r = RoundRect(new RectangleF(236, 712, 586, 30), 15))
            g.FillPath(rimHi, r);
        using (var pen = new Pen(BellEdge, edge) { LineJoin = LineJoin.Round }) g.DrawPath(pen, bell);
        g.Restore(state);
    }

    // The same mark as an SVG document (Assets\syswatch.svg is generated from this), for use outside the app.
    public static string ToSvg()
    {
        static string D(GraphicsPath p)
        {
            var pts = p.PathPoints;
            var types = p.PathTypes;
            var sb = new System.Text.StringBuilder();
            string F(float v) => v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            for (var i = 0; i < pts.Length; i++)
            {
                var t = types[i] & 0x07;
                if (t == 0) sb.Append($"M{F(pts[i].X)} {F(pts[i].Y)}");
                else if (t == 1) sb.Append($"L{F(pts[i].X)} {F(pts[i].Y)}");
                else if (t == 3 && i + 2 < pts.Length)
                {
                    sb.Append($"C{F(pts[i].X)} {F(pts[i].Y)} {F(pts[i + 1].X)} {F(pts[i + 1].Y)} {F(pts[i + 2].X)} {F(pts[i + 2].Y)}");
                    if ((types[i + 2] & 0x80) != 0) sb.Append('Z');
                    i += 2;
                    continue;
                }
                if ((types[i] & 0x80) != 0) sb.Append('Z');
            }
            return sb.ToString();
        }
        using var gear = GearPath();
        using var bell = BellPath();
        string Stops(Color[] c) => string.Join("", c.Select((col, k) => $"<stop offset=\"{(k == 0 ? 0 : k == 1 ? 0.55 : 1)}\" stop-color=\"#{col.R:X2}{col.G:X2}{col.B:X2}\"/>"));
        return $"""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1024 1024" width="1024" height="1024">
              <defs>
                <linearGradient id="gear" x1="0" y1="0" x2="0.7" y2="1">{Stops(GearStops)}</linearGradient>
                <linearGradient id="bell" x1="0" y1="0" x2="0.3" y2="1">{Stops(BellStops)}</linearGradient>
                <clipPath id="upper"><polygon points="{-200 - Gap},{-200 - Gap} {S + 200 - Gap},{-200 - Gap} {-200 - Gap},{S + 200 - Gap}"/></clipPath>
                <clipPath id="lower"><polygon points="{S + 200 + Gap},{-200 + Gap} {S + 200 + Gap},{S + 200 + Gap} {-200 + Gap},{S + 200 + Gap}"/></clipPath>
              </defs>
              <path clip-path="url(#upper)" fill="url(#gear)" fill-rule="evenodd" stroke="#0B3C74" stroke-opacity="0.82" stroke-width="10" stroke-linejoin="round" d="{D(gear)}"/>
              <path clip-path="url(#lower)" fill="url(#bell)" fill-rule="nonzero" stroke="#683000" stroke-opacity="0.75" stroke-width="10" stroke-linejoin="round" d="{D(bell)}"/>
            </svg>
            """;
    }

    // ── geometry ─────────────────────────────────────────────

    // Gear outline, the hollow ring and the solid hub (alternate fill: outline − ring + hub).
    private static GraphicsPath GearPath()
    {
        const float R = 0.93f * 512, r = 0.71f * 512, ring = 0.535f * 512, hub = 0.235f * 512;
        const int teeth = 8;
        var tip = 11f * MathF.PI / 180f;
        var root = 15.5f * MathF.PI / 180f;
        var step = 2 * MathF.PI / teeth;
        var pts = new List<PointF>();
        PointF P(float rad, float ang) => new(Cx + rad * MathF.Cos(ang), Cy + rad * MathF.Sin(ang));
        for (var i = 0; i < teeth; i++)
        {
            var a = i * step;
            pts.Add(P(r, a - root));
            for (var k = 0; k <= 6; k++) pts.Add(P(R, a - tip + 2 * tip * k / 6f));
            pts.Add(P(r, a + root));
            for (var k = 1; k < 8; k++) pts.Add(P(r, a + root + (step - 2 * root) * k / 8f));
        }
        var path = new GraphicsPath(FillMode.Alternate);
        path.AddPolygon(pts.ToArray());
        path.AddEllipse(Cx - ring, Cy - ring, ring * 2, ring * 2);
        path.AddEllipse(Cx - hub, Cy - hub, hub * 2, hub * 2);
        return path;
    }

    private static GraphicsPath BodyPath()
    {
        var body = new GraphicsPath();
        body.AddLine(278, 645, 290, 440);
        body.AddBezier(290, 440, 292, 300, 400, 232, 529, 232);
        body.AddBezier(529, 232, 658, 232, 766, 300, 768, 440);
        body.AddLine(768, 440, 780, 645);
        body.CloseFigure();
        return body;
    }

    private static GraphicsPath BellPath()
    {
        var path = new GraphicsPath(FillMode.Winding);
        path.AddEllipse(529 - 50, 182 - 50, 100, 100);
        using (var body = BodyPath()) path.AddPath(body, false);
        using var rim = new GraphicsPath();
        rim.AddLines(new PointF[] { new(254, 698), new(804, 698), new(846, 740) });
        rim.AddBezier(846, 740, 858, 748, 858, 760, 858, 770);
        rim.AddLine(858, 770, 858, 792);
        rim.AddBezier(858, 792, 858, 806, 852, 812, 838, 812);
        rim.AddLine(838, 812, 220, 812);
        rim.AddBezier(220, 812, 206, 812, 200, 806, 200, 792);
        rim.AddLine(200, 792, 200, 770);
        rim.AddBezier(200, 770, 200, 760, 200, 748, 212, 740);
        rim.CloseFigure();
        path.AddPath(rim, false);
        using var clapper = new GraphicsPath();
        clapper.AddArc(416, 845 - 113, 226, 226, 0, 180);
        clapper.CloseFigure();
        path.AddPath(clapper, false);
        return path;
    }

    // Each half is a triangle pulled back from the diagonal by the gap, which leaves the cut open.
    private static GraphicsPath Half(bool upperLeft, float gap)
    {
        var p = new GraphicsPath();
        p.AddPolygon(upperLeft
            ? new PointF[] { new(-200 - gap, -200 - gap), new(S + 200 - gap, -200 - gap), new(-200 - gap, S + 200 - gap) }
            : new PointF[] { new(S + 200 + gap, -200 + gap), new(S + 200 + gap, S + 200 + gap), new(-200 + gap, S + 200 + gap) });
        return p;
    }

    private static GraphicsPath RoundRect(RectangleF r, float radius)
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

    // ── materials ────────────────────────────────────────────

    // Three-stop gradient along (0,0)→(slant,1) of the bounds; the line is padded because GDI+ tiles past its ends.
    private static LinearGradientBrush Gradient(RectangleF b, Color[] stops, float slant, float mid)
    {
        var start = new PointF(b.Left, b.Top);
        var end = new PointF(b.Left + b.Width * slant, b.Bottom);
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        return new LinearGradientBrush(new PointF(start.X - dx, start.Y - dy), new PointF(end.X + dx, end.Y + dy), stops[0], stops[2])
        {
            InterpolationColors = new ColorBlend
            {
                Colors = [stops[0], stops[0], stops[1], stops[2], stops[2]],
                Positions = [0f, 1 / 3f, 1 / 3f + mid / 3, 2 / 3f, 1f],
            },
        };
    }

    private static void Highlight(Graphics g, GraphicsPath shape, RectangleF area, float opacity)
    {
        var state = g.Save();
        g.SetClip(shape, CombineMode.Intersect);
        using var path = new GraphicsPath();
        path.AddEllipse(area);
        using var brush = new LinearGradientBrush(area, Color.FromArgb((int)(204 * opacity), 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f);
        g.FillPath(brush, path);
        g.Restore(state);
    }
}
