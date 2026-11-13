using SysWatch.Config;

namespace SysWatch.Notify;

public interface INotifier
{
    string Name { get; }
    bool IsEnabled(AppConfig cfg);
    bool IsMinimal(AppConfig cfg);
    Task SendAsync(string text, AppConfig cfg, CancellationToken ct);
}
