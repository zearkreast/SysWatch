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
        NotificationKind.Alert => Subject.Length > 0 ? $"SysWatch: {Subject} uyarısı var." : "SysWatch: PC'de uyarı var.",
        NotificationKind.Recovered => Subject.Length > 0 ? $"SysWatch: {Subject} normale döndü." : "SysWatch: Durum normale döndü.",
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
        Title = $"{MetricInfo.Label(rule.Metric)} yüksek{(repeat ? " (devam ediyor)" : "")}",
        Body = $"Şu an: {MetricInfo.Format(rule.Metric, value)} · Eşik: {MetricInfo.Format(rule.Metric, rule.Limit)}",
    };

    public static Notification Recovered(Rule rule, double value, double peak) => new()
    {
        Kind = NotificationKind.Recovered,
        Subject = MetricInfo.Label(rule.Metric),
        Title = $"{MetricInfo.Label(rule.Metric)} normale döndü",
        Body = $"Şu an: {MetricInfo.Format(rule.Metric, value)} · En yüksek: {MetricInfo.Format(rule.Metric, peak)}",
    };

    public static Notification Info(string title, string body = "") => new()
    {
        Kind = NotificationKind.Info,
        Title = title,
        Body = body,
    };
}
