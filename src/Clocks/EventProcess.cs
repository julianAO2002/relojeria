namespace Clocks;

public sealed record LoggedEvent(string Process, string Event, DateTime Physical, long Lamport, string Vector);

/// <summary>
/// A process of the distributed system: keeps its physical clock, its Lamport clock and a vector
/// clock, and logs every event with the three values. Every operation is done under one lock so
/// the log order of a process always matches its clock order.
/// </summary>
public sealed class EventProcess
{
    public const string LamportHeader = "X-Lamport-Clock";
    public const string VectorHeader = "X-Vector-Clock";

    private readonly object _lock = new();
    private readonly List<LoggedEvent> _events = [];
    private readonly LamportClock _lamport = new();
    private readonly VectorClock _vector;

    public EventProcess(string name, string vectorKey, IEnumerable<string> vectorKeys, SimulatedClock physical)
    {
        Name = name;
        Physical = physical;
        _vector = new VectorClock(vectorKey, vectorKeys);
    }

    public string Name { get; }

    public SimulatedClock Physical { get; }

    public IReadOnlyList<LoggedEvent> Events
    {
        get { lock (_lock) return _events.ToList(); }
    }

    /// <summary>Rule 1: internal event, C = C + 1.</summary>
    public LoggedEvent Internal(string description)
    {
        lock (_lock) return Record(description, _lamport.Tick(), _vector.Tick());
    }

    /// <summary>Rule 2: before sending, C = C + 1; the message carries C (and the vector).</summary>
    public LoggedEvent Send(string description)
    {
        lock (_lock) return Record(description, _lamport.Tick(), _vector.Tick());
    }

    /// <summary>Rule 3: on receive, C = max(C, received) + 1.</summary>
    public LoggedEvent Receive(string description, long receivedLamport, string receivedVector)
    {
        lock (_lock)
        {
            var before = _lamport.Value;
            var lamport = _lamport.Receive(receivedLamport);
            var note = receivedLamport > before
                ? $" (recibido L={receivedLamport} > local L={before}: salta a {lamport})"
                : $" (recibido L={receivedLamport} <= local L={before}: local + 1)";
            return Record(description + note, lamport, _vector.Receive(receivedVector));
        }
    }

    private LoggedEvent Record(string description, long lamport, string vector)
    {
        var logged = new LoggedEvent(Name, description, Physical.Now, lamport, vector);
        _events.Add(logged);
        Console.WriteLine($"[{Name}] fisico {TimeFormat.Time(logged.Physical)} | Lamport {lamport,3} | vector [{vector}] | {description}");
        return logged;
    }
}
