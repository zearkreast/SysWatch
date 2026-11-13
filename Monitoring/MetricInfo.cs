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
        Metric.CpuTemp => "CPU sıcaklığı",
        Metric.GpuTemp => "GPU sıcaklığı",
        Metric.CpuLoad => "CPU kullanımı",
        Metric.GpuLoad => "GPU kullanımı",
        Metric.Ram => "RAM kullanımı",
        Metric.DiskTemp => "Disk sıcaklığı",
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
