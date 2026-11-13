using System.Net;
using System.Text.Json.Nodes;
using SysWatch.Config;
using SysWatch.Notify;

namespace SysWatch.Commands;

public abstract class Poller : IDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _task;

    protected static readonly TimeSpan MaxCommandAge = TimeSpan.FromMinutes(2);

    public abstract string Name { get; }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _task = Task.Run(() => LoopAsync(ct), ct);
        Log.Info(L.T($"{Name} komut dinleyicisi başladı.", $"{Name} command listener started."));
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        var backoff = TimeSpan.FromSeconds(5);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunAsync(ct).ConfigureAwait(false);
                backoff = TimeSpan.FromSeconds(5);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                var wait = ex is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests }
                    ? TimeSpan.FromSeconds(30) : backoff;
                Log.Warn(L.T($"{Name} dinleyici hatası, {wait.TotalSeconds:0} sn sonra yeniden denenecek: {ex.Message}", $"{Name} listener error, retrying in {wait.TotalSeconds:0} s: {ex.Message}"));
                try
                {
                    await Task.Delay(wait, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, 300));
            }
        }
    }

    // Runs until cancelled or until it throws; the base loop restarts it with backoff.
    protected abstract Task RunAsync(CancellationToken ct);

    public void Dispose()
    {
        try
        {
            _cts?.Cancel();
            _task?.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
        }
        _cts?.Dispose();
        _cts = null;
    }
}

public sealed class TelegramPoller(IAppContext app, CommandRouter router) : Poller
{
    private long _offset;
    private bool _primed;

    public override string Name => "Telegram";

    protected override async Task RunAsync(CancellationToken ct)
    {
        var token = app.Config.Telegram.BotToken;

        // Drop the backlog on start so commands sent while the PC was off never replay.
        if (!_primed)
        {
            var backlog = await TelegramApi.GetUpdatesAsync(token, -1, 0, ct).ConfigureAwait(false);
            if (backlog.Count > 0) _offset = backlog[^1]!["update_id"]!.GetValue<long>() + 1;
            _primed = true;
        }

        while (!ct.IsCancellationRequested)
        {
            var updates = await TelegramApi.GetUpdatesAsync(token, _offset, 50, ct).ConfigureAwait(false);
            foreach (var u in updates)
            {
                _offset = u!["update_id"]!.GetValue<long>() + 1;
                var msg = u["message"];
                var text = msg?["text"]?.GetValue<string>();
                var chatId = msg?["chat"]?["id"]?.GetValue<long>() ?? 0;
                var date = DateTimeOffset.FromUnixTimeSeconds(msg?["date"]?.GetValue<long>() ?? 0);
                if (!CommandRouter.LooksLikeCommand(text)) continue;

                var cfg = app.Config;
                if (cfg.Telegram.Mode != TelegramMode.NotifyAndCommands) continue;
                if (chatId != cfg.Telegram.ChatId || msg?["chat"]?["type"]?.GetValue<string>() != "private")
                {
                    Log.Warn(L.T($"Telegram: izinsiz sohbetten komut yok sayıldı (chat {chatId}).", $"Telegram: ignored a command from a chat that isn't allowed (chat {chatId})."));
                    continue;
                }
                if (DateTimeOffset.UtcNow - date > MaxCommandAge) continue;

                var reply = await router.HandleAsync(text!, Name, cfg.Telegram.Minimal).ConfigureAwait(false);
                await TelegramApi.SendMessageAsync(token, chatId, reply, ct).ConfigureAwait(false);
            }
        }
    }
}

public sealed class SignalListener(IAppContext app, CommandRouter router) : Poller
{
    public override string Name => "Signal";

    protected override async Task RunAsync(CancellationToken ct)
    {
        var s = app.Config.Signal;
        var req = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{s.DaemonPort}/api/v1/events");
        req.Headers.Accept.ParseAdd("text/event-stream");
        using var res = await Http.Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        using var reader = new StreamReader(await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false));

        var data = new System.Text.StringBuilder();
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line == null) throw new IOException(L.T("signal-cli olay akışı kapandı.", "The signal-cli event stream closed."));
            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                data.Append(line.AsSpan(5).Trim());
                continue;
            }
            if (line.Length == 0 && data.Length > 0)
            {
                var payload = data.ToString();
                data.Clear();
                await HandleEventAsync(payload, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleEventAsync(string payload, CancellationToken ct)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(payload);
        }
        catch (System.Text.Json.JsonException)
        {
            return;
        }
        var env = (root?["params"] ?? root)?["envelope"];
        if (env == null) return;

        var cfg = app.Config;
        var s = cfg.Signal;
        if (s.Mode != SignalMode.NotifyAndCommands) return;

        string? text = null;
        var source = env["sourceNumber"]?.ToString() ?? env["source"]?.ToString();
        if (env["dataMessage"]?["message"] is JsonNode dm && !s.NoteToSelf && source == s.Recipient)
        {
            text = dm.ToString();
        }
        else if (env["syncMessage"]?["sentMessage"] is JsonNode sent && s.NoteToSelf)
        {
            var dest = sent["destinationNumber"]?.ToString() ?? sent["destination"]?.ToString();
            if (dest == s.Account && source == s.Account) text = sent["message"]?.ToString();
        }

        if (!CommandRouter.LooksLikeCommand(text))
        {
            if (env["dataMessage"]?["message"] != null && source != s.Recipient && source != s.Account)
                Log.Warn(L.T("Signal: izinsiz numaradan mesaj yok sayıldı.", "Signal: ignored a message from a number that isn't allowed."));
            return;
        }

        var ts = DateTimeOffset.FromUnixTimeMilliseconds(env["timestamp"]?.GetValue<long>() ?? 0);
        if (DateTimeOffset.UtcNow - ts > MaxCommandAge) return;

        var reply = await router.HandleAsync(text!, Name, s.Minimal).ConfigureAwait(false);
        await SignalCli.RpcSendAsync(s, reply, ct).ConfigureAwait(false);
    }
}
