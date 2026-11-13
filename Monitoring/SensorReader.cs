using System.Runtime.InteropServices;
using System.Text;
using LibreHardwareMonitor.Hardware;
using SysWatch.Config;

namespace SysWatch.Monitoring;

public sealed class Snapshot
{
    public DateTime Time { get; init; }
    public Dictionary<Metric, double> Values { get; init; } = new();
    public List<string> Details { get; init; } = new();

    public double? Get(Metric m) => Values.TryGetValue(m, out var v) ? v : null;
}

public sealed record SensorItem(string Name, string Kind, string Value);

public sealed record SensorGroup(string Name, string Category, IReadOnlyList<SensorItem> Items);

public sealed class SensorReader : IDisposable
{
    private readonly object _gate = new();
    private Computer? _computer;
    private NativeGpu? _nativeGpu;
    private NativeDisk? _nativeDisk;
    private List<NativeGpu.Reading> _lastGpu = new();
    private List<NativeDisk.Reading> _lastDisk = new();

    public bool PawnIoInstalled { get; private set; }

    public void Open(HardwareConfig hw)
    {
        lock (_gate)
        {
            Close();
            try
            {
                PawnIoInstalled = LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled;
            }
            catch (Exception ex)
            {
                Log.Warn($"PawnIO durumu okunamadı: {ex.Message}");
            }

            // RAM comes from GlobalMemoryStatusEx; the Memory group (DIMM/SPD) stays off unless the user wants it.
            if (hw.Gpu && !hw.GpuDetailed) _nativeGpu = new NativeGpu();
            if (hw.Storage && !hw.StorageDetailed) _nativeDisk = new NativeDisk();

            _computer = new Computer
            {
                IsCpuEnabled = hw.Cpu,
                IsGpuEnabled = hw.Gpu && hw.GpuDetailed,
                IsStorageEnabled = hw.Storage && hw.StorageDetailed,
                IsMotherboardEnabled = hw.Motherboard,
                IsMemoryEnabled = hw.Memory,
                IsControllerEnabled = hw.Controller,
                IsNetworkEnabled = hw.Network,
                IsPsuEnabled = hw.Psu,
            };
            try
            {
                _computer.Open();
                Log.Info("Sensörler açıldı: " + string.Join(", ", _computer.Hardware.Select(h => $"{h.HardwareType}:{h.Name}")));
            }
            catch (Exception ex)
            {
                Log.Error("Sensörler açılamadı", ex);
            }
        }
    }

    public Snapshot Read()
    {
        var snap = new Snapshot { Time = DateTime.Now };
        lock (_gate)
        {
            if (_computer != null)
            {
                foreach (var hw in _computer.Hardware)
                {
                    try
                    {
                        hw.Update();
                        foreach (var sub in hw.SubHardware) sub.Update();
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"{hw.Name} okunamadı: {ex.Message}");
                    }
                }

                var cpus = Of(HardwareType.Cpu);
                var gpus = Of(HardwareType.GpuNvidia, HardwareType.GpuAmd, HardwareType.GpuIntel);
                var disks = Of(HardwareType.Storage);

                Put(snap, Metric.CpuTemp, cpus.Select(h => Pick(h, SensorType.Temperature,
                    "Core (Tctl/Tdie)", "CPU Package", "Core (Tctl)", "Core Max", "Core Average")).Max());
                Put(snap, Metric.CpuLoad, cpus.Select(h => Pick(h, SensorType.Load, "CPU Total")).Max());
                Put(snap, Metric.GpuTemp, gpus.Select(h => Pick(h, SensorType.Temperature, "GPU Core", "GPU Hot Spot")).Max());
                Put(snap, Metric.GpuLoad, gpus.Select(h => Pick(h, SensorType.Load, "GPU Core", "D3D 3D")).Max());
                Put(snap, Metric.DiskTemp, disks.Select(h => Pick(h, SensorType.Temperature, "Composite Temperature", "Temperature")).Max());

                foreach (var h in _computer.Hardware)
                {
                    var temp = Pick(h, SensorType.Temperature, "Core (Tctl/Tdie)", "CPU Package", "GPU Core", "Temperature");
                    var load = Pick(h, SensorType.Load, "CPU Total", "GPU Core", "D3D 3D");
                    if (temp == null && load == null) continue;
                    var sb = new StringBuilder(h.Name);
                    if (temp != null) sb.Append($" · {temp:0}°C");
                    if (load != null) sb.Append($" · %{load:0}");
                    snap.Details.Add(sb.ToString());
                }
            }

            if (_nativeDisk != null)
            {
                var disks = _lastDisk = _nativeDisk.Read();
                Put(snap, Metric.DiskTemp, disks.Max(d => d.Temperature));
                foreach (var d in disks.Where(d => d.Temperature != null))
                    snap.Details.Add($"{d.Name} · {d.Temperature:0}°C");
            }

            if (_nativeGpu != null)
            {
                var gpus = _lastGpu = _nativeGpu.Read();
                Put(snap, Metric.GpuTemp, gpus.Max(g => g.Temperature));
                Put(snap, Metric.GpuLoad, gpus.Max(g => g.Load));
                foreach (var g in gpus)
                {
                    if (g.Temperature == null && g.Load == null) continue;
                    var sb = new StringBuilder(g.Name);
                    if (g.Temperature != null) sb.Append($" · {g.Temperature:0}°C");
                    if (g.Load != null) sb.Append($" · %{g.Load:0}");
                    snap.Details.Add(sb.ToString());
                }
            }
        }

        var ram = RamPercent();
        if (ram != null) snap.Values[Metric.Ram] = ram.Value;
        return snap;
    }

    // Every sensor of every open hardware group, with the values from the last reading (no extra hardware access).
    public List<SensorGroup> AllSensors()
    {
        var groups = new List<SensorGroup>();
        lock (_gate)
        {
            if (_computer != null)
            {
                foreach (var hw in _computer.Hardware)
                {
                    var items = hw.Sensors.Concat(hw.SubHardware.SelectMany(sh => sh.Sensors))
                        .Where(sn => sn.Value.HasValue && !float.IsNaN(sn.Value.Value))
                        .OrderBy(sn => sn.SensorType).ThenBy(sn => sn.Index)
                        .Select(sn => new SensorItem(sn.Name, KindLabel(sn.SensorType), FormatValue(sn.SensorType, sn.Value!.Value)))
                        .ToList();
                    if (items.Count > 0) groups.Add(new SensorGroup(hw.Name, hw.HardwareType.ToString(), items));
                }
            }
            foreach (var gpu in _lastGpu)
            {
                var items = new List<SensorItem>();
                if (gpu.Temperature is { } t) items.Add(new SensorItem("GPU sıcaklığı", "Sıcaklık", $"{t:0.0} °C"));
                if (gpu.Load is { } l) items.Add(new SensorItem("GPU kullanımı (en yoğun motor)", "Yük", $"{l:0.0} %"));
                if (items.Count > 0) groups.Add(new SensorGroup(gpu.Name, "Gpu", items));
            }
            foreach (var disk in _lastDisk)
            {
                if (disk.Temperature is { } t) groups.Add(new SensorGroup(disk.Name, "Storage", [new SensorItem("Disk sıcaklığı", "Sıcaklık", $"{t:0} °C")]));
            }
        }
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (GlobalMemoryStatusEx(ref status) && status.TotalPhys > 0)
        {
            const double gb = 1024d * 1024 * 1024;
            groups.Add(new SensorGroup("Bellek (Windows)", "Memory",
            [
                new SensorItem("Kullanım", "Yük", $"{status.MemoryLoad} %"),
                new SensorItem("Kullanılan", "Veri", $"{(status.TotalPhys - status.AvailPhys) / gb:0.0} GB"),
                new SensorItem("Boş", "Veri", $"{status.AvailPhys / gb:0.0} GB"),
                new SensorItem("Toplam", "Veri", $"{status.TotalPhys / gb:0.0} GB"),
            ]));
        }
        return groups;
    }

    private static string KindLabel(SensorType t) => t switch
    {
        SensorType.Temperature => "Sıcaklık",
        SensorType.Load => "Yük",
        SensorType.Clock => "Saat hızı",
        SensorType.Power => "Güç",
        SensorType.Voltage => "Voltaj",
        SensorType.Current => "Akım",
        SensorType.Fan => "Fan",
        SensorType.Control => "Fan kontrolü",
        SensorType.Data or SensorType.SmallData => "Veri",
        SensorType.Throughput => "Aktarım",
        SensorType.Level => "Seviye",
        SensorType.Frequency => "Frekans",
        SensorType.Energy => "Enerji",
        SensorType.Factor => "Oran",
        _ => t.ToString(),
    };

    private static string FormatValue(SensorType t, float v) => t switch
    {
        SensorType.Temperature => $"{v:0.0} °C",
        SensorType.Load or SensorType.Control or SensorType.Level or SensorType.Humidity => $"{v:0.0} %",
        SensorType.Clock => $"{v:0} MHz",
        SensorType.Power => $"{v:0.0} W",
        SensorType.Voltage => $"{v:0.000} V",
        SensorType.Current => $"{v:0.00} A",
        SensorType.Fan => $"{v:0} RPM",
        SensorType.Data => $"{v:0.0} GB",
        SensorType.SmallData => $"{v:0} MB",
        SensorType.Throughput => v >= 1048576 ? $"{v / 1048576:0.0} MB/s" : $"{v / 1024:0.0} KB/s",
        SensorType.Frequency => $"{v:0} Hz",
        SensorType.Energy => $"{v:0} mWh",
        _ => $"{v:0.##}",
    };

    private IEnumerable<IHardware> Of(params HardwareType[] types) =>
        _computer!.Hardware.Where(h => types.Contains(h.HardwareType));

    private static void Put(Snapshot snap, Metric m, double? v)
    {
        if (v is { } value && !double.IsNaN(value)) snap.Values[m] = value;
    }

    // Preferred sensor names first; otherwise the highest value of that type.
    private static double? Pick(IHardware hw, SensorType type, params string[] preferred)
    {
        var sensors = hw.Sensors.Concat(hw.SubHardware.SelectMany(s => s.Sensors))
            .Where(s => s.SensorType == type && s.Value.HasValue && !float.IsNaN(s.Value.Value))
            // Without the PawnIO driver some temperatures read as 0 instead of being absent.
            .Where(s => type != SensorType.Temperature || s.Value is > 0 and < 150)
            .ToList();
        if (sensors.Count == 0) return null;
        foreach (var name in preferred)
        {
            var match = sensors.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match.Value;
        }
        return sensors.Max(s => s.Value);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    private static double? RamPercent()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhys == 0) return null;
        return 100.0 * (status.TotalPhys - status.AvailPhys) / status.TotalPhys;
    }

    private void Close()
    {
        try
        {
            _computer?.Close();
        }
        catch (Exception ex)
        {
            Log.Warn($"Sensörler kapatılırken hata: {ex.Message}");
        }
        _computer = null;
        _nativeGpu?.Dispose();
        _nativeGpu = null;
        _nativeDisk?.Dispose();
        _nativeDisk = null;
    }

    public void Dispose()
    {
        lock (_gate) Close();
    }
}
