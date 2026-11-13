using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using SysWatch.Notify;

namespace SysWatch.Commands;

// Minimal Discord Gateway client: one WebSocket, a heartbeat every ~41 s, and only INTERACTION_CREATE is handled.
// Interactions need no gateway intents, so the bot asks for none (no Message Content intent either).
public sealed class DiscordGateway(IAppContext app, CommandRouter router) : Poller
{
    private const string GatewayUrl = "wss://gateway.discord.gg/?v=10&encoding=json";
    private string? _appId;
    private bool _registered;

    public override string Name => "Discord";

    protected override async Task RunAsync(CancellationToken ct)
    {
        var d = app.Config.Discord;
        if (d.AllowedUserId.Length == 0)
        {
            Log.Warn(L.T("Discord: izinli kullanıcı ID'si boş, komutlar dinlenmiyor.", "Discord: the allowed user ID is empty, so commands are not handled."));
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        }

        _appId ??= await DiscordApi.ApplicationIdAsync(d.BotToken, ct).ConfigureAwait(false);
        if (!_registered)
        {
            await DiscordApi.RegisterCommandsAsync(d.BotToken, _appId, DiscordCommands.Definitions(), ct).ConfigureAwait(false);
            _registered = true;
            Log.Info(L.T("Discord slash komutları kaydedildi.", "Discord slash commands registered."));
        }

        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri(GatewayUrl), ct).ConfigureAwait(false);
        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        long? seq = null;
        Task? heartbeat = null;

        try
        {
            while (!sessionCts.IsCancellationRequested)
            {
                var msg = await ReceiveAsync(ws, sessionCts.Token).ConfigureAwait(false)
                          ?? throw new IOException(L.T($"Discord bağlantısı kapandı ({ws.CloseStatus} {ws.CloseStatusDescription}).", $"Discord connection closed ({ws.CloseStatus} {ws.CloseStatusDescription})."));
                if (msg["s"] is JsonValue s) seq = s.GetValue<long>();

                switch (msg["op"]?.GetValue<int>())
                {
                    case 10: // Hello
                        var interval = msg["d"]!["heartbeat_interval"]!.GetValue<int>();
                        heartbeat = HeartbeatAsync(ws, interval, () => seq, sessionCts.Token);
                        await SendAsync(ws, new JsonObject
                        {
                            ["op"] = 2,
                            ["d"] = new JsonObject
                            {
                                ["token"] = d.BotToken,
                                ["intents"] = 0,
                                ["properties"] = new JsonObject { ["os"] = "windows", ["browser"] = "SysWatch", ["device"] = "SysWatch" },
                            },
                        }, sessionCts.Token).ConfigureAwait(false);
                        break;
                    case 1: // server asks for an immediate heartbeat
                        await SendAsync(ws, new JsonObject { ["op"] = 1, ["d"] = seq }, sessionCts.Token).ConfigureAwait(false);
                        break;
                    case 7: // Reconnect
                    case 9: // Invalid session
                        throw new IOException(L.T("Discord yeniden bağlanma istedi.", "Discord asked to reconnect."));
                    case 0 when msg["t"]?.GetValue<string>() == "READY":
                        Log.Info(L.T("Discord bağlantısı hazır.", "Discord connection ready."));
                        break;
                    case 0 when msg["t"]?.GetValue<string>() == "INTERACTION_CREATE":
                        _ = HandleInteractionAsync(msg["d"]!, ct);
                        break;
                }
            }
        }
        finally
        {
            sessionCts.Cancel();
            if (heartbeat != null)
            {
                try
                {
                    await heartbeat.ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException)
                {
                }
            }
            if (ws.State == WebSocketState.Open)
            {
                try
                {
                    await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None).ConfigureAwait(false);
                }
                catch (WebSocketException)
                {
                }
            }
        }
    }

    private async Task HandleInteractionAsync(JsonNode interaction, CancellationToken ct)
    {
        var id = interaction["id"]!.GetValue<string>();
        var token = interaction["token"]!.GetValue<string>();
        try
        {
            if (interaction["type"]?.GetValue<int>() != 2) return; // APPLICATION_COMMAND
            var cfg = app.Config;
            var userId = (interaction["member"]?["user"] ?? interaction["user"])?["id"]?.GetValue<string>();
            if (userId != cfg.Discord.AllowedUserId)
            {
                Log.Warn(L.T("Discord: izinsiz kullanıcıdan komut yok sayıldı.", "Discord: ignored a command from a user who isn't allowed."));
                await DiscordApi.InteractionReplyAsync(id, token, L.T("Bu bilgisayarı yönetme yetkiniz yok.", "You aren't allowed to control this computer."), ct).ConfigureAwait(false);
                return;
            }

            // Deferred, ephemeral: the 3 s answer window is met even for /top, and only the user sees the reply.
            await DiscordApi.InteractionDeferAsync(id, token, ct).ConfigureAwait(false);
            var text = DiscordCommands.ToText(interaction["data"]!);
            var reply = await router.HandleAsync(text, Name, cfg.Discord.Minimal).ConfigureAwait(false);
            await DiscordApi.InteractionEditAsync(_appId!, token, reply, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(L.T("Discord komutu işlenemedi", "Couldn't handle a Discord command"), ex);
        }
    }

    private static async Task HeartbeatAsync(ClientWebSocket ws, int intervalMs, Func<long?> seq, CancellationToken ct)
    {
        await Task.Delay(Random.Shared.Next(intervalMs), ct).ConfigureAwait(false);
        while (!ct.IsCancellationRequested)
        {
            await SendAsync(ws, new JsonObject { ["op"] = 1, ["d"] = seq() }, ct).ConfigureAwait(false);
            await Task.Delay(intervalMs, ct).ConfigureAwait(false);
        }
    }

    private static readonly SemaphoreSlim SendLock = new(1, 1);

    private static async Task SendAsync(ClientWebSocket ws, JsonObject payload, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(payload.ToJsonString());
        await SendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        }
        finally
        {
            SendLock.Release();
        }
    }

    private static async Task<JsonNode?> ReceiveAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var ms = new MemoryStream();
        while (true)
        {
            var result = await ws.ReceiveAsync(buffer, ct).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) break;
        }
        return JsonNode.Parse(ms.ToArray());
    }
}

public static class DiscordCommands
{
    private const int SubString = 3, SubInteger = 4;

    // English names and descriptions, with Turkish ones that Discord shows to clients set to Turkish.
    // Interactions always arrive with the English option names.
    public static JsonArray Definitions()
    {
        static JsonObject Tr(string text) => new() { ["tr"] = text };

        static JsonObject Opt(int type, string name, string trName, string desc, string trDesc, bool required = false, JsonArray? choices = null)
        {
            var o = new JsonObject
            {
                ["type"] = type, ["name"] = name, ["name_localizations"] = Tr(trName),
                ["description"] = desc, ["description_localizations"] = Tr(trDesc), ["required"] = required,
            };
            if (choices != null) o["choices"] = choices;
            return o;
        }

        static JsonObject Cmd(string name, string desc, string trDesc, params JsonObject[] options) => new()
        {
            ["name"] = name,
            ["description"] = desc,
            ["description_localizations"] = Tr(trDesc),
            ["type"] = 1,
            ["integration_types"] = new JsonArray(0, 1),
            ["contexts"] = new JsonArray(0, 1, 2),
            ["options"] = new JsonArray(options.Select(o => (JsonNode)o).ToArray()),
        };

        var metrics = new JsonArray();
        foreach (var (key, en, tr) in new[]
                 {
                     ("cpu_temp", "CPU temperature", "CPU sıcaklığı"), ("gpu_temp", "GPU temperature", "GPU sıcaklığı"),
                     ("cpu_load", "CPU usage", "CPU kullanımı"), ("gpu_load", "GPU usage", "GPU kullanımı"),
                     ("ram", "RAM usage", "RAM kullanımı"), ("disk_temp", "Disk temperature", "Disk sıcaklığı"),
                 })
            metrics.Add(new JsonObject { ["name"] = en, ["name_localizations"] = Tr(tr), ["value"] = key });

        var pin = Opt(SubString, "pin", "pin", "Security PIN (if enabled)", "Güvenlik PIN'i (açıksa)");
        return
        [
            Cmd("status", "SysWatch: current temperatures and usage", "SysWatch: anlık sıcaklık ve kullanım değerleri"),
            Cmd("top", "SysWatch: processes using the most CPU/RAM", "SysWatch: en çok CPU/RAM kullanan işlemler"),
            Cmd("kill", "SysWatch: ends a process", "SysWatch: bir işlemi sonlandırır",
                Opt(SubString, "target", "hedef", "Process name or PID", "İşlem adı veya PID", true), pin.DeepClone().AsObject()),
            Cmd("shutdown", "SysWatch: shuts the PC down (asks for confirmation)", "SysWatch: bilgisayarı kapatır (onay ister)",
                Opt(SubInteger, "minutes", "dakika", "In how many minutes (default 1)", "Kaç dakika sonra (varsayılan 1)"), pin.DeepClone().AsObject()),
            Cmd("confirm", "SysWatch: confirms the pending shutdown", "SysWatch: bekleyen kapatmayı onaylar"),
            Cmd("cancel", "SysWatch: cancels the scheduled shutdown", "SysWatch: planlanmış kapatmayı iptal eder"),
            Cmd("thresholds", "SysWatch: shows the thresholds", "SysWatch: eşikleri gösterir"),
            Cmd("set", "SysWatch: changes a threshold", "SysWatch: eşik değiştirir",
                Opt(SubString, "metric", "metrik", "Which reading", "Hangi değer", true, metrics),
                Opt(SubString, "value", "deger", "A number, or on/off", "Sayı ya da on/off", true)),
            Cmd("mute", "SysWatch: mutes notifications", "SysWatch: bildirimleri susturur",
                Opt(SubInteger, "minutes", "dakika", "For how many minutes (default 60)", "Kaç dakika (varsayılan 60)")),
            Cmd("unmute", "SysWatch: turns notifications back on", "SysWatch: bildirimleri açar"),
        ];
    }

    // Turns a slash command into the same text form the other channels use, e.g. "/kill chrome 1234".
    public static string ToText(JsonNode data)
    {
        var name = data["name"]!.GetValue<string>();
        var options = data["options"] as JsonArray ?? new JsonArray();
        string? Get(string key) => options.FirstOrDefault(o => o?["name"]?.GetValue<string>() == key)?["value"]?.ToString();
        var parts = new List<string> { "/" + name };
        foreach (var key in name switch
                 {
                     "kill" => new[] { "target", "pin" },
                     "shutdown" => new[] { "minutes", "pin" },
                     "set" => new[] { "metric", "value" },
                     "mute" => new[] { "minutes" },
                     _ => Array.Empty<string>(),
                 })
        {
            var v = Get(key);
            if (!string.IsNullOrWhiteSpace(v)) parts.Add(v.Trim().Replace(' ', '_'));
        }
        // /shutdown with only a PIN would read the PIN as minutes; give the default explicitly.
        if (name == "shutdown" && Get("minutes") == null && Get("pin") != null) parts.Insert(1, "1");
        return string.Join(' ', parts);
    }
}
