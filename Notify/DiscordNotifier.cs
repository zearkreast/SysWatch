using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using SysWatch.Config;

namespace SysWatch.Notify;

public static class DiscordApi
{
    private const string Base = "https://discord.com/api/v10";
    private const int InvitePermissions = 1024 | 2048 | 65536; // View Channels, Send Messages, Read Message History

    private static HttpRequestMessage Bot(HttpRequestMessage req, string token)
    {
        req.Headers.Authorization = new AuthenticationHeaderValue("Bot", token);
        return req;
    }

    private static async Task<JsonNode?> Call(HttpRequestMessage req, CancellationToken ct)
    {
        try
        {
            return await Http.SendJsonAsync(req, TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode != null)
        {
            throw new HttpRequestException(Explain(ex), ex, ex.StatusCode);
        }
    }

    // Discord error codes: https://discord.com/developers/docs/topics/opcodes-and-status-codes#json
    public static string Explain(HttpRequestException ex)
    {
        var code = 0;
        var brace = ex.Message.IndexOf('{');
        if (brace >= 0)
        {
            try
            {
                code = JsonNode.Parse(ex.Message[brace..])?["code"]?.GetValue<int>() ?? 0;
            }
            catch (System.Text.Json.JsonException)
            {
            }
        }
        return code switch
        {
            50278 or 50007 => "Bot size DM gönderemiyor: botu hesabınıza eklemediniz (ya da yetkisini kaldırdınız) ve ortak bir sunucunuz yok. " +
                              "Ayarlar > Discord'da \"Hesabıma ekle\" ile botu hesabınıza ekleyin.",
            50001 => "Bot bu kanalı göremiyor. Botu sunucuya ekleyin ve kanalda \"Kanalı Görüntüle\" izni verin.",
            50013 => "Botun bu kanala mesaj gönderme izni yok (Mesaj Gönder / Mesaj Geçmişini Oku izinlerini verin).",
            10003 => "Kanal bulunamadı. Kanal ID'sini kontrol edin (kanala sağ tık > Kanal ID'sini Kopyala).",
            10013 => "Kullanıcı bulunamadı. İzinli kullanıcı ID'sini kontrol edin (adınıza sağ tık > Kullanıcı ID'sini Kopyala).",
            10015 => "Webhook bulunamadı ya da silinmiş. Webhook URL'sini yeniden kopyalayın.",
            50035 => "Discord girilen değeri kabul etmedi. Kanal ve kullanıcı ID'leri sadece rakamlardan oluşmalı.",
            _ when ex.StatusCode == System.Net.HttpStatusCode.Unauthorized => "Bot token geçersiz. Developer Portal > Bot > Reset Token ile yenisini alın.",
            _ when ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests => "Discord hız sınırına takıldı, biraz sonra tekrar deneyin.",
            _ => ex.Message,
        };
    }

    public static Task WebhookAsync(string url, string text, CancellationToken ct) =>
        Call(Http.Json(HttpMethod.Post, url,
            new { content = TelegramApi.Limit(text, 1900), allowed_mentions = new { parse = Array.Empty<string>() } }), ct);

    public static Task SendAsync(string token, string channelId, string text, CancellationToken ct) =>
        Call(Bot(Http.Json(HttpMethod.Post, $"{Base}/channels/{channelId}/messages",
            new { content = TelegramApi.Limit(text, 1900), allowed_mentions = new { parse = Array.Empty<string>() } }), token), ct);

    public static async Task<string> OpenDmAsync(string token, string userId, CancellationToken ct)
    {
        var node = await Call(Bot(Http.Json(HttpMethod.Post, $"{Base}/users/@me/channels",
            new { recipient_id = userId }), token), ct).ConfigureAwait(false);
        return node?["id"]?.GetValue<string>() ?? throw new InvalidOperationException("Discord DM kanalı açılamadı.");
    }

    public static async Task<string> ApplicationIdAsync(string token, CancellationToken ct)
    {
        var app = await Call(Bot(new HttpRequestMessage(HttpMethod.Get, $"{Base}/oauth2/applications/@me"), token), ct).ConfigureAwait(false);
        return app?["id"]?.GetValue<string>() ?? throw new InvalidOperationException("Uygulama ID'si alınamadı.");
    }

    // userInstall: "Add to My Apps" (works without a shared server); otherwise the bot joins a server.
    public static async Task<string> InviteUrlAsync(string token, bool userInstall, CancellationToken ct)
    {
        var id = await ApplicationIdAsync(token, ct).ConfigureAwait(false);
        return userInstall
            ? $"https://discord.com/oauth2/authorize?client_id={id}&integration_type=1&scope=applications.commands"
            : $"https://discord.com/oauth2/authorize?client_id={id}&integration_type=0&scope=bot+applications.commands&permissions={InvitePermissions}";
    }

    public static Task RegisterCommandsAsync(string token, string appId, JsonArray commands, CancellationToken ct) =>
        Call(Bot(new HttpRequestMessage(HttpMethod.Put, $"{Base}/applications/{appId}/commands")
        {
            Content = new StringContent(commands.ToJsonString(), System.Text.Encoding.UTF8, "application/json"),
        }, token), ct);

    private const int Ephemeral = 64;

    public static Task InteractionReplyAsync(string id, string token, string text, CancellationToken ct) =>
        Call(Http.Json(HttpMethod.Post, $"{Base}/interactions/{id}/{token}/callback",
            new { type = 4, data = new { content = TelegramApi.Limit(text, 1900), flags = Ephemeral } }), ct);

    public static Task InteractionDeferAsync(string id, string token, CancellationToken ct) =>
        Call(Http.Json(HttpMethod.Post, $"{Base}/interactions/{id}/{token}/callback",
            new { type = 5, data = new { flags = Ephemeral } }), ct);

    public static Task InteractionEditAsync(string appId, string token, string text, CancellationToken ct) =>
        Call(Http.Json(HttpMethod.Patch, $"{Base}/webhooks/{appId}/{token}/messages/@original",
            new { content = TelegramApi.Limit(text, 1900), allowed_mentions = new { parse = Array.Empty<string>() } }), ct);
}

public sealed class DiscordNotifier : INotifier
{
    private string? _dmChannel;
    private string? _dmFor;

    public string Name => "Discord";

    public bool IsEnabled(AppConfig cfg) => cfg.Discord.Mode switch
    {
        DiscordMode.Webhook => cfg.Discord.WebhookUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase),
        DiscordMode.Bot => cfg.Discord.BotToken.Length > 0 &&
                           (cfg.Discord.ChannelId.Length > 0 || cfg.Discord.AllowedUserId.Length > 0),
        _ => false,
    };

    public bool IsMinimal(AppConfig cfg) => cfg.Discord.Minimal;

    public async Task SendAsync(string text, AppConfig cfg, CancellationToken ct)
    {
        if (cfg.Discord.Mode == DiscordMode.Webhook)
        {
            await DiscordApi.WebhookAsync(cfg.Discord.WebhookUrl, text, ct).ConfigureAwait(false);
            return;
        }
        var channel = await ResolveChannelAsync(cfg.Discord, ct).ConfigureAwait(false);
        await DiscordApi.SendAsync(cfg.Discord.BotToken, channel, text, ct).ConfigureAwait(false);
    }

    // Without a channel ID the bot talks to the allowed user over DM.
    public async Task<string> ResolveChannelAsync(DiscordConfig d, CancellationToken ct)
    {
        if (d.ChannelId.Length > 0) return d.ChannelId;
        var key = d.AllowedUserId + "|" + d.BotToken.GetHashCode();
        if (_dmChannel == null || _dmFor != key)
        {
            _dmChannel = await DiscordApi.OpenDmAsync(d.BotToken, d.AllowedUserId, ct).ConfigureAwait(false);
            _dmFor = key;
        }
        return _dmChannel;
    }
}
