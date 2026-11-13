using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace SysWatch.UI;

// Aero-style glass. The window's title band and rim are left see-through (DWM frame extension, alpha 0) and a
// separate blurred "chrome" window sits right behind it. Unlike the Windows 11 system backdrop, that blur does not
// switch off when the window loses focus, and the opaque body keeps ordinary GDI controls rendering normally.
internal static class Glass
{
    public static bool Supported { get; } = Environment.OSVersion.Version.Build >= 17763;
    private static bool _transparency = true;
    private static long _transparencyRead = long.MinValue;

    public static void Extend(Form form, Padding margins)
    {
        var dark = Theme.Dark ? 1 : 0;
        DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int));
        var round = 2;
        DwmSetWindowAttribute(form.Handle, 33, ref round, sizeof(int));
        if (!Supported) return;
        var m = new MARGINS { Left = margins.Left, Right = margins.Right, Top = margins.Top, Bottom = margins.Bottom };
        DwmExtendFrameIntoClientArea(form.Handle, ref m);
    }

    // Blurred acrylic behind the whole window via the composition accent API (works on inactive windows too).
    public static bool SetAccent(IntPtr hwnd, Color tint)
    {
        var accent = new ACCENTPOLICY
        {
            AccentState = 4, // ACCENT_ENABLE_ACRYLICBLURBEHIND
            AccentFlags = 2,
            GradientColor = (tint.A << 24) | (tint.B << 16) | (tint.G << 8) | tint.R,
        };
        var size = Marshal.SizeOf(accent);
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WINCOMPATTRDATA { Attribute = 19, Data = ptr, SizeOfData = size };
            return SetWindowCompositionAttribute(hwnd, ref data) != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    // Cached: read on every paint otherwise.
    public static bool TransparencyEnabled()
    {
        if (Environment.TickCount64 - _transparencyRead < 5000) return _transparency;
        _transparencyRead = Environment.TickCount64;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            _transparency = key?.GetValue("EnableTransparency") is not int v || v != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            _transparency = true;
        }
        return _transparency;
    }

    // live: pixels are cleared to alpha 0 (the chrome window's blur shows through) and lightly tinted.
    // Otherwise (transparency effects off, old Windows) the band is painted as solid glass.
    public static void PaintGlass(Graphics g, Rectangle bounds, Region glassArea, bool live)
    {
        var state = g.Save();
        g.SetClip(glassArea, CombineMode.Replace);
        if (live)
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.Clear(Color.FromArgb(0, 0, 0, 0));
            g.CompositingMode = CompositingMode.SourceOver;
            using var tint = new LinearGradientBrush(bounds, Theme.GlassTintTop, Theme.GlassTintBottom, 90f);
            g.FillRectangle(tint, bounds);
        }
        else
        {
            using var fallback = new LinearGradientBrush(bounds, Theme.GlassSolidTop, Theme.GlassSolidBottom, 90f);
            g.FillRectangle(fallback, bounds);
        }
        using (var sheen = new LinearGradientBrush(bounds, Color.Transparent, Color.Transparent, 25f))
        {
            sheen.InterpolationColors = new ColorBlend
            {
                Colors = [Color.FromArgb(0, 255, 255, 255), Color.FromArgb(Theme.Dark ? 16 : 40, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), Color.FromArgb(Theme.Dark ? 10 : 24, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)],
                Positions = [0f, 0.18f, 0.32f, 0.62f, 1f],
            };
            g.FillRectangle(sheen, bounds);
        }
        var glossBand = new Rectangle(bounds.X, bounds.Y, bounds.Width, Math.Max(2, Math.Min(bounds.Height, 44) / 2));
        using (var gloss = new LinearGradientBrush(new Rectangle(glossBand.X, glossBand.Y, glossBand.Width, glossBand.Height + 1), Theme.GlassGlossTop, Theme.GlassGlossBottom, 90f))
            g.FillRectangle(gloss, glossBand);
        using (var top = new Pen(Color.FromArgb(Theme.Dark ? 60 : 170, 255, 255, 255))) g.DrawLine(top, bounds.X, bounds.Y, bounds.Right, bounds.Y);
        g.Restore(state);
    }

    public static void Gloss(Graphics g, RectangleF r, Color[] stops, float radius, Color border)
    {
        if (r.Width < 2 || r.Height < 2) return;
        using var path = Draw.Round(r, radius);
        using (var brush = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), stops[0], stops[3], 90f))
        {
            brush.InterpolationColors = new ColorBlend
            {
                Colors = [stops[0], stops[1], stops[2], stops[3]],
                Positions = [0f, 0.49f, 0.5f, 1f],
            };
            g.FillPath(brush, path);
        }
        using var pen = new Pen(border);
        g.DrawPath(pen, path);
        using var inner = Draw.Round(new RectangleF(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2), Math.Max(0, radius - 1));
        using var hl = new Pen(Color.FromArgb(Theme.Dark ? 30 : 140, 255, 255, 255));
        g.DrawPath(hl, inner);
    }

    // Vector text keeps correct alpha on glass, unlike GDI text.
    public static void Wordmark(Graphics g, Rectangle r, float emPx)
    {
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var family = new FontFamily("Segoe UI");
        using var path = new GraphicsPath();
        using var fmt = StringFormat.GenericTypographic;
        var semibold = FontStyle.Bold;
        path.AddString("Sys", family, (int)semibold, emPx, new PointF(0, 0), fmt);
        var sysWidth = path.GetBounds().Right + emPx * 0.04f;
        using var light = new FontFamily(FontExists("Segoe UI Light") ? "Segoe UI Light" : "Segoe UI");
        path.AddString("Watch", light, (int)FontStyle.Regular, emPx, new PointF(sysWidth, 0), fmt);
        var b = path.GetBounds();
        using (var m = new Matrix())
        {
            m.Translate(r.X - b.X, r.Y + (r.Height - b.Height) / 2f - b.Y);
            path.Transform(m);
        }
        b = path.GetBounds();
        foreach (var (w, a) in new[] { (7f, 40), (4.5f, 70), (2.5f, 120) })
        {
            using var halo = new Pen(Color.FromArgb(Theme.Dark ? a / 3 : a, 255, 255, 255), w) { LineJoin = LineJoin.Round };
            g.DrawPath(halo, path);
        }
        using (var fill = new LinearGradientBrush(new RectangleF(b.X, b.Y - 1, b.Width, b.Height + 2),
                   Theme.Dark ? Color.White : Color.FromArgb(12, 40, 84), Theme.Dark ? Color.FromArgb(170, 214, 255) : Color.FromArgb(24, 104, 190), 90f))
            g.FillPath(fill, path);
        g.Restore(state);
    }

    public static float WordmarkWidth(float emPx)
    {
        using var family = new FontFamily("Segoe UI");
        using var path = new GraphicsPath();
        using var fmt = StringFormat.GenericTypographic;
        path.AddString("SysWatch", family, (int)FontStyle.Regular, emPx, new PointF(0, 0), fmt);
        return path.GetBounds().Width * 1.05f;
    }

    public static Rectangle Pill(Graphics g, string text, Font font, int right, int centerY, Color hue)
    {
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var size = g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);
        var h = font.Height + 6;
        var r = new Rectangle(right - (int)size.Width - 26, centerY - h / 2, (int)size.Width + 26, h);
        var dark = Color.FromArgb((int)(hue.R * 0.55), (int)(hue.G * 0.55), (int)(hue.B * 0.55));
        Gloss(g, r, [Color.FromArgb(250, 255, 255, 255), Theme.Lighten(hue, 0.86), Theme.Lighten(hue, 0.72), Theme.Lighten(hue, 0.8)], h / 2f, dark);
        using (var dot = new SolidBrush(hue)) g.FillEllipse(dot, r.X + 9, centerY - 3.5f, 7, 7);
        using var brush = new SolidBrush(dark);
        g.DrawString(text, font, brush, r.X + 19, centerY - size.Height / 2f, StringFormat.GenericTypographic);
        g.Restore(state);
        return r;
    }

    // Glowing text on glass via DrawThemeTextEx, alpha-blended so the glass underneath stays intact.
    public static void GlowText(Graphics g, IntPtr hwnd, string text, Font font, Rectangle r, Color color, bool rightAlign = false)
    {
        if (r.Width <= 0 || r.Height <= 0 || text.Length == 0) return;
        var hdc = g.GetHdc();
        try
        {
            var mem = CreateCompatibleDC(hdc);
            var bmi = new BITMAPINFO { biSize = 40, biWidth = r.Width, biHeight = -r.Height, biPlanes = 1, biBitCount = 32 };
            var dib = CreateDIBSection(hdc, ref bmi, 0, out _, IntPtr.Zero, 0);
            var oldBmp = SelectObject(mem, dib);
            var hfont = font.ToHfont();
            var oldFont = SelectObject(mem, hfont);
            var theme = OpenThemeData(hwnd, "WINDOW");
            var opts = new DTTOPTS
            {
                dwSize = Marshal.SizeOf<DTTOPTS>(),
                dwFlags = 0x2000 /*COMPOSITED*/ | 0x800 /*GLOWSIZE*/ | 0x1 /*TEXTCOLOR*/,
                iGlowSize = Theme.Dark ? 6 : 12,
                crText = color.R | (color.G << 8) | (color.B << 16),
            };
            var rc = new RECT { Left = 8, Top = 0, Right = r.Width - 8, Bottom = r.Height };
            var flags = 0x4 | 0x20 | 0x8000 | 0x800 | (rightAlign ? 0x2 : 0);
            DrawThemeTextEx(theme, mem, 0, 0, text, -1, flags, ref rc, ref opts);
            var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            AlphaBlend(hdc, r.X, r.Y, r.Width, r.Height, mem, 0, 0, r.Width, r.Height, blend);
            if (theme != IntPtr.Zero) CloseThemeData(theme);
            SelectObject(mem, oldFont);
            DeleteObject(hfont);
            SelectObject(mem, oldBmp);
            DeleteObject(dib);
            DeleteDC(mem);
        }
        finally
        {
            g.ReleaseHdc(hdc);
        }
    }

    private static bool FontExists(string name)
    {
        using var f = new Font(name, 10);
        return string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase);
    }

    [StructLayout(LayoutKind.Sequential)] private struct MARGINS { public int Left, Right, Top, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct ACCENTPOLICY { public int AccentState, AccentFlags, GradientColor, AnimationId; }
    [StructLayout(LayoutKind.Sequential)] private struct WINCOMPATTRDATA { public int Attribute; public IntPtr Data; public int SizeOfData; }
    [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFO { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [StructLayout(LayoutKind.Sequential)]
    private struct DTTOPTS
    {
        public int dwSize, dwFlags, crText, crBorder, crShadow, iTextShadowType, ptShadowX, ptShadowY;
        public int iBorderSize, iFontPropId, iColorPropId, iStateId, fApplyOverlay, iGlowSize;
        public IntPtr pfnDrawTextCallback, lParam;
    }

    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);
    [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("user32.dll")] private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WINCOMPATTRDATA data);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("msimg32.dll")] private static extern bool AlphaBlend(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, BLENDFUNCTION blend);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern IntPtr OpenThemeData(IntPtr hwnd, string classes);
    [DllImport("uxtheme.dll")] private static extern int CloseThemeData(IntPtr theme);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int DrawThemeTextEx(IntPtr theme, IntPtr hdc, int part, int state, string text, int len, int flags, ref RECT rect, ref DTTOPTS options);
}

// Input-transparent, never-activated window that only renders the blurred acrylic. It is kept exactly behind its
// AeroForm (same bounds, next in z-order), so it is only ever seen through that form's see-through glass margins.
internal sealed class GlassChrome : Form
{
    public GlassChrome()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80 | 0x20 | 0x08000000; // WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var round = 2;
        Glass.DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
        Retint();
    }

    public void Retint() => Glass.SetAccent(Handle, Theme.ChromeTint);

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.CompositingMode = CompositingMode.SourceCopy;
        e.Graphics.Clear(Color.FromArgb(0, 0, 0, 0));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
    }
}

// Borderless window with a glass title band and rim and a self-drawn caption; dragging and resizing come from hit-testing.
public class AeroForm : Form
{
    private const int WM_NCHITTEST = 0x84, WM_WINDOWPOSCHANGED = 0x47;
    private const int HTCLIENT = 1, HTCAPTION = 2, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14,
        HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

    // The embedded multi-size .ico: the extracted exe icon is a single 32 px image and looks blurry when scaled up.
    private static readonly Lazy<Icon?> AppIcon = new(Icons.AppIcon);

    private enum CaptionButton { None, Minimize, Maximize, Close }

    private CaptionButton _hover, _pressed;
    private Font? _titleFont;
    private Bitmap? _logo;
    private GlassChrome? _chrome;

    protected int GlassTop { get; set; }
    protected int Rim { get; set; }
    protected bool Draggable { get; set; } = true;
    protected bool Resizable { get; set; } = true;
    protected bool CaptionButtons { get; set; } = true;
    protected virtual string GlassTitle => Text;
    protected virtual string GlassStatus => "";
    protected virtual Color GlassStatusColor => Theme.SuccessFill;

    private bool Live => _chrome != null && Glass.TransparencyEnabled();

    public AeroForm()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        FormBorderStyle = FormBorderStyle.None;
        Font = SystemFonts.MessageBoxFont ?? Font;
        AutoScaleMode = AutoScaleMode.Dpi;
        GlassTop = (int)(Font.Height * 2.6);
        Rim = Math.Max(6, Font.Height / 2);
        if (AppIcon.Value != null) Icon = AppIcon.Value;
        Theme.Changed += OnThemeChangedInternal;
    }

    protected void ApplyGlassPadding() => Padding = new Padding(Rim, GlassTop, Rim, Rim);

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style |= 0x00020000 | 0x00080000; // WS_MINIMIZEBOX | WS_SYSMENU: taskbar minimise/restore and Alt+Space
            // A real sizing frame (hidden by WM_NCCALCSIZE below) gives edge resizing, maximise and Aero Snap.
            if (Resizable) cp.Style |= 0x00040000 | 0x00010000; // WS_THICKFRAME | WS_MAXIMIZEBOX
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (Resizable) SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0020 | 0x0002 | 0x0001 | 0x0004 | 0x0010); // FRAMECHANGED
        Glass.Extend(this, new Padding(Rim, GlassTop, Rim, Rim));
        if (Glass.Supported && _chrome == null)
        {
            _chrome = new GlassChrome { TopMost = TopMost };
            _ = _chrome.Handle;
        }
        RestyleTheme();
    }

    protected virtual void RestyleTheme()
    {
        BackColor = Theme.Window;
        ForeColor = Theme.Text;
        Theme.ApplyControls(this);
        if (IsHandleCreated) Glass.Extend(this, new Padding(Rim, GlassTop, Rim, Rim));
        _chrome?.Retint();
        Invalidate(true);
    }

    private void OnThemeChangedInternal()
    {
        if (IsHandleCreated && !IsDisposed) BeginInvoke(RestyleTheme);
    }

    // Keeps the chrome window glued to this one: same bounds, directly behind it, hidden when this is hidden.
    private void SyncChrome()
    {
        if (_chrome == null || _chrome.IsDisposed) return;
        var show = Visible && WindowState != FormWindowState.Minimized && Glass.TransparencyEnabled();
        const uint NOACTIVATE = 0x0010, SHOW = 0x0040, HIDE = 0x0080;
        if (_chrome.TopMost != TopMost) _chrome.TopMost = TopMost;
        SetWindowPos(_chrome.Handle, Handle, Left, Top, Width, Height, NOACTIVATE | (show ? SHOW : HIDE));
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        SyncChrome();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _chrome?.Close();
        _chrome?.Dispose();
        _chrome = null;
        base.OnFormClosed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x83 && Resizable && m.WParam != IntPtr.Zero) // WM_NCCALCSIZE: the whole window is client area
        {
            m.Result = IntPtr.Zero;
            return;
        }
        if (m.Msg == 0x24 && Resizable) // WM_GETMINMAXINFO
        {
            // Without WS_CAPTION Windows maximises over the whole monitor, taskbar included; keep to the work area.
            base.WndProc(ref m);
            var mon = MonitorFromWindow(Handle, 2);
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (mon != IntPtr.Zero && GetMonitorInfo(mon, ref info))
            {
                var mmi = Marshal.PtrToStructure<MINMAXINFO>(m.LParam);
                mmi.maxPosition = new POINTW { X = info.work.Left - info.monitor.Left, Y = info.work.Top - info.monitor.Top };
                mmi.maxSize = new POINTW { X = info.work.Right - info.work.Left, Y = info.work.Bottom - info.work.Top };
                Marshal.StructureToPtr(mmi, m.LParam, false);
            }
            return;
        }
        // With a sizing frame, Windows repaints a classic caption over the window whenever activation, title or icon
        // changes (e.g. on Alt+Tab). The whole window is client area, so none of that painting is wanted.
        if (m.Msg == 0x86) // WM_NCACTIVATE: let Windows track activation but skip the repaint
        {
            m.LParam = new IntPtr(-1);
            base.WndProc(ref m);
            m.Result = new IntPtr(1);
            return;
        }
        if (m.Msg == 0x85 || m.Msg == 0xAE || m.Msg == 0xAF) // WM_NCPAINT, WM_NCUAHDRAWCAPTION, WM_NCUAHDRAWFRAME
        {
            m.Result = IntPtr.Zero;
            return;
        }
        if ((m.Msg == 0x0C || m.Msg == 0x80) && IsHandleCreated) // WM_SETTEXT, WM_SETICON: update without painting
        {
            var style = GetWindowLong(Handle, -16);
            SetWindowLong(Handle, -16, style & ~0x10000000); // drop WS_VISIBLE for the call
            base.WndProc(ref m);
            SetWindowLong(Handle, -16, style);
            return;
        }
        if (m.Msg == WM_NCHITTEST)
        {
            var screen = new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16)));
            m.Result = (IntPtr)HitTest(PointToClient(screen));
            return;
        }
        base.WndProc(ref m);
        if (m.Msg == WM_WINDOWPOSCHANGED) SyncChrome();
    }

    private int HitTest(Point p)
    {
        var edge = Resizable && WindowState == FormWindowState.Normal ? Math.Max(6, Rim) : 0;
        var left = p.X < edge; var right = p.X >= Width - edge; var top = p.Y < edge; var bottom = p.Y >= Height - edge;
        if (top && left) return HTTOPLEFT;
        if (top && right) return HTTOPRIGHT;
        if (bottom && left) return HTBOTTOMLEFT;
        if (bottom && right) return HTBOTTOMRIGHT;
        if (left) return HTLEFT;
        if (right) return HTRIGHT;
        if (top) return HTTOP;
        if (bottom) return HTBOTTOM;
        if (Draggable && p.Y < GlassTop && ButtonAt(p) == CaptionButton.None) return HTCAPTION;
        return HTCLIENT;
    }

    private Rectangle ButtonRect(CaptionButton b)
    {
        var h = Math.Min(GlassTop - 12, Font.Height + 6);
        var closeW = (int)(h * 2.1);
        var minW = (int)(h * 1.35);
        var top = WindowState == FormWindowState.Maximized ? 2 : 1;
        var close = new Rectangle(Width - Rim - closeW, top, closeW, h);
        if (b == CaptionButton.Close) return close;
        var max = Resizable ? new Rectangle(close.Left - minW + 1, top, minW, h) : close;
        return b == CaptionButton.Maximize ? max : new Rectangle(max.Left - minW + 1, top, minW, h);
    }

    private CaptionButton ButtonAt(Point p)
    {
        if (!CaptionButtons) return CaptionButton.None;
        if (ButtonRect(CaptionButton.Close).Contains(p)) return CaptionButton.Close;
        if (Resizable && ButtonRect(CaptionButton.Maximize).Contains(p)) return CaptionButton.Maximize;
        if (ButtonRect(CaptionButton.Minimize).Contains(p)) return CaptionButton.Minimize;
        return CaptionButton.None;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var b = ButtonAt(e.Location);
        if (b != _hover) { _hover = b; Invalidate(new Rectangle(0, 0, Width, GlassTop)); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover != CaptionButton.None) { _hover = CaptionButton.None; Invalidate(new Rectangle(0, 0, Width, GlassTop)); }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        _pressed = e.Button == MouseButtons.Left ? ButtonAt(e.Location) : CaptionButton.None;
        if (_pressed != CaptionButton.None) Invalidate(new Rectangle(0, 0, Width, GlassTop));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var b = ButtonAt(e.Location);
        var pressed = _pressed;
        _pressed = CaptionButton.None;
        Invalidate(new Rectangle(0, 0, Width, GlassTop));
        if (b != pressed) return;
        if (b == CaptionButton.Close) Close();
        else if (b == CaptionButton.Minimize) WindowState = FormWindowState.Minimized;
        else if (b == CaptionButton.Maximize) WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        var all = new Rectangle(0, 0, Width, Height);
        var body = new Rectangle(Rim, GlassTop, Width - Rim * 2, Height - GlassTop - Rim);
        using (var glass = new Region(all))
        {
            glass.Exclude(body);
            Glass.PaintGlass(g, all, glass, Live);
        }
        using (var b = new SolidBrush(Theme.Window)) g.FillRectangle(b, body);
        using (var edge = new Pen(Theme.GlassEdge)) g.DrawRectangle(edge, body.X - 1, body.Y - 1, body.Width + 1, body.Height + 1);
        using (var outer = new Pen(Color.FromArgb(Theme.Dark ? 120 : 140, 20, 50, 90))) g.DrawRectangle(outer, 0, 0, Width - 1, Height - 1);

        Draw.Setup(g);
        var logoSize = Math.Min(GlassTop - 12, (int)(Font.Height * 1.6));
        if (_logo == null || _logo.Width != logoSize) { _logo?.Dispose(); _logo = Logo.Render(logoSize); }
        g.DrawImage(_logo, Rim + 4, (GlassTop - logoSize) / 2);
        var textLeft = Rim + 10 + logoSize;
        var buttonsLeft = CaptionButtons ? ButtonRect(CaptionButton.Minimize).Left : Width - Rim;
        var status = GlassStatus;
        var pill = status.Length > 0 ? Glass.Pill(g, status, Font, buttonsLeft - 10, GlassTop / 2, GlassStatusColor) : Rectangle.Empty;
        var titleRight = pill.IsEmpty ? buttonsLeft - 6 : pill.Left - 6;
        if (GlassTitle == "SysWatch")
        {
            Glass.Wordmark(g, new Rectangle(textLeft, 0, Math.Max(0, titleRight - textLeft), GlassTop), Font.Height * 1.25f);
        }
        else
        {
            _titleFont ??= new Font(Font.FontFamily, Font.Size * 1.08f);
            Glass.GlowText(g, Handle, GlassTitle, _titleFont, new Rectangle(textLeft - 8, 0, Math.Max(0, titleRight - textLeft + 8), GlassTop), Theme.GlassText);
        }
        if (CaptionButtons) PaintCaptionButtons(g);
    }

    private void PaintCaptionButtons(Graphics g)
    {
        foreach (var b in Resizable ? new[] { CaptionButton.Minimize, CaptionButton.Maximize, CaptionButton.Close } : new[] { CaptionButton.Minimize, CaptionButton.Close })
        {
            var r = ButtonRect(b);
            var hot = _hover == b;
            var down = _pressed == b && hot;
            Color[] stops = b == CaptionButton.Close
                ? hot
                    ? [Color.FromArgb(244, 168, 148), Color.FromArgb(230, 96, 70), Color.FromArgb(204, 44, 22), Color.FromArgb(236, 128, 88)]
                    : [Color.FromArgb(236, 176, 166), Color.FromArgb(218, 116, 100), Color.FromArgb(194, 70, 52), Color.FromArgb(214, 126, 104)]
                : hot ? Theme.ButtonHoverGloss : [Color.FromArgb(160, 255, 255, 255), Color.FromArgb(90, 255, 255, 255), Color.FromArgb(50, 255, 255, 255), Color.FromArgb(90, 255, 255, 255)];
            if (down) stops = stops.Select(c => Color.FromArgb(c.A, (int)(c.R * 0.85), (int)(c.G * 0.85), (int)(c.B * 0.85))).ToArray();
            Glass.Gloss(g, new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), stops, 3.5f, Color.FromArgb(170, 30, 50, 80));
            using var pen = new Pen(b == CaptionButton.Close ? Color.White : Theme.GlassText, 1.6f);
            var cx = r.X + r.Width / 2f;
            var cy = r.Y + r.Height / 2f;
            var s = r.Height * 0.18f;
            if (b == CaptionButton.Close)
            {
                g.DrawLine(pen, cx - s, cy - s, cx + s, cy + s);
                g.DrawLine(pen, cx - s, cy + s, cx + s, cy - s);
            }
            else if (b == CaptionButton.Maximize)
            {
                if (WindowState == FormWindowState.Maximized)
                {
                    g.DrawRectangle(pen, cx - s * 1.1f, cy - s * 0.5f, s * 1.7f, s * 1.5f);
                    g.DrawLine(pen, cx - s * 0.5f, cy - s * 1.1f, cx + s * 1.2f, cy - s * 1.1f);
                    g.DrawLine(pen, cx + s * 1.2f, cy - s * 1.1f, cx + s * 1.2f, cy + s * 0.4f);
                }
                else
                {
                    g.DrawRectangle(pen, cx - s * 1.2f, cy - s * 0.9f, s * 2.4f, s * 1.8f);
                }
            }
            else
            {
                g.DrawLine(pen, cx - s * 1.3f, cy + s * 0.8f, cx + s * 1.3f, cy + s * 0.8f);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Theme.Changed -= OnThemeChangedInternal;
            _titleFont?.Dispose();
            _logo?.Dispose();
            _chrome?.Dispose();
        }
        base.Dispose(disposing);
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [StructLayout(LayoutKind.Sequential)] private struct RECTW { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINTW { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int cbSize; public RECTW monitor, work; public uint flags; }
    [StructLayout(LayoutKind.Sequential)] private struct MINMAXINFO { public POINTW reserved, maxSize, maxPosition, minTrackSize, maxTrackSize; }
}
