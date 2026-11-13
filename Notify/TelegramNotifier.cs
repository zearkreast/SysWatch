using System.Text.Json.Nodes;
using SysWatch.Config;

namespace SysWatch.Notify;

public static class TelegramApi
{
    private static string Url(string token, string method) => $"https://api.telegram.org/bot{token}/{method}";

    public static Task SendMessageAsync(string token, long chatId, string text, CancellationToken ct) =>
        Http.SendJsonAsync(Http.Json(HttpMethod.Post, Url(token, "sendMessage"),
            new { chat_id = chatId, text = Limit(text, 4000), disable_web_page_preview = true }),
            TimeSpan.FromSeconds(20), ct);

    public static async Task<JsonArray> GetUpdatesAsync(string token, long offset, int timeoutSec, CancellationToken ct)
    {
        var url = Url(token, "getUpdates") + $"?timeout={timeoutSec}&offset={offset}&allowed_updates=%5B%22message%22%5D";
        var node = await Http.SendJsonAsync(new HttpRequestMessage(HttpMethod.Get, url),
            TimeSpan.FromSeconds(timeoutSec + 15), ct).ConfigureAwait(false);
        return node?["result"] as JsonArray ?? new JsonArray();
    }

    internal static string Limit(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}

public sealed class TelegramNotifier : INotifier
{
    public string Name => "Telegram";

    public bool IsEnabled(AppConfig cfg) =>
        cfg.Telegram.Mode != TelegramMode.Off && cfg.Telegram.BotToken.Length > 0 && cfg.Telegram.ChatId != 0;

    public bool IsMinimal(AppConfig cfg) => cfg.Telegram.Minimal;

    public Task SendAsync(string text, AppConfig cfg, CancellationToken ct) =>
        TelegramApi.SendMessageAsync(cfg.Telegram.BotToken, cfg.Telegram.ChatId, text, ct);
}
