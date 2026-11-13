using SysWatch.Config;
using SysWatch.Notify;

namespace SysWatch.Monitoring;

public sealed class AlertEngine
{
    private sealed class State
    {
        public DateTime? AboveSince;
        public bool Active;
        public DateTime LastNotified = DateTime.MinValue;
        public double Peak;
    }

    private readonly Dictionary<Metric, State> _states = new();
    private readonly object _gate = new();

    public IReadOnlyList<Metric> ActiveMetrics
    {
        get
        {
            lock (_gate) return _states.Where(kv => kv.Value.Active).Select(kv => kv.Key).ToList();
        }
    }

    public List<Notification> Evaluate(Snapshot snap, AppConfig cfg)
    {
        var output = new List<Notification>();
        lock (_gate)
        {
            foreach (var rule in cfg.Rules)
            {
                if (!_states.TryGetValue(rule.Metric, out var st)) _states[rule.Metric] = st = new State();

                if (!rule.Enabled)
                {
                    st.Active = false;
                    st.AboveSince = null;
                    continue;
                }

                var value = snap.Get(rule.Metric);
                if (value == null) continue;
                var v = value.Value;
                var now = snap.Time;

                if (!st.Active)
                {
                    if (v >= rule.Limit)
                    {
                        st.AboveSince ??= now;
                        if ((now - st.AboveSince.Value).TotalSeconds >= rule.DurationSec)
                        {
                            st.Active = true;
                            st.Peak = v;
                            st.LastNotified = now;
                            output.Add(Notification.Alert(rule, v, false));
                        }
                    }
                    else
                    {
                        st.AboveSince = null;
                    }
                    continue;
                }

                st.Peak = Math.Max(st.Peak, v);
                if (v < rule.Limit - rule.Hysteresis)
                {
                    st.Active = false;
                    st.AboveSince = null;
                    output.Add(Notification.Recovered(rule, v, st.Peak));
                }
                else if ((now - st.LastNotified).TotalMinutes >= rule.CooldownMin)
                {
                    st.LastNotified = now;
                    output.Add(Notification.Alert(rule, v, true));
                }
            }
        }
        return output;
    }
}
