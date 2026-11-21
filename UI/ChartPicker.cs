using SysWatch.Config;
using SysWatch.Monitoring;

namespace SysWatch.UI;

// Chooses what a status card shows: one of the built-in cards, or any sensor from the same tree as the details page.
internal sealed class ChartPicker : AeroForm
{
    private readonly IUiHost _app;
    private readonly SensorTree _tree;
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 2000 };

    // The chosen chart key, and for a sensor its name, hardware and type so the card can be labelled before the next reading.
    public string? Chosen { get; private set; }
    public SensorSample? ChosenSensor { get; private set; }

    public ChartPicker(IUiHost app, IReadOnlyCollection<string> taken, string? current)
    {
        _app = app;
        Text = current == null ? L.T("Grafik ekle", "Add chart") : L.T("Grafiği değiştir", "Change chart");
        CaptionButtons = false;
        ShowInTaskbar = false;
        KeyPreview = true;
        StartPosition = FormStartPosition.CenterParent;
        ApplyGlassPadding();
        var unit = Font.Height;
        ClientSize = new Size(unit * 36, unit * 32);
        MinimumSize = new Size(unit * 26, unit * 22);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(unit / 2, unit / 3, unit / 2, 0) };
        var builtInLabel = new Label { Text = L.T("Hazır kartlar", "Built-in cards"), AutoSize = true, Dock = DockStyle.Top, Tag = "muted", Padding = new Padding(0, 0, 0, 4) };
        var builtIns = new TableLayoutPanel { Dock = DockStyle.Top, Height = (int)(unit * 2.6), ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
        for (var i = 0; i < 4; i++) builtIns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        var iconSize = Icons.SizeFor(Font) - 4;
        foreach (var (key, text, icon) in new[]
        {
            ("cpu", "CPU", Icons.Hardware), ("gpu", "GPU", Icons.Gpu), ("ram", "RAM", Icons.Memory), ("disk", "Disk", Icons.Disk),
        })
        {
            var onAnotherCard = key != current && taken.Contains(key);
            var b = new FlatButton(onAnotherCard ? $"{text} ✓" : text, key == current)
            {
                AutoSize = false, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 6, 0), Image = Icons.Get(icon, iconSize), Enabled = !onAnotherCard,
            };
            b.Click += (_, _) => Pick(key, null);
            builtIns.Controls.Add(b);
        }

        var sensorsLabel = new Label
        {
            Text = L.T("Ya da herhangi bir sensör: tıklayınca seçilir", "Or any sensor: click one to choose it"),
            AutoSize = true, Dock = DockStyle.Top, Tag = "muted", Padding = new Padding(0, unit / 2, 0, 4),
        };
        var search = DetailsView.SearchBox(Font);
        search.Dock = DockStyle.Top;
        var gap = new Panel { Dock = DockStyle.Top, Height = 6 };
        _tree = new SensorTree(Font) { Dock = DockStyle.Fill, Pickable = true };
        _tree.SensorPicked += (item, group) =>
        {
            var key = AppConfig.SensorChartPrefix + item.Id;
            if (key != current && taken.Contains(key))
            {
                MessageBox.Show(this, L.T("Bu sensör zaten bir kartta gösteriliyor.", "This sensor is already on a card."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Pick(key, new SensorSample(item.Name, group, item.Type, double.NaN));
        };
        search.TextChanged += (_, _) => _tree.Filter = search.Text;

        body.Controls.Add(_tree);
        body.Controls.Add(gap);
        body.Controls.Add(search);
        body.Controls.Add(sensorsLabel);
        body.Controls.Add(builtIns);
        body.Controls.Add(builtInLabel);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = (int)(unit * 2.8) };
        var cancel = new FlatButton(L.T("Vazgeç", "Cancel"));
        cancel.Click += (_, _) => Close();
        bottom.Controls.Add(cancel);
        bottom.Layout += (_, _) => cancel.Location = new Point(bottom.Width - cancel.Width - unit / 2, (bottom.Height - cancel.Height) / 2);
        CancelButton = cancel;

        Controls.Add(body);
        Controls.Add(bottom);
        var shown = _app.LastSnapshot;
        _tree.SetData(_app.Sensors());
        _refresh.Tick += (_, _) =>
        {
            if (_app.LastSnapshot == shown) return;
            shown = _app.LastSnapshot;
            _tree.SetData(_app.Sensors());
        };
        Shown += (_, _) => { _refresh.Start(); search.Focus(); };
    }

    private void Pick(string key, SensorSample? sensor)
    {
        Chosen = key;
        ChosenSensor = sensor;
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) Close();
        base.OnKeyDown(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _refresh.Dispose();
        base.Dispose(disposing);
    }
}
