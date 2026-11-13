using SysWatch.Config;

namespace SysWatch.Notify;

public sealed class LocalNotifier(Action<string, string> showBalloon) : INotifier
{
    public string Name => "Windows";
    public bool IsEnabled(AppConfig cfg) => cfg.Local.Enabled;
    public bool IsMinimal(AppConfig cfg) => false;

    public Task SendAsync(string text, AppConfig cfg, CancellationToken ct)
    {
        var lines = text.Split('\n', 2);
        showBalloon(lines[0], lines.Length > 1 ? lines[1] : "");
        return Task.CompletedTask;
    }
}

public sealed class NotifierHub(IReadOnlyList<INotifier> notifiers)
{
    public IReadOnlyList<INotifier> Notifiers => notifiers;

    public DateTime MutedUntil { get; set; } = DateTime.MinValue;
    public bool Muted => DateTime.Now < MutedUntil;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime Time, string? Error)> _lastResult = new();

    // Last delivery attempt per channel, for the status dots in settings.
    public (DateTime Time, string? Error)? LastResult(string notifier) =>
        _lastResult.TryGetValue(notifier, out var r) ? r : null;

    public async Task BroadcastAsync(Notification n, AppConfig cfg, bool ignoreMute = false)
    {
        if (Muted && !ignoreMute) return;
        var tasks = notifiers.Where(x => x.IsEnabled(cfg)).Select(x => SendToAsync(x, n, cfg));
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public async Task<string?> SendToAsync(INotifier notifier, Notification n, AppConfig cfg)
    {
        try
        {
            var text = n.TextFor(notifier.IsMinimal(cfg), cfg.IncludeHostname);
            await notifier.SendAsync(text, cfg, CancellationToken.None).ConfigureAwait(false);
            _lastResult[notifier.Name] = (DateTime.Now, null);
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(L.T($"{notifier.Name} gönderimi başarısız", $"{notifier.Name} delivery failed"), ex);
            _lastResult[notifier.Name] = (DateTime.Now, ex.Message);
            return ex.Message;
        }
    }
}
