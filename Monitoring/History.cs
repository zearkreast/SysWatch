using System.Text;
using System.Text.Json;
using SysWatch.Config;

namespace SysWatch.Monitoring;

// Last N samples per metric for the sparklines. ~5 KB for 6 metrics × 100 samples.
public sealed class MetricHistory(int capacity = 100)
{
    private readonly Dictionary<Metric, double[]> _data = new();
    private readonly object _gate = new();
    private int _head;
    private int _count;

    public int Capacity => capacity;

    public void Add(Snapshot snap)
    {
        lock (_gate)
        {
            foreach (var m in Enum.GetValues<Metric>())
            {
                if (!_data.TryGetValue(m, out var buf)) _data[m] = buf = Enumerable.Repeat(double.NaN, capacity).ToArray();
                buf[_head] = snap.Get(m) ?? double.NaN;
            }
            _head = (_head + 1) % capacity;
            _count = Math.Min(_count + 1, capacity);
        }
    }

    // Oldest first; NaN marks a missing reading.
    public double[] Series(Metric m)
    {
        lock (_gate)
        {
            var result = new double[_count];
            if (!_data.TryGetValue(m, out var buf)) return Enumerable.Repeat(double.NaN, _count).ToArray();
            var start = (_head - _count + capacity) % capacity;
            for (var i = 0; i < _count; i++) result[i] = buf[(start + i) % capacity];
            return result;
        }
    }
}

public enum EventKind { Alert, Recovered, Command, Info }

public sealed record EventEntry(DateTime Time, EventKind Kind, string Text);

// Recent events in memory, everything (up to 1 MB / 7 days) in a JSONL file read only when the history window opens.
public sealed class AlertLog(string path)
{
    private const int MemoryCapacity = 200;
    private const long MaxBytes = 1024 * 1024;
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    private readonly LinkedList<EventEntry> _recent = new();
    private readonly object _gate = new();

    public event Action<EventEntry>? Added;

    public string FilePath => path;

    public void Add(EventKind kind, string text)
    {
        var entry = new EventEntry(DateTime.Now, kind, text);
        lock (_gate)
        {
            _recent.AddFirst(entry);
            while (_recent.Count > MemoryCapacity) _recent.RemoveLast();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (new FileInfo(path) is { Exists: true, Length: > MaxBytes }) Trim();
                File.AppendAllText(path, JsonSerializer.Serialize(entry) + "\n", Encoding.UTF8);
            }
            catch (IOException ex)
            {
                Log.Warn($"Geçmiş dosyasına yazılamadı: {ex.Message}");
            }
        }
        Added?.Invoke(entry);
    }

    // Newest first.
    public List<EventEntry> ReadAll()
    {
        lock (_gate)
        {
            var cutoff = DateTime.Now - MaxAge;
            var list = new List<EventEntry>();
            try
            {
                if (File.Exists(path))
                {
                    foreach (var line in File.ReadLines(path))
                    {
                        if (TryParse(line) is { } e && e.Time >= cutoff) list.Add(e);
                    }
                }
            }
            catch (IOException ex)
            {
                Log.Warn($"Geçmiş dosyası okunamadı: {ex.Message}");
            }
            if (list.Count == 0) list.AddRange(_recent.Reverse());
            list.Reverse();
            return list;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _recent.Clear();
            try
            {
                File.Delete(path);
            }
            catch (IOException ex)
            {
                Log.Warn($"Geçmiş silinemedi: {ex.Message}");
            }
        }
    }

    // Keeps the newer half of the entries that are younger than MaxAge.
    private void Trim()
    {
        var cutoff = DateTime.Now - MaxAge;
        var keep = File.ReadLines(path).Select(TryParse).Where(e => e != null && e.Time >= cutoff).ToList();
        keep = keep.Skip(keep.Count / 2).ToList();
        var tmp = path + ".tmp";
        File.WriteAllLines(tmp, keep.Select(e => JsonSerializer.Serialize(e)), Encoding.UTF8);
        File.Move(tmp, path, overwrite: true);
    }

    private static EventEntry? TryParse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try
        {
            return JsonSerializer.Deserialize<EventEntry>(line);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
