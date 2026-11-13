using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using SysWatch.Config;

namespace SysWatch.Notify;

public static class SignalCli
{
    public static string ResolvePath(SignalConfig s)
    {
        if (s.CliPath.Length > 0) return s.CliPath;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in new[] { "signal-cli.bat", "signal-cli.cmd", "signal-cli.exe" })
            {
                var candidate = Path.Combine(dir.Trim(), name);
                if (File.Exists(candidate)) return candidate;
            }
        }
        throw new FileNotFoundException("signal-cli bulunamadı. Ayarlar > Signal'de yolunu seçin.");
    }

    public static ProcessStartInfo StartInfo(SignalConfig s, IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(ResolvePath(s))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-a");
        psi.ArgumentList.Add(s.Account);
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }

    // One short-lived JVM per message: nothing stays resident between alerts.
    public static async Task SendOnceAsync(SignalConfig s, string text, CancellationToken ct)
    {
        var args = new List<string> { "send", "--message-from-stdin" };
        if (s.NoteToSelf) args.Add("--note-to-self");
        else args.Add(s.Recipient);

        using var proc = Process.Start(StartInfo(s, args)) ?? throw new InvalidOperationException("signal-cli başlatılamadı.");
        JobObject.Add(proc);
        await proc.StandardInput.WriteAsync(text).ConfigureAwait(false);
        proc.StandardInput.Close();
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        _ = proc.StandardOutput.ReadToEndAsync(ct);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(90));
        try
        {
            await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            proc.Kill(entireProcessTree: true);
            throw new TimeoutException("signal-cli 90 sn içinde bitmedi.");
        }
        if (proc.ExitCode != 0)
        {
            var err = await stderrTask.ConfigureAwait(false);
            throw new InvalidOperationException($"signal-cli çıkış kodu {proc.ExitCode}: {Last(err)}");
        }
    }

    public static Task RpcSendAsync(SignalConfig s, string text, CancellationToken ct)
    {
        var parameters = new JsonObject { ["message"] = text };
        if (s.NoteToSelf) parameters["noteToSelf"] = true;
        else parameters["recipient"] = new JsonArray(s.Recipient);
        var payload = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "send",
            ["params"] = parameters,
            ["id"] = Environment.TickCount64,
        };
        var req = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{s.DaemonPort}/api/v1/rpc")
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        return CheckRpc(Http.SendJsonAsync(req, TimeSpan.FromSeconds(60), ct));
    }

    private static async Task CheckRpc(Task<JsonNode?> call)
    {
        var res = await call.ConfigureAwait(false);
        if (res?["error"] is JsonNode error)
            throw new InvalidOperationException("signal-cli: " + (error["message"]?.ToString() ?? error.ToJsonString()));
    }

    private static string Last(string s)
    {
        var lines = s.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? "" : lines[^1];
    }
}

public sealed class SignalCliHost : IDisposable
{
    private Process? _proc;

    public bool Running => _proc is { HasExited: false };

    public void Start(SignalConfig s)
    {
        Stop();
        var psi = SignalCli.StartInfo(s, ["daemon", $"--http=127.0.0.1:{s.DaemonPort}", "--receive-mode=on-start"]);
        psi.RedirectStandardInput = false;
        _proc = Process.Start(psi) ?? throw new InvalidOperationException("signal-cli daemon başlatılamadı.");
        JobObject.Add(_proc);
        _proc.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data) && (e.Data.Contains("ERROR") || e.Data.Contains("WARN")))
                Log.Warn("signal-cli: " + e.Data);
        };
        _proc.BeginOutputReadLine();
        _proc.BeginErrorReadLine();
        Log.Info("signal-cli daemon başlatıldı.");
    }

    public void Stop()
    {
        if (_proc == null) return;
        try
        {
            if (!_proc.HasExited) _proc.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            Log.Warn($"signal-cli durdurulamadı: {ex.Message}");
        }
        _proc.Dispose();
        _proc = null;
    }

    public void Dispose() => Stop();
}

public sealed class SignalNotifier(SignalCliHost host) : INotifier
{
    public string Name => "Signal";

    public bool IsEnabled(AppConfig cfg) =>
        cfg.Signal.Mode != SignalMode.Off && cfg.Signal.Account.Length > 0 &&
        (cfg.Signal.NoteToSelf || cfg.Signal.Recipient.Length > 0);

    public bool IsMinimal(AppConfig cfg) => cfg.Signal.Minimal;

    public Task SendAsync(string text, AppConfig cfg, CancellationToken ct) =>
        cfg.Signal.Mode == SignalMode.NotifyAndCommands && host.Running
            ? SignalCli.RpcSendAsync(cfg.Signal, text, ct)
            : SignalCli.SendOnceAsync(cfg.Signal, text, ct);
}
