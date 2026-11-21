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
    // The sensors charted on the status page, by SensorItem.Id.
    public Dictionary<string, SensorSample> Watched { get; init; } = new();

    public double? Get(Metric m) => Values.TryGetValue(m, out var v) ? v : null;
}

// Id stays the same across restarts (LHM's sensor identifier, or "native/..." for the Windows reads), so charts can be saved by it.
public sealed record SensorItem(string Id, string Name, string Type, string Value, string Min, string Max);

public sealed record SensorGroup(string Name, string Category, IReadOnlyList<SensorItem> Items);

public sealed record SensorSample(string Name, string Hardware, string Type, double Value);

public sealed class SensorReader : IDisposable
{
    private readonly object _gate = new();
    private Computer? _computer;
    private NativeGpu? _nativeGpu;
    private NativeDisk? _nativeDisk;
    private List<NativeGpu.Reading> _lastGpu = new();
    private List<NativeDisk.Reading> _lastDisk = new();
    private volatile string[] _watched = [];
    // Lowest and highest value since start for the Windows reads; LHM keeps its own.
    private readonly Dictionary<string, (double Min, double Max)> _range = new();

    public bool PawnIoInstalled { get; private set; }

    public void Watch(IEnumerable<string> ids) => _watched = ids.Distinct().ToArray();

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
                Log.Warn(L.T($"PawnIO durumu okunamadı: {ex.Message}", $"Couldn't read the PawnIO status: {ex.Message}"));
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
                Log.Info(L.T("Sensörler açıldı: ", "Sensors opened: ") + string.Join(", ", _computer.Hardware.Select(h => $"{h.HardwareType}:{h.Name}")));
            }
            catch (Exception ex)
            {
                Log.Error(L.T("Sensörler açılamadı", "Couldn't open the sensors"), ex);
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
                        Log.Warn(L.T($"{hw.Name} okunamadı: {ex.Message}", $"Couldn't read {hw.Name}: {ex.Message}"));
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
                    if (load != null) sb.Append(" · " + L.Percent(load.Value));
                    snap.Details.Add(sb.ToString());
                }
            }

            var native = new Dictionary<string, SensorSample>();
            void Track(string id, string name, string hardware, string type, double? value)
            {
                if (value is not { } v) return;
                native[id] = new SensorSample(name, hardware, type, v);
                _range[id] = _range.TryGetValue(id, out var r) ? (Math.Min(r.Min, v), Math.Max(r.Max, v)) : (v, v);
            }

            if (_nativeDisk != null)
            {
                var disks = _lastDisk = _nativeDisk.Read();
                Put(snap, Metric.DiskTemp, disks.Max(d => d.Temperature));
                foreach (var d in disks.Where(d => d.Temperature != null))
                {
                    snap.Details.Add($"{d.Name} · {d.Temperature:0}°C");
                    Track(DiskTempId(d), DiskTempName, d.Name, "Temperature", d.Temperature);
                }
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
                    if (g.Load != null) sb.Append(" · " + L.Percent(g.Load.Value));
                    snap.Details.Add(sb.ToString());
                    Track(GpuTempId(g), GpuTempName, g.Name, "Temperature", g.Temperature);
                    Track(GpuLoadId(g), GpuLoadName, g.Name, "Load", g.Load);
                }
            }

            if (ReadMemory() is { } mem)
            {
                snap.Values[Metric.Ram] = mem.Percent;
                Track(RamLoadId, RamLoadName, MemoryGroupName, "Load", mem.Percent);
                Track(RamUsedId, RamUsedName, MemoryGroupName, "Data", mem.UsedGb);
                Track(RamFreeId, RamFreeName, MemoryGroupName, "Data", mem.FreeGb);
            }

            FillWatched(snap, native);
        }
        return snap;
    }

    private void FillWatched(Snapshot snap, Dictionary<string, SensorSample> native)
    {
        var watched = _watched;
        if (watched.Length == 0) return;
        Dictionary<string, (ISensor Sensor, IHardware Hardware)>? lhm = null;
        foreach (var id in watched)
        {
            if (native.TryGetValue(id, out var sample))
            {
                snap.Watched[id] = sample;
                continue;
            }
            if (_computer == null || id.StartsWith("native/", StringComparison.Ordinal)) continue;
            lhm ??= _computer.Hardware
                .SelectMany(hw => hw.Sensors.Concat(hw.SubHardware.SelectMany(sh => sh.Sensors)).Select(sn => (Sensor: sn, Hardware: hw)))
                .GroupBy(p => p.Sensor.Identifier.ToString())
                .ToDictionary(g => g.Key, g => g.First());
            if (lhm.TryGetValue(id, out var p) && p.Sensor.Value is { } v && !float.IsNaN(v))
                snap.Watched[id] = new SensorSample(p.Sensor.Name, p.Hardware.Name, p.Sensor.SensorType.ToString(), v);
        }
    }

    private List<SensorGroup>? _allSensors;

    // Every sensor of every open hardware group, with the values from the last reading (no extra hardware access).
    // Called on the UI thread: while a reading holds the lock (a detailed-storage update can take hundreds of ms) it
    // returns the previous list instead of freezing the window.
    public List<SensorGroup> AllSensors()
    {
        if (_allSensors is not { } cached) Monitor.Enter(_gate);
        else if (!Monitor.TryEnter(_gate)) return cached;
        var groups = new List<SensorGroup>();
        try
        {
            if (_computer != null)
            {
                foreach (var hw in _computer.Hardware)
                {
                    var items = hw.Sensors.Concat(hw.SubHardware.SelectMany(sh => sh.Sensors))
                        .Where(sn => sn.Value.HasValue && !float.IsNaN(sn.Value.Value))
                        .OrderBy(sn => sn.SensorType).ThenBy(sn => sn.Index)
                        .Select(sn =>
                        {
                            var type = sn.SensorType.ToString();
                            return new SensorItem(sn.Identifier.ToString(), sn.Name, type, SensorFormat.Format(type, sn.Value!.Value),
                                sn.Min is { } lo ? SensorFormat.Format(type, lo) : "", sn.Max is { } hi ? SensorFormat.Format(type, hi) : "");
                        })
                        .ToList();
                    if (items.Count > 0) groups.Add(new SensorGroup(hw.Name, hw.HardwareType.ToString(), items));
                }
            }
            foreach (var gpu in _lastGpu)
            {
                var items = new List<SensorItem>();
                if (gpu.Temperature is { } t) items.Add(NativeItem(GpuTempId(gpu), GpuTempName, "Temperature", t));
                if (gpu.Load is { } l) items.Add(NativeItem(GpuLoadId(gpu), GpuLoadName, "Load", l));
                if (items.Count > 0) groups.Add(new SensorGroup(gpu.Name, "Gpu", items));
            }
            foreach (var disk in _lastDisk)
            {
                if (disk.Temperature is { } t) groups.Add(new SensorGroup(disk.Name, "Storage", [NativeItem(DiskTempId(disk), DiskTempName, "Temperature", t)]));
            }
            if (ReadMemory() is { } mem)
            {
                groups.Add(new SensorGroup(MemoryGroupName, "Memory",
                [
                    NativeItem(RamLoadId, RamLoadName, "Load", mem.Percent),
                    NativeItem(RamUsedId, RamUsedName, "Data", mem.UsedGb),
                    NativeItem(RamFreeId, RamFreeName, "Data", mem.FreeGb),
                    new SensorItem("native/ram/total", L.T("Toplam", "Total"), "Data", SensorFormat.Format("Data", mem.TotalGb), "", ""),
                ]));
            }
        }
        finally
        {
            Monitor.Exit(_gate);
        }
        return _allSensors = groups;
    }

    private SensorItem NativeItem(string id, string name, string type, double value)
    {
        var (min, max) = _range.TryGetValue(id, out var r) ? (SensorFormat.Format(type, r.Min), SensorFormat.Format(type, r.Max)) : ("", "");
        return new SensorItem(id, name, type, SensorFormat.Format(type, value), min, max);
    }

    private static string GpuTempId(NativeGpu.Reading g) => $"native/gpu/{g.Name}/temperature";
    private static string GpuLoadId(NativeGpu.Reading g) => $"native/gpu/{g.Name}/load";
    private static string DiskTempId(NativeDisk.Reading d) => $"native/disk/{d.Name}/temperature";
    private const string RamLoadId = "native/ram/load", RamUsedId = "native/ram/used", RamFreeId = "native/ram/available";
    private static string GpuTempName => L.T("GPU sıcaklığı", "GPU temperature");
    private static string GpuLoadName => L.T("GPU kullanımı (en yoğun motor)", "GPU usage (busiest engine)");
    private static string DiskTempName => L.T("Disk sıcaklığı", "Disk temperature");
    private static string MemoryGroupName => L.T("Bellek (Windows)", "Memory (Windows)");
    private static string RamLoadName => L.T("Kullanım", "Usage");
    private static string RamUsedName => L.T("Kullanılan", "Used");
    private static string RamFreeName => L.T("Boş", "Available");

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

    private static (double Percent, double UsedGb, double FreeGb, double TotalGb)? ReadMemory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhys == 0) return null;
        const double gb = 1024d * 1024 * 1024;
        var used = status.TotalPhys - status.AvailPhys;
        return (100.0 * used / status.TotalPhys, used / gb, status.AvailPhys / gb, status.TotalPhys / gb);
    }

    private void Close()
    {
        try
        {
            _computer?.Close();
        }
        catch (Exception ex)
        {
            Log.Warn(L.T($"Sensörler kapatılırken hata: {ex.Message}", $"Error while closing the sensors: {ex.Message}"));
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
