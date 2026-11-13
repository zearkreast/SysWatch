using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using SysWatch.Notify;

namespace SysWatch.Updates;

public enum InstallKind { None, Installed, Portable }

public sealed record UpdateInfo(Version Version, string AssetUrl, string AssetName, string SumsUrl);

// A downloaded file must match the SHA-256 in its release's SHA256SUMS.txt before anything is run or replaced.
public static class Updater
{
    private const string Repo = "zearkreast/SysWatch";
    private static readonly string WorkDir = Path.Combine(Path.GetTempPath(), "SysWatch-update");

    public static Version Current { get; } = Normalize(typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0, 0));

    // The installer leaves its uninstaller next to the exe; a build run from bin\ is neither kind and never updates.
    public static InstallKind Kind { get; } = Log.Portable ? InstallKind.Portable
        : File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe")) ? InstallKind.Installed
        : InstallKind.None;

    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        var release = await Http.SendJsonAsync(req, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
        var tag = release?["tag_name"]?.GetValue<string>() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest) || Normalize(latest) <= Current) return null;

        var assets = release?["assets"] as JsonArray ?? new JsonArray();
        string? Url(string name) => assets.FirstOrDefault(a => a?["name"]?.GetValue<string>() == name)?["browser_download_url"]?.GetValue<string>();
        var assetName = Kind == InstallKind.Portable ? "SysWatch_x64-portable.zip" : "SysWatch_x64-setup.exe";
        var asset = Url(assetName);
        var sums = Url("SHA256SUMS.txt");
        if (asset == null || sums == null)
            throw new InvalidOperationException(L.T($"{tag} sürümünde {assetName} ya da SHA256SUMS.txt yok.", $"Release {tag} has no {assetName} or SHA256SUMS.txt."));
        return new UpdateInfo(Normalize(latest), asset, assetName, sums);
    }

    public static async Task<string> DownloadAsync(UpdateInfo update, CancellationToken ct)
    {
        if (Directory.Exists(WorkDir)) Directory.Delete(WorkDir, true);
        Directory.CreateDirectory(WorkDir);
        var file = Path.Combine(WorkDir, update.AssetName);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(10));
        var sums = await Http.Client.GetStringAsync(update.SumsUrl, cts.Token).ConfigureAwait(false);
        var expected = sums.Split('\n')
            .Select(l => l.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries))
            .FirstOrDefault(p => p.Length == 2 && p[1].Trim().TrimStart('*') == update.AssetName)?[0];
        if (expected == null)
            throw new InvalidOperationException(L.T($"SHA256SUMS.txt içinde {update.AssetName} yok.", $"SHA256SUMS.txt doesn't list {update.AssetName}."));

        using (var res = await Http.Client.GetAsync(update.AssetUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false))
        {
            res.EnsureSuccessStatusCode();
            await using var output = File.Create(file);
            await res.Content.CopyToAsync(output, cts.Token).ConfigureAwait(false);
        }

        string actual;
        await using (var input = File.OpenRead(file))
            actual = Convert.ToHexString(await SHA256.HashDataAsync(input, cts.Token).ConfigureAwait(false));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(file);
            throw new InvalidOperationException(L.T("İndirilen dosyanın SHA-256 özeti tutmuyor; güncelleme iptal edildi.",
                "The downloaded file's SHA-256 doesn't match; the update was cancelled."));
        }
        return file;
    }

    // The caller must exit right after: the installer stops a SysWatch still running, and a portable
    // restart waits for this process to end before taking the single-instance lock.
    public static void Apply(string file)
    {
        if (Kind == InstallKind.Installed)
        {
            Process.Start(new ProcessStartInfo(file, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /UPDATE=1") { UseShellExecute = false });
            return;
        }

        var unpacked = Path.Combine(WorkDir, "unpacked");
        ZipFile.ExtractToDirectory(file, unpacked, overwriteFiles: true);
        var source = Path.Combine(unpacked, "SysWatch");
        if (!File.Exists(Path.Combine(source, "SysWatch.exe")))
            throw new InvalidOperationException(L.T("Zip beklenen dosyaları içermiyor.", "The zip doesn't hold the expected files."));

        var target = AppContext.BaseDirectory;
        var exe = Environment.ProcessPath!;
        foreach (var path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, path);
            if (rel.Equals("portable.txt", StringComparison.OrdinalIgnoreCase)) continue;
            var dest = Path.Combine(target, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            // Windows lets a running exe be renamed but not overwritten.
            if (string.Equals(Path.GetFullPath(dest), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase))
                File.Move(exe, exe + ".old", overwrite: true);
            File.Copy(path, dest, overwrite: true);
        }
        Process.Start(new ProcessStartInfo(exe, $"--after-update {Environment.ProcessId}") { UseShellExecute = false });
    }

    public static void CleanUp()
    {
        try
        {
            var old = Environment.ProcessPath + ".old";
            if (File.Exists(old)) File.Delete(old);
            if (Directory.Exists(WorkDir)) Directory.Delete(WorkDir, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));
}
