using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SysWatch.Config;
using SysWatch.Monitoring;
using SysWatch.Notify;

namespace SysWatch.Commands;

public interface IAppContext
{
    AppConfig Config { get; }
    Snapshot? LastSnapshot { get; }
    IReadOnlyList<Metric> ActiveAlerts { get; }
    NotifierHub Hub { get; }
    AlertLog Events { get; }
    void UpdateConfig(Action<AppConfig> change);
}

public sealed class CommandRouter(IAppContext app)
{
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso",
        "svchost", "explorer", "dwm", "fontdrvhost", "memory compression", "secure system", "msmpeng",
    };

    private readonly object _gate = new();
    private (int Minutes, DateTime Expires)? _pendingShutdown;
    private int _pinFailures;
    private DateTime _pinLockedUntil = DateTime.MinValue;

    public static bool LooksLikeCommand(string? text) => text != null && text.TrimStart().StartsWith('/');

    public async Task<string> HandleAsync(string text, string channel, bool minimal)
    {
        var parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLowerInvariant();
        var at = cmd.IndexOf('@'); // Telegram appends @BotName in groups
        if (at > 0) cmd = cmd[..at];
        var args = parts.Skip(1).ToArray();
        Log.Info(L.T($"Komut ({channel}): {cmd}", $"Command ({channel}): {cmd}"));
        if (channel != "Yerel") app.Events.Add(EventKind.Command, L.T($"Komut ({channel}): {cmd}", $"Command ({channel}): {cmd}"));

        try
        {
            return cmd switch
            {
                "/start" or "/help" or "/yardim" => Help(),
                "/status" or "/durum" => Status(minimal),
                "/top" => minimal ? MinimalRefusal() : await TopAsync().ConfigureAwait(false),
                "/kill" => Kill(args),
                "/shutdown" => Shutdown(args),
                "/confirm" => Confirm(),
                "/cancel" => Cancel(),
                "/thresholds" or "/esikler" => Thresholds(),
                "/set" => Set(args),
                "/mute" => Mute(args),
                "/unmute" => Unmute(),
                _ => L.T("Bilinmeyen komut. /help yazın.", "Unknown command. Type /help."),
            };
        }
        catch (Exception ex)
        {
            Log.Error(L.T($"Komut hatası ({cmd})", $"Command error ({cmd})"), ex);
            return L.T("Komut çalıştırılırken hata oluştu: ", "The command failed: ") + ex.Message;
        }
    }

    private static string Help() => L.T(
        """
        SysWatch komutları:
        /status: anlık durum
        /top: en çok kaynak kullanan işlemler
        /kill <ad|pid> [PIN]: işlemi sonlandırır
        /shutdown [dakika] [PIN]: bilgisayarı kapatır (onay ister)
        /confirm: kapatmayı onaylar · /cancel: kapatmayı iptal eder
        /thresholds: eşikleri gösterir
        /set <metrik> <değer|on|off>: eşik değiştirir (ör. /set cpu_temp 80)
        /mute <dakika> · /unmute: bildirimleri susturur / açar
        Metrikler: cpu_temp, gpu_temp, cpu_load, gpu_load, ram, disk_temp
        """,
        """
        SysWatch commands:
        /status: current readings
        /top: processes using the most resources
        /kill <name|pid> [PIN]: ends a process
        /shutdown [minutes] [PIN]: shuts the PC down (asks for confirmation)
        /confirm: confirms the shutdown · /cancel: cancels it
        /thresholds: shows the thresholds
        /set <metric> <value|on|off>: changes a threshold (e.g. /set cpu_temp 80)
        /mute <minutes> · /unmute: mutes / unmutes notifications
        Metrics: cpu_temp, gpu_temp, cpu_load, gpu_load, ram, disk_temp
        """);

    private static string MinimalRefusal() =>
        L.T("Bu kanal az bilgi modunda, ayrıntı gönderilmiyor. Signal veya PGP'li e-posta kullanın ya da az bilgi modunu kapatın.", "This channel is in minimal mode, so no details are sent. Use Signal or PGP e-mail, or turn minimal mode off.");

    private string Status(bool minimal)
    {
        var active = app.ActiveAlerts;
        var muted = app.Hub.Muted ? L.T($" · Bildirimler {app.Hub.MutedUntil:HH:mm}'e kadar susturuldu", $" · Notifications muted until {app.Hub.MutedUntil:HH:mm}") : "";
        if (minimal)
            return (active.Count == 0 ? L.T("Durum: normal.", "Status: normal.") : L.T($"Durum: {active.Count} uyarı aktif ({string.Join(", ", active.Select(MetricInfo.Label))}).", $"Status: {active.Count} active alert(s) ({string.Join(", ", active.Select(MetricInfo.Label))}).")) + muted;

        var snap = app.LastSnapshot;
        if (snap == null) return L.T("Henüz ölçüm yok, birkaç saniye sonra tekrar deneyin.", "No reading yet, try again in a few seconds.");
        var sb = new StringBuilder();
        if (app.Config.IncludeHostname) sb.AppendLine(Environment.MachineName);
        foreach (var m in Enum.GetValues<Metric>())
        {
            var v = snap.Get(m);
            var flag = active.Contains(m) ? " ⚠" : "";
            sb.AppendLine($"{MetricInfo.Label(m)}: {(v == null ? L.T("okunamıyor", "unreadable") : MetricInfo.Format(m, v.Value))}{flag}");
        }
        if (snap.Details.Count > 0)
        {
            sb.AppendLine();
            foreach (var d in snap.Details) sb.AppendLine(d);
        }
        sb.Append(active.Count == 0 ? L.T("Uyarı yok.", "No alerts.") : L.T($"{active.Count} uyarı aktif.", $"{active.Count} active alert(s)."));
        sb.Append(muted);
        return sb.ToString();
    }

    private static async Task<string> TopAsync()
    {
        static Dictionary<int, TimeSpan> Sample()
        {
            var map = new Dictionary<int, TimeSpan>();
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    map[p.Id] = p.TotalProcessorTime;
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                }
                finally
                {
                    p.Dispose();
                }
            }
            return map;
        }

        var first = Sample();
        var sw = Stopwatch.StartNew();
        await Task.Delay(1000).ConfigureAwait(false);
        var second = Sample();
        var elapsed = sw.Elapsed.TotalMilliseconds * Environment.ProcessorCount;

        var rows = new List<(string Name, int Pid, double Cpu, long Mem)>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    var cpu = first.TryGetValue(p.Id, out var a) && second.TryGetValue(p.Id, out var b)
                        ? (b - a).TotalMilliseconds / elapsed * 100 : 0;
                    rows.Add((p.ProcessName, p.Id, cpu, p.WorkingSet64));
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                }
            }
        }

        var sb = new StringBuilder(L.T("En çok CPU:\n", "Top CPU:\n"));
        foreach (var r in rows.Where(r => r.Pid != 0).OrderByDescending(r => r.Cpu).Take(5))
            sb.AppendLine($"  {r.Name} (pid {r.Pid}): {L.Percent(r.Cpu, "0.0")}");
        sb.AppendLine(L.T("En çok RAM:", "Top RAM:"));
        foreach (var r in rows.OrderByDescending(r => r.Mem).Take(5))
            sb.AppendLine($"  {r.Name} (pid {r.Pid}): {r.Mem / 1048576.0:0} MB");
        return sb.ToString().TrimEnd();
    }

    private string Kill(string[] args)
    {
        if (!CheckPin(ref args, out var pinError)) return pinError;
        if (args.Length != 1) return L.T("Kullanım: /kill <ad|pid>", "Usage: /kill <name|pid>") + (app.Config.Security.PinEnabled ? " <PIN>" : "");

        var target = args[0];
        var self = Environment.ProcessId;
        List<Process> matches;
        if (int.TryParse(target, out var pid))
        {
            try
            {
                matches = [Process.GetProcessById(pid)];
            }
            catch (ArgumentException)
            {
                return L.T($"pid {pid} bulunamadı.", $"pid {pid} not found.");
            }
        }
        else
        {
            var name = target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? target[..^4] : target;
            matches = Process.GetProcessesByName(name).ToList();
            if (matches.Count == 0) return L.T($"\"{name}\" adında çalışan işlem yok.", $"No running process named \"{name}\".");
        }

        var killed = 0;
        var refused = new List<string>();
        foreach (var p in matches)
        {
            using (p)
            {
                if (p.Id <= 4 || p.Id == self || Protected.Contains(p.ProcessName) || p.SessionId == 0)
                {
                    refused.Add($"{p.ProcessName} ({p.Id})");
                    continue;
                }
                try
                {
                    p.Kill();
                    killed++;
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    refused.Add($"{p.ProcessName} ({p.Id}): {ex.Message}");
                }
            }
        }

        var msg = L.T($"{killed} işlem sonlandırıldı.", $"{killed} process(es) ended.");
        if (refused.Count > 0) msg += L.T(" Korumalı/sonlandırılamayan: ", " Protected or couldn't be ended: ") + string.Join(", ", refused.Take(5));
        Log.Info(L.T($"/kill {target}: {killed} sonlandırıldı, {refused.Count} reddedildi", $"/kill {target}: {killed} ended, {refused.Count} refused"));
        return msg;
    }

    private string Shutdown(string[] args)
    {
        if (!CheckPin(ref args, out var pinError)) return pinError;
        var minutes = 1;
        if (args.Length > 0 && (!int.TryParse(args[0], out minutes) || minutes < 0 || minutes > 1440))
            return L.T("Kullanım: /shutdown [dakika 0-1440]", "Usage: /shutdown [minutes 0-1440]");
        lock (_gate) _pendingShutdown = (minutes, DateTime.Now.AddMinutes(2));
        return L.T($"Bilgisayar {minutes} dakika sonra kapatılacak. Onaylamak için 2 dakika içinde /confirm yazın.", $"The PC will shut down in {minutes} minute(s). Send /confirm within 2 minutes to go ahead.");
    }

    private string Confirm()
    {
        (int Minutes, DateTime Expires) pending;
        lock (_gate)
        {
            if (_pendingShutdown == null || _pendingShutdown.Value.Expires < DateTime.Now)
            {
                _pendingShutdown = null;
                return L.T("Onay bekleyen bir kapatma yok.", "There is no shutdown waiting for confirmation.");
            }
            pending = _pendingShutdown.Value;
            _pendingShutdown = null;
        }
        var seconds = Math.Max(pending.Minutes * 60, 10);
        RunShutdown("/s", "/t", seconds.ToString(CultureInfo.InvariantCulture), "/c", L.T("SysWatch uzaktan kapatma", "SysWatch remote shutdown"));
        Log.Info(L.T($"Kapatma planlandı: {seconds} sn", $"Shutdown scheduled: {seconds} s"));
        return L.T($"Kapatma planlandı ({seconds} sn). İptal için /cancel.", $"Shutdown scheduled ({seconds} s). Send /cancel to stop it.");
    }

    private string Cancel()
    {
        lock (_gate) _pendingShutdown = null;
        var code = RunShutdown("/a");
        return code == 0 ? L.T("Planlanmış kapatma iptal edildi.", "The scheduled shutdown was cancelled.") : L.T("İptal edilecek planlanmış kapatma yoktu.", "There was no scheduled shutdown to cancel.");
    }

    private static int RunShutdown(params string[] args)
    {
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.WaitForExit(10000);
        return p.HasExited ? p.ExitCode : -1;
    }

    private string Thresholds()
    {
        var sb = new StringBuilder(L.T("Eşikler:\n", "Thresholds:\n"));
        foreach (var r in app.Config.Rules)
        {
            sb.AppendLine($"{MetricInfo.Key(r.Metric)}: {(r.Enabled ? MetricInfo.Format(r.Metric, r.Limit) : L.T("kapalı", "off"))}" +
                          L.T($" · süre {r.DurationSec} sn · bekleme {r.CooldownMin} dk", $" · duration {r.DurationSec} s · repeat {r.CooldownMin} min"));
        }
        return sb.ToString().TrimEnd();
    }

    private string Set(string[] args)
    {
        if (args.Length != 2 || !MetricInfo.TryParse(args[0], out var metric))
            return L.T("Kullanım: /set <metrik> <değer|on|off>. Metrikler: cpu_temp, gpu_temp, cpu_load, gpu_load, ram, disk_temp", "Usage: /set <metric> <value|on|off>. Metrics: cpu_temp, gpu_temp, cpu_load, gpu_load, ram, disk_temp");

        var value = args[1].ToLowerInvariant();
        string result;
        if (value is "on" or "off")
        {
            var enabled = value == "on";
            app.UpdateConfig(c => c.RuleFor(metric).Enabled = enabled);
            result = L.T($"{MetricInfo.Label(metric)} uyarısı {(enabled ? "açıldı" : "kapatıldı")}.", $"{MetricInfo.Label(metric)} alert turned {(enabled ? "on" : "off")}.");
        }
        else if (double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var limit)
                 && limit is > 0 and <= 150)
        {
            app.UpdateConfig(c =>
            {
                var r = c.RuleFor(metric);
                r.Limit = limit;
                r.Enabled = true;
            });
            result = L.T($"{MetricInfo.Label(metric)} eşiği {MetricInfo.Format(metric, limit)} yapıldı.", $"{MetricInfo.Label(metric)} threshold set to {MetricInfo.Format(metric, limit)}.");
        }
        else
        {
            return L.T("Değer 0-150 arasında bir sayı ya da on/off olmalı.", "The value must be a number from 0 to 150, or on/off.");
        }
        return result;
    }

    private string Mute(string[] args)
    {
        var minutes = 60;
        if (args.Length > 0 && (!int.TryParse(args[0], out minutes) || minutes < 1 || minutes > 1440))
            return L.T("Kullanım: /mute <dakika 1-1440>", "Usage: /mute <minutes 1-1440>");
        app.Hub.MutedUntil = DateTime.Now.AddMinutes(minutes);
        return L.T($"Bildirimler {app.Hub.MutedUntil:HH:mm}'e kadar susturuldu.", $"Notifications muted until {app.Hub.MutedUntil:HH:mm}.");
    }

    private string Unmute()
    {
        app.Hub.MutedUntil = DateTime.MinValue;
        return L.T("Bildirimler açıldı.", "Notifications are on.");
    }

    // When a PIN is set, it must be the last argument. Five wrong tries lock PIN commands for 15 minutes.
    private bool CheckPin(ref string[] args, out string error)
    {
        error = "";
        var sec = app.Config.Security;
        if (!sec.PinEnabled || sec.Pin.Length == 0) return true;

        lock (_gate)
        {
            if (DateTime.Now < _pinLockedUntil)
            {
                error = L.T($"Çok fazla hatalı PIN. {_pinLockedUntil:HH:mm}'e kadar bu komutlar kilitli.", $"Too many wrong PINs. These commands are locked until {_pinLockedUntil:HH:mm}.");
                return false;
            }
            if (args.Length == 0)
            {
                error = L.T("Bu komut PIN istiyor (son argüman olarak yazın).", "This command needs the PIN (as the last argument).");
                return false;
            }
            var given = Encoding.UTF8.GetBytes(args[^1]);
            var expected = Encoding.UTF8.GetBytes(sec.Pin);
            if (!CryptographicOperations.FixedTimeEquals(given, expected))
            {
                if (++_pinFailures >= 5)
                {
                    _pinFailures = 0;
                    _pinLockedUntil = DateTime.Now.AddMinutes(15);
                }
                Log.Warn(L.T("Hatalı PIN denemesi", "Wrong PIN attempt"));
                error = L.T("PIN hatalı.", "Wrong PIN.");
                return false;
            }
            _pinFailures = 0;
            args = args[..^1];
            return true;
        }
    }
}
