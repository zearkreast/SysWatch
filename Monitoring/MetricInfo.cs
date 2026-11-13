using SysWatch.Config;

namespace SysWatch.Monitoring;

public static class MetricInfo
{
    public static string Key(Metric m) => m switch
    {
        Metric.CpuTemp => "cpu_temp",
        Metric.GpuTemp => "gpu_temp",
        Metric.CpuLoad => "cpu_load",
        Metric.GpuLoad => "gpu_load",
        Metric.Ram => "ram",
        Metric.DiskTemp => "disk_temp",
        _ => m.ToString(),
    };

    public static string Label(Metric m) => m switch
    {
        Metric.CpuTemp => L.T("CPU sıcaklığı", "CPU temperature"),
        Metric.GpuTemp => L.T("GPU sıcaklığı", "GPU temperature"),
        Metric.CpuLoad => L.T("CPU kullanımı", "CPU usage"),
        Metric.GpuLoad => L.T("GPU kullanımı", "GPU usage"),
        Metric.Ram => L.T("RAM kullanımı", "RAM usage"),
        Metric.DiskTemp => L.T("Disk sıcaklığı", "Disk temperature"),
        _ => m.ToString(),
    };

    public static string Unit(Metric m) => m is Metric.CpuTemp or Metric.GpuTemp or Metric.DiskTemp ? "°C" : "%";

    public static string Format(Metric m, double v) => $"{v:0}{Unit(m)}";

    public static bool TryParse(string key, out Metric metric)
    {
        foreach (var m in Enum.GetValues<Metric>())
        {
            if (string.Equals(Key(m), key, StringComparison.OrdinalIgnoreCase))
            {
                metric = m;
                return true;
            }
        }
        metric = default;
        return false;
    }
}
