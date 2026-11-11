using System.Text;
using Clocks;

namespace Lamport.Orders;

/// <summary>Builds the merged report of the three services.</summary>
public static class Timeline
{
    public static string Render(IReadOnlyList<LoggedEvent> events)
    {
        var text = new StringBuilder();

        // Lamport gives a partial order; the process name breaks ties to get a total order.
        var byLamport = events.OrderBy(e => e.Lamport).ThenBy(e => e.Process, StringComparer.Ordinal).ToList();
        text.AppendLine("=== Orden total por reloj de Lamport (desempate por nombre de proceso) ===");
        AppendTable(text, byLamport);

        var byPhysical = events.OrderBy(e => e.Physical).ToList();
        text.AppendLine();
        text.AppendLine("=== Orden por reloj fisico (hora local de cada servicio) ===");
        AppendTable(text, byPhysical);

        // a -> b (by vector clocks) but b has an earlier physical timestamp than a.
        var violations = (from a in events
                          from b in events
                          where VectorClock.HappenedBefore(a.Vector, b.Vector) && b.Physical < a.Physical
                          select (a, b)).ToList();
        text.AppendLine();
        text.AppendLine($"=== Inconsistencias del reloj fisico: {violations.Count} pares donde a -> b pero hora(b) < hora(a) ===");
        // Show each message as (send -> receive): b is the first event of its process caused by a,
        // and a is the latest event of the other process that b depends on.
        var closest = violations
            .Where(v => v.a.Process != v.b.Process)
            .GroupBy(v => v.b)
            .Select(g => g.MaxBy(v => v.a.Lamport))
            .Where(v => !events.Any(e => e.Process == v.b.Process && e.Lamport < v.b.Lamport &&
                                         VectorClock.HappenedBefore(v.a.Vector, e.Vector)))
            .OrderBy(v => v.b.Lamport)
            .Take(6);
        foreach (var (a, b) in closest)
        {
            text.AppendLine($"  {Describe(a)}  ->  {Describe(b)}  (hora(b) - hora(a) = {TimeFormat.Delta(b.Physical - a.Physical)})");
        }

        // Lamport says L(a) < L(b), but the events are not causally related.
        var concurrent = (from a in events
                          from b in events
                          where a.Lamport < b.Lamport && VectorClock.Concurrent(a.Vector, b.Vector)
                          select (a, b)).ToList();
        text.AppendLine();
        text.AppendLine($"=== Eventos concurrentes con L(a) < L(b): {concurrent.Count} pares (Lamport no los distingue) ===");
        foreach (var (a, b) in concurrent.Where(v => v.a.Process != v.b.Process).Take(4))
        {
            text.AppendLine($"  {Describe(a)}  ||  {Describe(b)}");
        }

        return text.ToString();
    }

    private static string Describe(LoggedEvent e) => $"{Short(e.Process)} L={e.Lamport} [{e.Vector}] \"{Cut(e.Event, 38)}\"";

    private static void AppendTable(StringBuilder text, IEnumerable<LoggedEvent> events)
    {
        text.AppendLine($"{"proceso",-18}{"reloj fisico",-15}{"Lamport",-9}{"vector",-16}evento");
        foreach (var e in events)
        {
            text.AppendLine($"{e.Process,-18}{TimeFormat.Time(e.Physical),-15}{e.Lamport,-9}{e.Vector,-16}{Cut(e.Event, 95)}");
        }
    }

    private static string Short(string process) => process.Replace("Service", "");

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..(max - 3)] + "...";
}
