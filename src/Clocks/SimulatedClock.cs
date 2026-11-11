using System.Globalization;

namespace Clocks;

/// <summary>
/// Application-level clock. It never touches the operating system clock: it is the real
/// UTC time plus an initial offset, a drift (ppm) and the corrections applied by the
/// synchronization algorithms.
///
/// All containers share the host kernel clock, so the skew is simulated here.
/// When SCENARIO_START_UTC is set, that real instant is shown as SCENARIO_BASE_TIME
/// (10:00:00 by default), so the logs match the times of the case study.
/// </summary>
public sealed class SimulatedClock
{
    private static readonly DateTime BaseDay = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly DateTime _startUtc;
    private readonly DateTime _shownAtStart;
    private readonly TimeSpan _initialOffset;
    private readonly double _driftPpm;
    private readonly Func<DateTime> _utcNow;
    private long _correctionTicks;

    public SimulatedClock(TimeSpan initialOffset, double driftPpm = 0, DateTime? scenarioStartUtc = null,
        TimeSpan? baseTime = null, Func<DateTime>? utcNow = null)
    {
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _initialOffset = initialOffset;
        _driftPpm = driftPpm;
        if (scenarioStartUtc is { } start)
        {
            _startUtc = start;
            _shownAtStart = BaseDay + (baseTime ?? new TimeSpan(10, 0, 0));
        }
        else
        {
            // Without a scenario, the clock shows real UTC time (+ offset).
            _startUtc = _utcNow().Date;
            _shownAtStart = _startUtc;
        }
    }

    /// <summary>Reads CLOCK_OFFSET_SECONDS, CLOCK_DRIFT_PPM, SCENARIO_START_UTC and SCENARIO_BASE_TIME.</summary>
    public static SimulatedClock FromEnvironment(string offsetVariable = "CLOCK_OFFSET_SECONDS",
        string driftVariable = "CLOCK_DRIFT_PPM")
    {
        var offset = TimeSpan.FromSeconds(ReadDouble(offsetVariable));
        var drift = ReadDouble(driftVariable);
        DateTime? start = null;
        var startText = Environment.GetEnvironmentVariable("SCENARIO_START_UTC");
        if (!string.IsNullOrWhiteSpace(startText))
        {
            start = DateTime.Parse(startText, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        }
        var baseText = Environment.GetEnvironmentVariable("SCENARIO_BASE_TIME");
        TimeSpan? baseTime = string.IsNullOrWhiteSpace(baseText)
            ? null
            : TimeSpan.Parse(baseText, CultureInfo.InvariantCulture);
        return new SimulatedClock(offset, drift, start, baseTime);
    }

    private static double ReadDouble(string variable)
    {
        var text = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrWhiteSpace(text) ? 0 : double.Parse(text, CultureInfo.InvariantCulture);
    }

    public TimeSpan Correction => TimeSpan.FromTicks(Interlocked.Read(ref _correctionTicks));

    public double DriftPpm => _driftPpm;

    public DateTime Now
    {
        get
        {
            var elapsed = _utcNow() - _startUtc;
            var drift = TimeSpan.FromTicks((long)(elapsed.Ticks * _driftPpm / 1_000_000));
            return _shownAtStart + elapsed + _initialOffset + drift + Correction;
        }
    }

    /// <summary>Applies a correction to the application clock (the OS clock is not modified).</summary>
    public void Adjust(TimeSpan delta) => Interlocked.Add(ref _correctionTicks, delta.Ticks);
}
