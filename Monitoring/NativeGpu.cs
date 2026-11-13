using System.Runtime.InteropServices;

namespace SysWatch.Monitoring;

// GPU temperature and load through the WDDM kernel thunks (gdi32 D3DKMT*), the same source Task Manager uses.
// Unlike LibreHardwareMonitor's GPU group this loads no vendor libraries (nvml/nvapi/ADL), which saves ~40 MB.
// Struct offsets match the Windows SDK layouts (verified against the CsWin32 definitions LHM ships).
public sealed class NativeGpu : IDisposable
{
    public sealed record Reading(string Name, double? Temperature, double? Load);

    private sealed class Adapter
    {
        public uint Handle;
        public long Luid;
        public string Name = "";
        public uint NodeCount;
        public long[] LastRunning = [];
        public long LastStamp;
    }

    private const int KmtqaiRegistryInfo = 8;
    private const int KmtqaiAdapterType = 15;
    private const int KmtqaiAdapterPerfData = 62;
    private const int StatsAdapter = 0;
    private const int StatsNode = 5;
    private const int StatsSize = 808;
    private const int StatsResult = 0x18;
    private const int StatsQueryArg = 0x320;
    private const int PerfDataSize = 64;
    private const int PerfDataTemperature = 0x38;
    private const int SoftwareDeviceBit = 1 << 2;

    private readonly List<Adapter> _adapters = new();
    private readonly IntPtr _stats = Marshal.AllocHGlobal(StatsSize);
    private readonly IntPtr _perf = Marshal.AllocHGlobal(PerfDataSize);

    public int AdapterCount => _adapters.Count;

    public NativeGpu()
    {
        try
        {
            Enumerate();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Warn(L.T($"Yerel GPU arayüzü kullanılamıyor: {ex.Message}", $"Native GPU interface unavailable: {ex.Message}"));
        }
    }

    private void Enumerate()
    {
        var enumArgs = new EnumAdapters2();
        if (D3DKMTEnumAdapters2(ref enumArgs) != 0 || enumArgs.NumAdapters == 0) return;
        var infos = new AdapterInfo[enumArgs.NumAdapters];
        var pin = GCHandle.Alloc(infos, GCHandleType.Pinned);
        try
        {
            enumArgs.pAdapters = pin.AddrOfPinnedObject();
            if (D3DKMTEnumAdapters2(ref enumArgs) != 0) return;
        }
        finally
        {
            pin.Free();
        }

        for (var i = 0; i < enumArgs.NumAdapters; i++)
        {
            var info = infos[i];
            var luid = ((long)info.LuidHigh << 32) | info.LuidLow;
            var type = 0u;
            if (QueryInfo(info.hAdapter, KmtqaiAdapterType, ref type) && (type & SoftwareDeviceBit) != 0)
            {
                Close(info.hAdapter);
                continue;
            }

            var adapter = new Adapter { Handle = info.hAdapter, Luid = luid, Name = AdapterName(info.hAdapter) };
            if (QueryStats(StatsAdapter, luid, 0))
                adapter.NodeCount = (uint)Marshal.ReadInt32(_stats, StatsResult + 4);
            adapter.LastRunning = new long[adapter.NodeCount];
            _adapters.Add(adapter);
        }
        Log.Info(L.T("Yerel GPU arayüzü: ", "Native GPU interface: ") + string.Join(", ", _adapters.Select(a => L.T($"{a.Name} ({a.NodeCount} motor)", $"{a.Name} ({a.NodeCount} engines)"))));
    }

    public List<Reading> Read()
    {
        var output = new List<Reading>(_adapters.Count);
        var now = DateTime.UtcNow.Ticks;
        foreach (var a in _adapters)
        {
            double? temp = null;
            Zero(_perf, PerfDataSize);
            if (QueryRaw(a.Handle, KmtqaiAdapterPerfData, _perf, PerfDataSize))
            {
                var deci = Marshal.ReadInt32(_perf, PerfDataTemperature);
                if (deci is > 0 and < 1500) temp = deci / 10.0;
            }

            double? load = null;
            var elapsed = now - a.LastStamp;
            var busiest = 0.0;
            var any = false;
            for (uint n = 0; n < a.NodeCount; n++)
            {
                if (!QueryStats(StatsNode, a.Luid, n)) continue;
                var running = Marshal.ReadInt64(_stats, StatsResult);
                if (a.LastStamp != 0 && elapsed > 0 && running >= a.LastRunning[n])
                {
                    busiest = Math.Max(busiest, (running - a.LastRunning[n]) * 100.0 / elapsed);
                    any = true;
                }
                a.LastRunning[n] = running;
            }
            if (any) load = Math.Min(100, busiest);
            a.LastStamp = now;
            output.Add(new Reading(a.Name, temp, load));
        }
        return output;
    }

    private bool QueryStats(int type, long luid, uint arg)
    {
        Zero(_stats, StatsSize);
        Marshal.WriteInt32(_stats, 0, type);
        Marshal.WriteInt32(_stats, 4, (int)(luid & 0xFFFFFFFF));
        Marshal.WriteInt32(_stats, 8, (int)(luid >> 32));
        Marshal.WriteInt32(_stats, StatsQueryArg, (int)arg);
        return D3DKMTQueryStatistics(_stats) == 0;
    }

    private static bool QueryInfo(uint handle, int type, ref uint value)
    {
        var buf = Marshal.AllocHGlobal(4);
        try
        {
            Marshal.WriteInt32(buf, 0);
            if (!QueryRaw(handle, type, buf, 4)) return false;
            value = (uint)Marshal.ReadInt32(buf);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private static string AdapterName(uint handle)
    {
        const int size = 4 * 260 * 2;
        var buf = Marshal.AllocHGlobal(size);
        try
        {
            Zero(buf, size);
            return QueryRaw(handle, KmtqaiRegistryInfo, buf, size) ? Marshal.PtrToStringUni(buf)?.Trim() ?? "GPU" : "GPU";
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private static readonly byte[] Zeros = new byte[4 * 260 * 2];

    private static void Zero(IntPtr ptr, int size) => Marshal.Copy(Zeros, 0, ptr, size);

    private static bool QueryRaw(uint handle, int type, IntPtr data, int size)
    {
        var q = new QueryAdapterInfo { hAdapter = handle, Type = type, pPrivateDriverData = data, PrivateDriverDataSize = (uint)size };
        return D3DKMTQueryAdapterInfo(ref q) == 0;
    }

    private static void Close(uint handle)
    {
        var c = new CloseAdapter { hAdapter = handle };
        D3DKMTCloseAdapter(ref c);
    }

    public void Dispose()
    {
        foreach (var a in _adapters) Close(a.Handle);
        _adapters.Clear();
        Marshal.FreeHGlobal(_stats);
        Marshal.FreeHGlobal(_perf);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EnumAdapters2
    {
        public uint NumAdapters;
        public IntPtr pAdapters;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdapterInfo
    {
        public uint hAdapter;
        public uint LuidLow;
        public int LuidHigh;
        public uint NumOfSources;
        public int bPrecisePresentRegionsPreferred;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct QueryAdapterInfo
    {
        public uint hAdapter;
        public int Type;
        public IntPtr pPrivateDriverData;
        public uint PrivateDriverDataSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CloseAdapter
    {
        public uint hAdapter;
    }

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTEnumAdapters2(ref EnumAdapters2 args);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTQueryAdapterInfo(ref QueryAdapterInfo args);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTQueryStatistics(IntPtr args);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTCloseAdapter(ref CloseAdapter args);
}
