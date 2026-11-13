using System.Text.Json;
using System.Text.Json.Serialization;

namespace SysWatch.Config;

public enum Metric { CpuTemp, GpuTemp, CpuLoad, GpuLoad, Ram, DiskTemp }

public enum TelegramMode { Off, Notify, NotifyAndCommands }
public enum DiscordMode { Off, Webhook, Bot }
public enum SignalMode { Off, Notify, NotifyAndCommands }
public enum UiTheme { Light, Dark, System }

public sealed class Rule
{
    public Metric Metric { get; set; }
    public bool Enabled { get; set; } = true;
    public double Limit { get; set; }
    public double Hysteresis { get; set; }
    public int DurationSec { get; set; } = 30;
    public int CooldownMin { get; set; } = 10;
}

public sealed class HardwareConfig
{
    public bool Cpu { get; set; } = true;
    public bool Gpu { get; set; } = true;
    // Vendor libraries (nvml/nvapi/ADL) via LHM: more sensors, ~40 MB more memory.
    public bool GpuDetailed { get; set; }
    public bool Storage { get; set; } = true;
    // SMART via LHM: more disk sensors, ~40 MB more memory when elevated.
    public bool StorageDetailed { get; set; }
    public bool Motherboard { get; set; }
    public bool Memory { get; set; }
    public bool Controller { get; set; }
    public bool Network { get; set; }
    public bool Psu { get; set; }
}

public sealed class LocalConfig
{
    public bool Enabled { get; set; } = true;
}

public sealed class TelegramConfig
{
    public TelegramMode Mode { get; set; } = TelegramMode.Off;
    [JsonIgnore] public string BotToken { get; set; } = "";
    [JsonPropertyName("BotTokenProtected")]
    public string? BotTokenProtected { get => SecretStore.Protect(BotToken); set => BotToken = SecretStore.Unprotect(value); }
    public long ChatId { get; set; }
    public bool Minimal { get; set; } = true;
}

public sealed class DiscordConfig
{
    public DiscordMode Mode { get; set; } = DiscordMode.Off;
    [JsonIgnore] public string WebhookUrl { get; set; } = "";
    [JsonPropertyName("WebhookUrlProtected")]
    public string? WebhookUrlProtected { get => SecretStore.Protect(WebhookUrl); set => WebhookUrl = SecretStore.Unprotect(value); }
    [JsonIgnore] public string BotToken { get; set; } = "";
    [JsonPropertyName("BotTokenProtected")]
    public string? BotTokenProtected { get => SecretStore.Protect(BotToken); set => BotToken = SecretStore.Unprotect(value); }
    public string ChannelId { get; set; } = "";
    public string AllowedUserId { get; set; } = "";
    public bool Minimal { get; set; } = true;
}

public sealed class EmailConfig
{
    public bool Enabled { get; set; }
    public string SmtpHost { get; set; } = "";
    public int Port { get; set; } = 587;
    public string Username { get; set; } = "";
    [JsonIgnore] public string Password { get; set; } = "";
    [JsonPropertyName("PasswordProtected")]
    public string? PasswordProtected { get => SecretStore.Protect(Password); set => Password = SecretStore.Unprotect(value); }
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string PgpPublicKeyPath { get; set; } = "";
    public bool Minimal { get; set; }
}

public sealed class SignalConfig
{
    public SignalMode Mode { get; set; } = SignalMode.Off;
    public string CliPath { get; set; } = "";
    public string Account { get; set; } = "";
    public string Recipient { get; set; } = "";
    public bool NoteToSelf { get; set; }
    public int DaemonPort { get; set; } = 18765;
    public bool Minimal { get; set; }
}

public sealed class SecurityConfig
{
    public bool PinEnabled { get; set; }
    [JsonIgnore] public string Pin { get; set; } = "";
    [JsonPropertyName("PinProtected")]
    public string? PinProtected { get => SecretStore.Protect(Pin); set => Pin = SecretStore.Unprotect(value); }
}

public sealed class AppConfig
{
    public int IntervalSeconds { get; set; } = 3;
    public bool IncludeHostname { get; set; } = true;
    public List<Metric> TrayReadings { get; set; } = [];
    // Hex colour for the tray numbers; empty follows the taskbar (light/dark).
    public string TrayNumberColor { get; set; } = "";
    public UiTheme UiTheme { get; set; } = UiTheme.Light;
    public bool ReduceMotion { get; set; }
    public HardwareConfig Hardware { get; set; } = new();
    public List<Rule> Rules { get; set; } = DefaultRules();
    public LocalConfig Local { get; set; } = new();
    public TelegramConfig Telegram { get; set; } = new();
    public DiscordConfig Discord { get; set; } = new();
    public EmailConfig Email { get; set; } = new();
    public SignalConfig Signal { get; set; } = new();
    public SecurityConfig Security { get; set; } = new();

    public static string FilePath => Path.Combine(Log.DataDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static List<Rule> DefaultRules() =>
    [
        new() { Metric = Metric.CpuTemp, Limit = 85, Hysteresis = 5 },
        new() { Metric = Metric.GpuTemp, Limit = 83, Hysteresis = 5 },
        new() { Metric = Metric.CpuLoad, Limit = 90, Hysteresis = 10 },
        new() { Metric = Metric.GpuLoad, Limit = 95, Hysteresis = 10 },
        new() { Metric = Metric.Ram, Limit = 90, Hysteresis = 5 },
        new() { Metric = Metric.DiskTemp, Limit = 70, Hysteresis = 3 },
    ];

    public Rule RuleFor(Metric metric)
    {
        var rule = Rules.FirstOrDefault(r => r.Metric == metric);
        if (rule == null)
        {
            rule = DefaultRules().First(r => r.Metric == metric);
            rule.Enabled = false;
            Rules.Add(rule);
        }
        return rule;
    }

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), JsonOptions) ?? new AppConfig();
                cfg.Normalize();
                return cfg;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Log.Error("config.json okunamadı, varsayılanlar kullanılıyor", ex);
        }
        var fresh = new AppConfig();
        fresh.Save();
        return fresh;
    }

    public void Save()
    {
        Normalize();
        Directory.CreateDirectory(Log.DataDir);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(tmp, FilePath, overwrite: true);
    }

    public AppConfig Clone() =>
        JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;

    private void Normalize()
    {
        IntervalSeconds = Math.Clamp(IntervalSeconds, 1, 60);
        foreach (var metric in Enum.GetValues<Metric>()) RuleFor(metric);
        Rules = Rules.GroupBy(r => r.Metric).Select(g => g.First()).OrderBy(r => r.Metric).ToList();
        foreach (var r in Rules)
        {
            r.DurationSec = Math.Clamp(r.DurationSec, 0, 3600);
            r.CooldownMin = Math.Clamp(r.CooldownMin, 1, 1440);
            r.Hysteresis = Math.Max(0, r.Hysteresis);
        }
    }
}
