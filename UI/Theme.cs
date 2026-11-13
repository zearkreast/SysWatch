using System.Runtime.InteropServices;
using Microsoft.Win32;
using SysWatch.Config;

namespace SysWatch.UI;

// Frutiger Aero palette: sky-blue glass, glossy white surfaces. Light by default; a dark "smoked glass" variant on request.
public static class Theme
{
    private static UiTheme _mode = UiTheme.Light;
    private static bool _systemDark;

    public static bool Dark => _mode == UiTheme.Dark || (_mode == UiTheme.System && _systemDark);

    // Glass (title band and rim)
    // Over the blurred chrome window: light enough that the blur stays visible.
    public static Color GlassTintTop => Dark ? Color.FromArgb(70, 30, 40, 54) : Color.FromArgb(18, 180, 220, 250);
    public static Color GlassTintBottom => Dark ? Color.FromArgb(100, 16, 22, 30) : Color.FromArgb(42, 80, 155, 228);
    public static Color ChromeTint => Dark ? Color.FromArgb(150, 18, 24, 32) : Color.FromArgb(40, 150, 200, 245);
    public static Color GlassSolidTop => Dark ? Color.FromArgb(56, 66, 80) : Color.FromArgb(178, 214, 244);
    public static Color GlassSolidBottom => Dark ? Color.FromArgb(32, 38, 48) : Color.FromArgb(120, 176, 230);
    public static Color GlassGlossTop => Color.FromArgb(Dark ? 50 : 120, 255, 255, 255);
    public static Color GlassGlossBottom => Color.FromArgb(Dark ? 4 : 18, 255, 255, 255);
    public static Color GlassText => Dark ? Color.White : Color.FromArgb(16, 32, 52);
    public static Color GlassEdge => Dark ? Color.FromArgb(120, 0, 0, 0) : Color.FromArgb(150, 40, 80, 130);

    // Body
    public static Color Window => Dark ? Color.FromArgb(28, 33, 40) : Color.FromArgb(250, 252, 255);
    public static Color Surface => Dark ? Color.FromArgb(38, 45, 54) : Color.White;
    public static Color SurfaceHover => Dark ? Color.FromArgb(46, 68, 92) : Color.FromArgb(229, 243, 255);
    public static Color Field => Dark ? Color.FromArgb(22, 26, 32) : Color.White;
    public static Color Border => Dark ? Color.FromArgb(70, 82, 96) : Color.FromArgb(175, 198, 222);
    public static Color Text => Dark ? Color.FromArgb(236, 242, 248) : Color.FromArgb(18, 34, 52);
    public static Color TextSecondary => Dark ? Color.FromArgb(178, 196, 214) : Color.FromArgb(52, 82, 112);
    public static Color TextMuted => Dark ? Color.FromArgb(130, 146, 162) : Color.FromArgb(110, 128, 148);
    public static Color Accent => Dark ? Color.FromArgb(96, 176, 255) : Color.FromArgb(30, 110, 200);
    public static Color OnAccent => Color.White;

    // Glossy surfaces: top / just above the split / just below the split / bottom
    public static Color[] CardGloss => Dark
        ? [Color.FromArgb(58, 68, 80), Color.FromArgb(44, 52, 62), Color.FromArgb(34, 41, 50), Color.FromArgb(40, 48, 58)]
        : [Color.White, Color.FromArgb(240, 247, 253), Color.FromArgb(222, 236, 249), Color.FromArgb(236, 245, 252)];
    public static Color[] AlarmGloss => Dark
        ? [Color.FromArgb(104, 46, 44), Color.FromArgb(84, 36, 35), Color.FromArgb(70, 28, 28), Color.FromArgb(80, 34, 33)]
        : [Color.FromArgb(255, 247, 246), Color.FromArgb(253, 229, 226), Color.FromArgb(250, 204, 199), Color.FromArgb(253, 230, 227)];
    public static Color[] ButtonGloss => Dark
        ? [Color.FromArgb(76, 88, 102), Color.FromArgb(58, 68, 80), Color.FromArgb(44, 52, 62), Color.FromArgb(54, 64, 76)]
        : [Color.FromArgb(252, 253, 255), Color.FromArgb(236, 242, 249), Color.FromArgb(214, 227, 242), Color.FromArgb(228, 238, 249)];
    public static Color[] ButtonHoverGloss => Dark
        ? [Color.FromArgb(88, 126, 170), Color.FromArgb(62, 100, 146), Color.FromArgb(44, 82, 128), Color.FromArgb(56, 96, 142)]
        : [Color.FromArgb(234, 246, 253), Color.FromArgb(217, 240, 252), Color.FromArgb(190, 230, 253), Color.FromArgb(167, 217, 245)];
    public static Color[] PrimaryGloss => Dark
        ? [Color.FromArgb(92, 160, 230), Color.FromArgb(52, 124, 204), Color.FromArgb(30, 98, 180), Color.FromArgb(46, 120, 204)]
        : [Color.FromArgb(120, 190, 245), Color.FromArgb(70, 150, 225), Color.FromArgb(32, 112, 200), Color.FromArgb(60, 145, 225)];
    public static Color[] SelectionGloss => Dark
        ? [Color.FromArgb(66, 104, 148), Color.FromArgb(52, 88, 132)]
        : [Color.FromArgb(220, 235, 252), Color.FromArgb(193, 219, 252)];
    public static Color SelectionBorder => Dark ? Color.FromArgb(96, 140, 190) : Color.FromArgb(125, 162, 206);
    public static Color ButtonBorder => Dark ? Color.FromArgb(24, 30, 38) : Color.FromArgb(112, 136, 164);
    public static Color AlarmBorder => Dark ? Color.FromArgb(150, 70, 66) : Color.FromArgb(214, 120, 110);

    public static Color Danger => Dark ? Color.FromArgb(255, 160, 150) : Color.FromArgb(170, 32, 20);
    public static Color DangerFill => Color.FromArgb(226, 75, 74);
    public static Color DangerSurface => AlarmGloss[2];
    public static Color Success => Dark ? Color.FromArgb(120, 214, 110) : Color.FromArgb(22, 120, 40);
    public static Color SuccessFill => Color.FromArgb(52, 168, 83);
    public static Color Warning => Dark ? Color.FromArgb(252, 200, 90) : Color.FromArgb(150, 90, 0);
    public static Color Neutral => Color.FromArgb(136, 140, 148);
    public static Color Chart => Dark ? Color.FromArgb(110, 190, 255) : Color.FromArgb(28, 120, 210);

    public static string IconFont { get; } = FontExists("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

    public static event Action? Changed;

    static Theme()
    {
        _systemDark = ReadSystemDark();
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)) return;
            var was = Dark;
            _systemDark = ReadSystemDark();
            if (was != Dark) Changed?.Invoke();
        };
    }

    public static void SetMode(UiTheme mode)
    {
        if (mode == _mode) return;
        var was = Dark;
        _mode = mode;
        if (was != Dark) Changed?.Invoke();
    }

    // For screenshots/tests.
    public static void ForceDark(bool dark) => SetMode(dark ? UiTheme.Dark : UiTheme.Light);

    private static bool ReadSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static Color Lighten(Color c, double amount) => Color.FromArgb(
        (int)(c.R + (255 - c.R) * amount), (int)(c.G + (255 - c.G) * amount), (int)(c.B + (255 - c.B) * amount));

    // Recolours stock controls. Custom-drawn ones read the palette in OnPaint.
    public static void ApplyControls(Control root)
    {
        foreach (Control c in root.Controls)
        {
            switch (c)
            {
                case TextBox or NumericUpDown:
                    c.BackColor = Field;
                    c.ForeColor = Text;
                    break;
                case ComboBox combo:
                    combo.FlatStyle = FlatStyle.Flat;
                    combo.BackColor = Field;
                    combo.ForeColor = Text;
                    if (combo.IsHandleCreated) SetWindowTheme(combo.Handle, Dark ? "DarkMode_CFD" : null, null);
                    break;
                case DataGridView grid:
                    StyleGrid(grid);
                    break;
                case LinkLabel link:
                    link.LinkColor = Accent;
                    link.ActiveLinkColor = Accent;
                    link.VisitedLinkColor = Accent;
                    break;
                case FlatButton fb:
                    fb.Invalidate();
                    break;
                case CheckBox or RadioButton or Label:
                    c.ForeColor = c.Tag as string == "muted" ? TextMuted : Text;
                    break;
                case Panel or TableLayoutPanel or FlowLayoutPanel:
                    if (c.Tag as string != "keep") c.BackColor = Color.Transparent;
                    if (c is ScrollableControl { AutoScroll: true } && c.IsHandleCreated)
                        SetWindowTheme(c.Handle, Dark ? "DarkMode_Explorer" : "Explorer", null);
                    break;
            }
            if (c.HasChildren) ApplyControls(c);
        }
    }

    private static void StyleGrid(DataGridView g)
    {
        g.BackgroundColor = Surface;
        g.BorderStyle = BorderStyle.None;
        g.GridColor = Border;
        g.EnableHeadersVisualStyles = false;
        g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        g.ColumnHeadersDefaultCellStyle.BackColor = CardGloss[2];
        g.ColumnHeadersDefaultCellStyle.ForeColor = TextSecondary;
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = CardGloss[2];
        g.DefaultCellStyle.BackColor = Surface;
        g.DefaultCellStyle.ForeColor = Text;
        g.DefaultCellStyle.SelectionBackColor = SelectionGloss[1];
        g.DefaultCellStyle.SelectionForeColor = Text;
        if (g.IsHandleCreated) SetWindowTheme(g.Handle, Dark ? "DarkMode_Explorer" : "Explorer", null);
    }

    private sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Dark ? Surface : Color.FromArgb(241, 245, 251);
        public override Color ImageMarginGradientMiddle => ImageMarginGradientBegin;
        public override Color ImageMarginGradientEnd => ImageMarginGradientBegin;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => SelectionBorder;
        public override Color MenuItemSelected => SelectionGloss[0];
        public override Color MenuItemSelectedGradientBegin => SelectionGloss[0];
        public override Color MenuItemSelectedGradientEnd => SelectionGloss[1];
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Surface;
    }

    public static void StyleMenu(ToolStrip menu)
    {
        menu.Renderer = new ToolStripProfessionalRenderer(new MenuColors()) { RoundedEdges = true };
        menu.BackColor = Surface;
        menu.ForeColor = Text;
        foreach (ToolStripItem item in menu.Items) StyleItem(item);
    }

    private static void StyleItem(ToolStripItem item)
    {
        item.ForeColor = Text;
        if (item is not ToolStripMenuItem mi) return;
        mi.DropDown.BackColor = Surface;
        mi.DropDown.Renderer = new ToolStripProfessionalRenderer(new MenuColors()) { RoundedEdges = true };
        foreach (ToolStripItem sub in mi.DropDownItems) StyleItem(sub);
    }

    private static bool FontExists(string name)
    {
        using var f = new Font(name, 10);
        return string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase);
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    internal static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);
}
