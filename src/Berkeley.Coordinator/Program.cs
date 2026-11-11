// Exercise 2 - Coordinator of Berkeley's algorithm.
//
// Every INTERVAL_SECONDS it:
//   1. asks every node for its current time,
//   2. computes each difference with its own clock (compensating RTT/2),
//   3. averages the differences including its own (0),
//   4. computes the adjustment of each server (average - difference),
//   5. sends each node its adjustment and applies its own,
//   6. repeats after the configured interval.
//
// Environment:
//   COORDINATOR_NAME, NODES (comma separated URLs), CLOCK_OFFSET_SECONDS, CLOCK_DRIFT_PPM
//   INTERVAL_SECONDS (default 5), ROUNDS (0 = forever)
//   OUTLIER_THRESHOLD_SECONDS  0 = plain average; > 0 = fault-tolerant average that leaves out
//                              clocks that differ from the median by more than the threshold
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using Clocks;

var name = Env("COORDINATOR_NAME", "coordinador");
var nodes = Env("NODES", "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var interval = TimeSpan.FromSeconds(double.Parse(Env("INTERVAL_SECONDS", "5"), CultureInfo.InvariantCulture));
var rounds = int.Parse(Env("ROUNDS", "0"), CultureInfo.InvariantCulture);
var threshold = TimeSpan.FromSeconds(double.Parse(Env("OUTLIER_THRESHOLD_SECONDS", "0"), CultureInfo.InvariantCulture));
var clock = SimulatedClock.FromEnvironment();

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

Log($"iniciado. Hora local {TimeFormat.Time(clock.Now)} | nodos: {string.Join(", ", nodes)} | intervalo {interval.TotalSeconds} s | " +
    (threshold > TimeSpan.Zero
        ? $"promedio tolerante a fallas (descarta relojes a mas de {threshold.TotalSeconds} s de la mediana)"
        : "promedio simple"));
await Task.Delay(TimeSpan.FromSeconds(2)); // let the nodes start

for (var round = 1; rounds == 0 || round <= rounds; round++)
{
    await RunRound(round);
    if (rounds == 0 || round < rounds) await Task.Delay(interval);
}

Log("fin");
return;

async Task RunRound(int round)
{
    Log($"===== Ronda {round} =====");

    // 1-2. Poll every node in parallel and compute its difference with the coordinator.
    var polls = await Task.WhenAll(nodes.Select(Poll));
    var readings = new List<BerkeleyReading> { new(name, TimeSpan.Zero) };
    readings.AddRange(polls.Select(p => new BerkeleyReading(p.Name, p.Difference)));
    var coordinatorNow = clock.Now;

    // 3-4. Average and adjustments.
    var result = Berkeley.Compute(readings, threshold);

    // Every clock is shown at the same instant (coordinator time + difference), so the columns compare.
    Log($"{"participante",-14}{"hora",-15}{"diferencia",-13}{"en promedio",-13}{"ajuste",-13}hora ajustada");
    foreach (var adjustment in result.Adjustments)
    {
        if (adjustment.Difference is null)
        {
            var error = polls.FirstOrDefault(p => p.Name == adjustment.Name)?.Error;
            Log($"{adjustment.Name,-14}{"SIN RESPUESTA",-15}{"-",-13}{"no",-13}{"-",-13}-  ({error})");
            continue;
        }
        var read = coordinatorNow + adjustment.Difference.Value;
        Log($"{adjustment.Name,-14}{TimeFormat.Time(read),-15}{TimeFormat.Delta(adjustment.Difference.Value),-13}" +
            $"{(adjustment.Included ? "si" : "NO (atipico)"),-13}{TimeFormat.Delta(adjustment.Adjustment!.Value),-13}" +
            $"{TimeFormat.Time(read + adjustment.Adjustment.Value)}");
    }

    var answered = readings.Where(r => r.Difference.HasValue).Select(r => r.Difference!.Value).ToList();
    Log($"promedio de diferencias: {TimeFormat.Delta(result.Average)} | " +
        $"dispersion antes del ajuste (max - min): {TimeFormat.Delta(answered.Max() - answered.Min())}");

    // 5. Send each node its adjustment; apply the coordinator's own.
    foreach (var adjustment in result.Adjustments.Where(a => a.Adjustment.HasValue))
    {
        if (adjustment.Name == name)
        {
            clock.Adjust(adjustment.Adjustment!.Value);
            continue;
        }
        var url = polls.First(p => p.Name == adjustment.Name).Url;
        try
        {
            using var response = await http.PostAsJsonAsync($"{url}/api/clock/adjust",
                new { AdjustmentMs = adjustment.Adjustment!.Value.TotalMilliseconds });
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Log($"no se pudo enviar el ajuste a {adjustment.Name}: {ex.Message}");
        }
    }
}

async Task<Poll> Poll(string url)
{
    var host = new Uri(url).Host;
    var stopwatch = Stopwatch.StartNew();
    try
    {
        var reply = await http.GetFromJsonAsync<ClockReply>($"{url}/api/clock");
        stopwatch.Stop();
        // Same idea as Cristian: the node read its clock about RTT/2 before the reply arrived.
        var estimated = reply!.Time + stopwatch.Elapsed / 2;
        return new Poll(host, url, estimated - clock.Now, null);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        return new Poll(host, url, null, ex is TaskCanceledException ? "timeout" : ex.Message.Split('(')[0].Trim());
    }
}

void Log(string message) => Console.WriteLine($"[{name} {TimeFormat.Time(clock.Now)}] {message}");

static string Env(string variable, string fallback) =>
    Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value ? value : fallback;

internal sealed record ClockReply(string Name, DateTime Time);

internal sealed record Poll(string Name, string Url, TimeSpan? Difference, string? Error);
