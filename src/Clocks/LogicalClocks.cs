using System.Globalization;

namespace Clocks;

/// <summary>Lamport logical clock (thread-safe).</summary>
public sealed class LamportClock
{
    private readonly object _lock = new();
    private long _value;

    public LamportClock(long initial = 0) => _value = initial;

    public long Value
    {
        get { lock (_lock) return _value; }
    }

    /// <summary>Internal event or send: C = C + 1. The returned value travels with the message.</summary>
    public long Tick()
    {
        lock (_lock) return ++_value;
    }

    /// <summary>Receive: C = max(C, received) + 1.</summary>
    public long Receive(long received)
    {
        lock (_lock)
        {
            _value = Math.Max(_value, received) + 1;
            return _value;
        }
    }
}

/// <summary>
/// Vector clock, used only to check which events are really concurrent (something Lamport
/// timestamps alone cannot tell).
/// </summary>
public sealed class VectorClock
{
    private readonly object _lock = new();
    private readonly string _self;
    private readonly SortedDictionary<string, long> _entries;

    public VectorClock(string self, IEnumerable<string> processes)
    {
        _self = self;
        _entries = new SortedDictionary<string, long>(processes.ToDictionary(p => p, _ => 0L), StringComparer.Ordinal);
        _entries.TryAdd(self, 0);
    }

    public string Tick()
    {
        lock (_lock)
        {
            _entries[_self]++;
            return Format(_entries);
        }
    }

    public string Receive(string received)
    {
        lock (_lock)
        {
            foreach (var (process, value) in Parse(received))
            {
                _entries[process] = Math.Max(_entries.GetValueOrDefault(process), value);
            }
            _entries[_self]++;
            return Format(_entries);
        }
    }

    public static string Format(IEnumerable<KeyValuePair<string, long>> entries) =>
        string.Join(",", entries.Select(e => $"{e.Key}={e.Value.ToString(CultureInfo.InvariantCulture)}"));

    public static Dictionary<string, long> Parse(string text) =>
        text.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('='))
            .ToDictionary(kv => kv[0], kv => long.Parse(kv[1], CultureInfo.InvariantCulture));

    /// <summary>a happened-before b: a &lt;= b component-wise and a != b.</summary>
    public static bool HappenedBefore(string a, string b)
    {
        var va = Parse(a);
        var vb = Parse(b);
        var keys = va.Keys.Union(vb.Keys).ToList();
        return keys.All(k => va.GetValueOrDefault(k) <= vb.GetValueOrDefault(k)) &&
               keys.Any(k => va.GetValueOrDefault(k) < vb.GetValueOrDefault(k));
    }

    public static bool Concurrent(string a, string b) => !HappenedBefore(a, b) && !HappenedBefore(b, a) && a != b;
}
