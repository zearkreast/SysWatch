using System.Drawing.Drawing2D;

namespace SysWatch.UI;

// Small glossy badges for sensor types and the CPU (the Tango set has no chip or thermometer), drawn as vectors so
// they stay sharp at any DPI.
internal static class SensorGlyphs
{
    // Types in the order the tree lists them.
    public static readonly string[] TypeOrder =
        ["Clock", "Temperature", "Load", "Power", "Voltage", "Current", "Fan", "Control", "Level", "Data", "SmallData", "Throughput", "Frequency", "Energy", "Factor"];

    public static int TypeRank(string type)
    {
        var i = Array.IndexOf(TypeOrder, type);
        return i < 0 ? TypeOrder.Length : i;
    }

    public static string GroupLabel(string type) => type switch
    {
        "Clock" => L.T("Saat hızları", "Clocks"),
        "Temperature" => L.T("Sıcaklıklar", "Temperatures"),
        "Load" => L.T("Yük", "Load"),
        "Power" => L.T("Güç", "Powers"),
        "Voltage" => L.T("Voltajlar", "Voltages"),
        "Current" => L.T("Akımlar", "Currents"),
        "Fan" => L.T("Fanlar", "Fans"),
        "Control" => L.T("Fan kontrolü", "Controls"),
        "Level" => L.T("Seviyeler", "Levels"),
        "Data" or "SmallData" => L.T("Veri", "Data"),
        "Throughput" => L.T("Aktarım", "Throughput"),
        "Frequency" => L.T("Frekanslar", "Frequencies"),
        "Energy" => L.T("Enerji", "Energy"),
        "Factor" => L.T("Oranlar", "Factors"),
        _ => type,
    };

    private static Color Hue(string type) => type switch
    {
        "Clock" => Color.FromArgb(40, 130, 225),
        "Temperature" => Color.FromArgb(226, 84, 52),
        "Load" => Color.FromArgb(58, 166, 70),
        "Power" => Color.FromArgb(232, 160, 24),
        "Voltage" => Color.FromArgb(142, 92, 214),
        "Current" => Color.FromArgb(24, 150, 150),
        "Fan" or "Control" => Color.FromArgb(22, 168, 204),
        "Data" or "SmallData" => Color.FromArgb(84, 104, 210),
        "Throughput" => Color.FromArgb(32, 160, 120),
        _ => Color.FromArgb(120, 132, 150),
    };

    public static void TypeBadge(Graphics g, string type, RectangleF r)
    {
        Orb(g, r, Hue(type));
        var s = r.Width;
        var c = new PointF(r.X + s / 2, r.Y + s / 2);
        using var pen = new Pen(Color.White, Math.Max(1.2f, s / 11)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var fill = new SolidBrush(Color.White);
        float U(float f) => s * f;
        switch (type)
        {
            case "Clock":
                g.DrawEllipse(pen, c.X - U(.28f), c.Y - U(.28f), U(.56f), U(.56f));
                g.DrawLine(pen, c.X, c.Y, c.X, c.Y - U(.17f));
                g.DrawLine(pen, c.X, c.Y, c.X + U(.13f), c.Y + U(.06f));
                break;
            case "Temperature":
                g.DrawLine(pen, c.X, r.Y + U(.2f), c.X, c.Y + U(.08f));
                g.FillEllipse(fill, c.X - U(.13f), c.Y + U(.04f), U(.26f), U(.26f));
                break;
            case "Load":
                for (var i = 0; i < 3; i++)
                {
                    var h = U(.18f + i * .14f);
                    g.FillRectangle(fill, r.X + U(.24f + i * .19f), r.Bottom - U(.22f) - h, U(.13f), h);
                }
                break;
            case "Power":
                g.FillPolygon(fill, new[]
                {
                    new PointF(r.X + U(.56f), r.Y + U(.16f)), new PointF(r.X + U(.3f), r.Y + U(.56f)), new PointF(r.X + U(.5f), r.Y + U(.56f)),
                    new PointF(r.X + U(.42f), r.Y + U(.86f)), new PointF(r.X + U(.72f), r.Y + U(.42f)), new PointF(r.X + U(.52f), r.Y + U(.42f)),
                });
                break;
            case "Voltage":
                g.DrawLines(pen, new[] { new PointF(r.X + U(.28f), r.Y + U(.28f)), new PointF(c.X, r.Y + U(.72f)), new PointF(r.X + U(.72f), r.Y + U(.28f)) });
                break;
            case "Current":
                g.DrawLine(pen, r.X + U(.24f), c.Y, r.X + U(.74f), c.Y);
                g.DrawLines(pen, new[] { new PointF(r.X + U(.56f), c.Y - U(.16f)), new PointF(r.X + U(.74f), c.Y), new PointF(r.X + U(.56f), c.Y + U(.16f)) });
                break;
            case "Fan":
            case "Control":
                var state = g.Save();
                g.TranslateTransform(c.X, c.Y);
                for (var i = 0; i < 3; i++)
                {
                    g.FillEllipse(fill, -U(.09f), -U(.32f), U(.18f), U(.3f));
                    g.RotateTransform(120);
                }
                g.Restore(state);
                using (var hub = new SolidBrush(Hue(type))) g.FillEllipse(hub, c.X - U(.06f), c.Y - U(.06f), U(.12f), U(.12f));
                break;
            case "Data":
            case "SmallData":
                for (var i = 0; i < 3; i++) g.DrawEllipse(pen, c.X - U(.22f), r.Y + U(.22f + i * .16f), U(.44f), U(.16f));
                break;
            case "Throughput":
                g.DrawLine(pen, r.X + U(.36f), r.Y + U(.24f), r.X + U(.36f), r.Y + U(.74f));
                g.DrawLines(pen, new[] { new PointF(r.X + U(.24f), r.Y + U(.36f)), new PointF(r.X + U(.36f), r.Y + U(.24f)), new PointF(r.X + U(.48f), r.Y + U(.36f)) });
                g.DrawLine(pen, r.X + U(.64f), r.Y + U(.26f), r.X + U(.64f), r.Y + U(.76f));
                g.DrawLines(pen, new[] { new PointF(r.X + U(.52f), r.Y + U(.64f)), new PointF(r.X + U(.64f), r.Y + U(.76f)), new PointF(r.X + U(.76f), r.Y + U(.64f)) });
                break;
            default:
                g.FillEllipse(fill, c.X - U(.12f), c.Y - U(.12f), U(.24f), U(.24f));
                break;
        }
    }

    public static void Chip(Graphics g, RectangleF r)
    {
        var s = r.Width;
        using var pin = new Pen(Color.FromArgb(150, 160, 172), Math.Max(1f, s / 16));
        for (var i = 0; i < 4; i++)
        {
            var o = s * (.26f + i * .16f);
            g.DrawLine(pin, r.X + o, r.Y + s * .04f, r.X + o, r.Bottom - s * .04f);
            g.DrawLine(pin, r.X + s * .04f, r.Y + o, r.Right - s * .04f, r.Y + o);
        }
        var body = new RectangleF(r.X + s * .16f, r.Y + s * .16f, s * .68f, s * .68f);
        Orb(g, body, Color.FromArgb(96, 112, 134));
        using var die = Draw.Round(new RectangleF(body.X + s * .17f, body.Y + s * .17f, s * .34f, s * .34f), s * .05f);
        using var dieFill = new LinearGradientBrush(new RectangleF(body.X, body.Y, s * .68f, s * .68f), Color.FromArgb(230, 236, 244), Color.FromArgb(170, 182, 198), 45f);
        g.FillPath(dieFill, die);
    }

    private static void Orb(Graphics g, RectangleF r, Color hue)
    {
        var radius = r.Width * .26f;
        using var path = Draw.Round(r, radius);
        using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Theme.Lighten(hue, .35), Dark(hue, .18), 90f))
            g.FillPath(b, path);
        var glossRect = new RectangleF(r.X + 1, r.Y + 1, r.Width - 2, r.Height * .48f);
        using (var gloss = Draw.Round(glossRect, Math.Max(1, radius - 1)))
        using (var gb = new LinearGradientBrush(new RectangleF(glossRect.X, glossRect.Y - 1, glossRect.Width, glossRect.Height + 2), Color.FromArgb(150, 255, 255, 255), Color.FromArgb(25, 255, 255, 255), 90f))
            g.FillPath(gb, gloss);
        using var edge = new Pen(Dark(hue, .35));
        g.DrawPath(edge, path);
    }

    private static Color Dark(Color c, double amount) =>
        Color.FromArgb((int)(c.R * (1 - amount)), (int)(c.G * (1 - amount)), (int)(c.B * (1 - amount)));
}
