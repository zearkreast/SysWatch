using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;

namespace SysWatch.UI;

// Discord, Telegram and Signal logos come unmodified from Simple Icons (Assets\Brands, see NOTICE.txt);
// Windows notifications and e-mail have no brand logo here, so they get a plain bell / envelope.
internal static partial class Brand
{
    private static readonly Dictionary<(string, int), Bitmap> Cache = new();

    private static readonly Dictionary<string, (Color Color, string? Svg, string? Glyph)> Channels = new()
    {
        ["Discord"] = (Color.FromArgb(0x58, 0x65, 0xF2), "discord", null),
        ["Telegram"] = (Color.FromArgb(0x26, 0xA5, 0xE4), "telegram", null),
        ["Signal"] = (Color.FromArgb(0x3B, 0x45, 0xFD), "signal", null),
        ["Windows"] = (Color.FromArgb(0x00, 0x78, 0xD4), null,
            // bell: dome, rim and clapper on a 24 grid
            "M12 2.5c-.9 0-1.6.7-1.6 1.6v.5C7.6 5.3 5.8 7.8 5.8 10.7v4.1L4 17.2v1h16v-1l-1.8-2.4v-4.1c0-2.9-1.8-5.4-4.6-6.1v-.5c0-.9-.7-1.6-1.6-1.6zM9.6 19.4a2.4 2.4 0 0 0 4.8 0z"),
        ["E-posta"] = (Color.FromArgb(0xF0, 0x8A, 0x1C), null,
            // envelope with flap
            "M3 6.2C3 5.5 3.5 5 4.2 5h15.6c.7 0 1.2.5 1.2 1.2v11.6c0 .7-.5 1.2-1.2 1.2H4.2C3.5 19 3 18.5 3 17.8zm1.9.4 7.1 5.6 7.1-5.6zm-.1 1.7v9h14.4v-9L12 14z"),
    };

    [GeneratedRegex("""\sd="([^"]+)"\s*/?>""")]
    private static partial Regex PathData();

    public static bool Has(string channel) => Channels.ContainsKey(channel);

    public static Bitmap Badge(string channel, int size)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue((channel, size), out var cached)) return cached;
            var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            if (Channels.TryGetValue(channel, out var info))
            {
                using var g = Graphics.FromImage(bmp);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                var c = info.Color;
                var r = new RectangleF(0.5f, 0.5f, size - 1.5f, size - 1.5f);
                Glass.Gloss(g, r, [Theme.Lighten(c, 0.45), Theme.Lighten(c, 0.12), c, Theme.Lighten(c, 0.08)], size * 0.24f,
                    Color.FromArgb((int)(c.R * 0.55), (int)(c.G * 0.55), (int)(c.B * 0.55)));
                using var glyph = SvgPath.Parse(info.Svg != null ? LoadSvg(info.Svg) : info.Glyph!);
                var inner = size * 0.6f;
                using (var m = new Matrix())
                {
                    m.Translate((size - inner) / 2f, (size - inner) / 2f);
                    m.Scale(inner / 24f, inner / 24f);
                    glyph.Transform(m);
                }
                using (var shadowPath = (GraphicsPath)glyph.Clone())
                using (var shift = new Matrix())
                {
                    shift.Translate(0, Math.Max(0.6f, size / 32f));
                    shadowPath.Transform(shift);
                    using var shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0));
                    g.FillPath(shadow, shadowPath);
                }
                g.FillPath(Brushes.White, glyph);
            }
            Cache[(channel, size)] = bmp;
            return bmp;
        }
    }

    private static string LoadSvg(string name)
    {
        using var stream = typeof(Brand).Assembly.GetManifestResourceStream($"SysWatch.Brands.{name}.svg");
        if (stream == null) return "";
        using var reader = new StreamReader(stream);
        var m = PathData().Match(reader.ReadToEnd());
        return m.Success ? m.Groups[1].Value : "";
    }
}
