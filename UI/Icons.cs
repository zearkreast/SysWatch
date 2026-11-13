using System.Drawing.Drawing2D;

namespace SysWatch.UI;

// UI icons from the public-domain Tango set (Assets\Icons, 32 px PNGs embedded in the exe), scaled once per size
// and cached. Also the multi-size app icon, so Windows can pick a sharp size for the title bar and taskbar.
internal static class Icons
{
    public const string Status = "utilities-system-monitor";
    public const string Details = "document-properties";
    public const string History = "office-calendar";
    public const string Thresholds = "dialog-warning";
    public const string Channels = "mail-send-receive";
    public const string Appearance = "preferences-desktop-theme";
    public const string Hardware = "computer";
    public const string Security = "system-lock-screen";
    public const string App = "preferences-system";
    public const string Info = "dialog-information";
    public const string Chat = "internet-group-chat";
    public const string Mail = "internet-mail";
    public const string Encrypted = "network-wireless-encrypted";
    public const string Recovered = "weather-clear";
    public const string Terminal = "utilities-terminal";
    public const string Mute = "audio-volume-muted";
    public const string Log = "accessories-text-editor";
    public const string Exit = "system-log-out";
    public const string Gpu = "video-display";
    public const string Disk = "drive-harddisk";
    public const string Network = "network-wired";
    public const string Power = "battery";
    public const string Board = "preferences-desktop-peripherals";
    public const string Memory = "media-flash";

    private static readonly Dictionary<(string, int), Bitmap> Cache = new();

    // "brand:Discord" etc. returns the channel badge instead of a Tango icon.
    public static Bitmap Get(string name, int size)
    {
        if (name.StartsWith("brand:", StringComparison.Ordinal)) return Brand.Badge(name[6..], size);
        lock (Cache)
        {
            if (Cache.TryGetValue((name, size), out var cached)) return cached;
            using var stream = typeof(Icons).Assembly.GetManifestResourceStream($"SysWatch.Icons.{name}.png");
            Bitmap result;
            if (stream == null)
            {
                result = new Bitmap(size, size);
            }
            else
            {
                using var source = new Bitmap(stream);
                result = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using var g = Graphics.FromImage(result);
                g.InterpolationMode = size < source.Width ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.DrawImage(source, new Rectangle(0, 0, size, size));
            }
            Cache[(name, size)] = result;
            return result;
        }
    }

    public static int SizeFor(Font font) => Math.Max(16, (int)Math.Round(font.Height * 1.3));

    public static Icon? AppIcon()
    {
        using var stream = typeof(Icons).Assembly.GetManifestResourceStream("SysWatch.syswatch.ico");
        return stream == null ? null : new Icon(stream);
    }
}
