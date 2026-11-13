namespace SysWatch.UI;

// Tiny tweening helper on one shared UI timer. With reduced motion every animation jumps straight to its end.
public static class Motion
{
    private sealed class Tween
    {
        public required object Key;
        public required Action<float> Step;
        public Action? Done;
        public long Start;
        public int Duration;
    }

    private static readonly List<Tween> Active = new();
    private static System.Windows.Forms.Timer? _timer;

    public static bool UserReduced { get; set; }

    // The user's choice, or Windows' own "Animation effects" switch when that is off.
    public static bool Reduced => UserReduced || !SystemInformation.UIEffectsEnabled;

    public static float EaseOut(float t) => 1 - MathF.Pow(1 - t, 3);

    // Starts (or restarts) the animation identified by key; step receives eased progress 0..1.
    public static void Run(object key, int durationMs, Action<float> step, Action? done = null)
    {
        Active.RemoveAll(t => ReferenceEquals(t.Key, key));
        if (Reduced || durationMs <= 0)
        {
            step(1f);
            done?.Invoke();
            return;
        }
        Active.Add(new Tween { Key = key, Step = step, Done = done, Start = Environment.TickCount64, Duration = durationMs });
        step(0f);
        _timer ??= CreateTimer();
        _timer.Start();
    }

    public static void Stop(object key) => Active.RemoveAll(t => ReferenceEquals(t.Key, key));

    private static System.Windows.Forms.Timer CreateTimer()
    {
        var timer = new System.Windows.Forms.Timer { Interval = 15 };
        timer.Tick += (_, _) =>
        {
            var now = Environment.TickCount64;
            foreach (var t in Active.ToArray())
            {
                var p = Math.Clamp((now - t.Start) / (float)t.Duration, 0f, 1f);
                try
                {
                    t.Step(EaseOut(p));
                }
                catch (ObjectDisposedException)
                {
                    p = 1f;
                }
                if (p >= 1f)
                {
                    Active.Remove(t);
                    t.Done?.Invoke();
                }
            }
            if (Active.Count == 0) timer.Stop();
        };
        return timer;
    }
}
