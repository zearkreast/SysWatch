using SysWatch.Config;
using SysWatch.Monitoring;

namespace SysWatch.Notify;

public enum NotificationKind { Alert, Recovered, Info }

public sealed class Notification
{
    public NotificationKind Kind { get; init; }
    public string Title { get; init; } = "";
    public string Body { get; init; } = "";
    // What the alert is about ("CPU sıcaklığı"); minimal mode names it but never sends values.
    public string Subject { get; init; } = "";

    public string MinimalText => Kind switch
    {
        NotificationKind.Alert => Subject.Length > 0 ? L.T($"SysWatch: {Subject} uyarısı var.", $"SysWatch: {Subject} alert.") : L.T("SysWatch: PC'de uyarı var.", "SysWatch: there is an alert on the PC."),
        NotificationKind.Recovered => Subject.Length > 0 ? L.T($"SysWatch: {Subject} normale döndü.", $"SysWatch: {Subject} is back to normal.") : L.T("SysWatch: Durum normale döndü.", "SysWatch: back to normal."),
        _ => $"SysWatch: {Title}",
    };

    public string FullText(bool includeHostname)
    {
        var host = includeHostname ? $" [{Environment.MachineName}]" : "";
        return $"SysWatch{host}: {Title}\n{Body}".TrimEnd();
    }

    public string TextFor(bool minimal, bool includeHostname) => minimal ? MinimalText : FullText(includeHostname);

    public static Notification Alert(Rule rule, double value, bool repeat) => new()
    {
        Kind = NotificationKind.Alert,
        Subject = MetricInfo.Label(rule.Metric),
        Title = L.T($"{MetricInfo.Label(rule.Metric)} yüksek{(repeat ? " (devam ediyor)" : "")}", $"{MetricInfo.Label(rule.Metric)} high{(repeat ? " (ongoing)" : "")}"),
        Body = L.T($"Şu an: {MetricInfo.Format(rule.Metric, value)} · Eşik: {MetricInfo.Format(rule.Metric, rule.Limit)}", $"Now: {MetricInfo.Format(rule.Metric, value)} · Threshold: {MetricInfo.Format(rule.Metric, rule.Limit)}"),
    };

    public static Notification Recovered(Rule rule, double value, double peak) => new()
    {
        Kind = NotificationKind.Recovered,
        Subject = MetricInfo.Label(rule.Metric),
        Title = L.T($"{MetricInfo.Label(rule.Metric)} normale döndü", $"{MetricInfo.Label(rule.Metric)} back to normal"),
        Body = L.T($"Şu an: {MetricInfo.Format(rule.Metric, value)} · En yüksek: {MetricInfo.Format(rule.Metric, peak)}", $"Now: {MetricInfo.Format(rule.Metric, value)} · Peak: {MetricInfo.Format(rule.Metric, peak)}"),
    };

    public static Notification Info(string title, string body = "") => new()
    {
        Kind = NotificationKind.Info,
        Title = title,
        Body = body,
    };
}
