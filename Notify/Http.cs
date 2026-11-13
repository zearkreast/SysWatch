using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SysWatch.Notify;

public static class Http
{
    public static HttpClient Client { get; } = Create();

    private static HttpClient Create()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        };
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SysWatch/1.0");
        return client;
    }

    public static async Task<JsonNode?> SendJsonAsync(HttpRequestMessage req, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        using var res = await Client.SendAsync(req, cts.Token).ConfigureAwait(false);
        var body = await res.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"HTTP {(int)res.StatusCode}: {Trim(body)}", null, res.StatusCode);
        return string.IsNullOrWhiteSpace(body) ? null : JsonNode.Parse(body);
    }

    public static HttpRequestMessage Json(HttpMethod method, string url, object payload) =>
        new(method, url) { Content = JsonContent.Create(payload, options: new JsonSerializerOptions()) };

    private static string Trim(string s) => s.Length > 300 ? s[..300] : s;
}
