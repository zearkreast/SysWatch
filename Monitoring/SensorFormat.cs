namespace SysWatch.Monitoring;

// Number and unit for a sensor reading; the type is LibreHardwareMonitor's SensorType name.
public static class SensorFormat
{
    public static (string Number, string Unit) Split(string type, double v) => type switch
    {
        "Temperature" => ($"{v:0.0}", "°C"),
        "Load" or "Control" or "Level" or "Humidity" => ($"{v:0.0}", "%"),
        "Clock" => ($"{v:0}", "MHz"),
        "Power" => ($"{v:0.0}", "W"),
        "Voltage" => ($"{v:0.000}", "V"),
        "Current" => ($"{v:0.00}", "A"),
        "Fan" => ($"{v:0}", "RPM"),
        "Data" => ($"{v:0.0}", "GB"),
        "SmallData" => ($"{v:0}", "MB"),
        "Throughput" => v >= 1048576 ? ($"{v / 1048576:0.0}", "MB/s") : ($"{v / 1024:0.0}", "KB/s"),
        "Frequency" => ($"{v:0}", "Hz"),
        "Energy" => ($"{v:0}", "mWh"),
        _ => ($"{v:0.##}", ""),
    };

    public static string Format(string type, double v)
    {
        var (number, unit) = Split(type, v);
        return unit.Length == 0 ? number : $"{number} {unit}";
    }
}
