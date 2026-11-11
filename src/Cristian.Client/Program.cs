// Exercise 1 - Client of Cristian's algorithm.
//
// For each synchronization it:
//   1. asks the time server for its time (REST),
//   2. records the local send instant (T0) and 3. the local receive instant (T1),
//   4. computes the round trip time (RTT, measured with a monotonic Stopwatch),
//   5. estimates the current time as serverTime + RTT/2,
//   6. adjusts its application clock (never the OS clock) and
//   7. prints the time before and after the adjustment.
// It takes several samples and keeps the one with the lowest RTT (samples above MAX_RTT_MS
// are discarded).
//
// Environment:
//   CLIENT_NAME, TIME_SERVER_URL, CLOCK_OFFSET_SECONDS, CLOCK_DRIFT_PPM
//   PHASES                normal,simetrica,asimetrica,alta (simulated network conditions)
//   SAMPLES               samples per synchronization (default 3)
//   MAX_RTT_MS            samples with a higher RTT are discarded (default 1000)
//   ROUNDS, SYNC_INTERVAL_SECONDS  periodic resynchronization after the phases
//   EVAL_SERVER_OFFSET_SECONDS, EVAL_SERVER_DRIFT_PPM
//       ONLY used to print the real error: the algorithm never reads them.
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using Clocks;

var name = Env("CLIENT_NAME", "cliente");
var serverUrl = Env("TIME_SERVER_URL", "http://time-server:8080");
var samplesPerSync = int.Parse(Env("SAMPLES", "3"), CultureInfo.InvariantCulture);
var maxRtt = TimeSpan.FromMilliseconds(double.Parse(Env("MAX_RTT_MS", "1000"), CultureInfo.InvariantCulture));
var rounds = int.Parse(Env("ROUNDS", "0"), CultureInfo.InvariantCulture);
var interval = TimeSpan.FromSeconds(double.Parse(Env("SYNC_INTERVAL_SECONDS", "5"), CultureInfo.InvariantCulture));
var phases = Env("PHASES", "normal").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

var clock = SimulatedClock.FromEnvironment();
// Reference used only to measure how good the estimate was.
var serverTruth = SimulatedClock.FromEnvironment("EVAL_SERVER_OFFSET_SECONDS", "EVAL_SERVER_DRIFT_PPM");

using var http = new HttpClient { BaseAddress = new Uri(serverUrl), Timeout = TimeSpan.FromSeconds(4) };

var networks = new Dictionary<string, (int Request, int Response, string Description)>
{
    ["normal"] = (0, 0, "red Docker sin latencia agregada"),
    ["simetrica"] = (200, 200, "latencia simetrica: 200 ms de ida y 200 ms de vuelta"),
    ["asimetrica"] = (50, 450, "latencia asimetrica: 50 ms de ida y 450 ms de vuelta"),
    ["alta"] = (1500, 1500, $"RTT muy alto: 1500 ms de ida y 1500 ms de vuelta (limite {maxRtt.TotalMilliseconds} ms)"),
};

Log($"iniciado. Hora local {TimeFormat.Time(clock.Now)} | servidor {serverUrl} | deriva {clock.DriftPpm} ppm");
Log($"diferencia real con el servidor antes de sincronizar: {TimeFormat.Delta(clock.Now - serverTruth.Now)}");
await WaitForServer();

foreach (var phase in phases)
{
    var network = networks[phase];
    Log($"=== Fase '{phase}': {network.Description} ===");
    await Synchronize(network.Request, network.Response);
}

for (var round = 1; round <= rounds; round++)
{
    await Task.Delay(interval);
    Log($"=== Resincronizacion periodica {round}/{rounds} ===");
    await Synchronize(0, 0);
}

Log("fin");
return;

async Task Synchronize(int requestDelayMs, int responseDelayMs)
{
    var samples = new List<CristianSample>();
    for (var i = 1; i <= samplesPerSync; i++)
    {
        var t0 = clock.Now;                      // 2. local send instant
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var reply = await http.GetFromJsonAsync<TimeReply>(
                $"/api/time?client={name}&requestDelayMs={requestDelayMs}&responseDelayMs={responseDelayMs}");
            stopwatch.Stop();
            var t1 = clock.Now;                  // 3. local receive instant
            var sample = new CristianSample(t0, t1, reply!.ServerTime, stopwatch.Elapsed);   // 4. RTT
            samples.Add(sample);
            Log($"  muestra {i}: T0={TimeFormat.Time(t0)} T1={TimeFormat.Time(t1)} " +
                $"servidor={TimeFormat.Time(sample.ServerTime)} RTT={TimeFormat.Ms(sample.Rtt)}" +
                (sample.Rtt > maxRtt ? "  -> DESCARTADA (RTT mayor al limite)" : ""));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Log($"  muestra {i}: el servidor de tiempo NO responde ({ex.GetType().Name}: {ex.Message.Split('\n')[0]})");
        }
    }

    var best = Cristian.BestSample(samples, maxRtt);
    if (best is null)
    {
        Log($"  sin muestras validas: se mantiene el reloj (correccion acumulada {TimeFormat.Delta(clock.Correction)}). " +
            $"Error real actual: {TimeFormat.SignedMs(clock.Now - serverTruth.Now)}");
        return;
    }

    var before = clock.Now;
    clock.Adjust(best.Offset);                   // 6. adjust the application clock
    var after = clock.Now;
    Log($"  mejor muestra RTT={TimeFormat.Ms(best.Rtt)} -> hora estimada = servidor + RTT/2 (precision +/-{TimeFormat.Ms(best.Accuracy)})");
    Log($"  antes {TimeFormat.Time(before)} | despues {TimeFormat.Time(after)} | ajuste {TimeFormat.Delta(best.Offset)} | " +
        $"error real {TimeFormat.SignedMs(after - serverTruth.Now)}");
}

async Task WaitForServer()
{
    for (var i = 0; i < 30; i++)
    {
        try
        {
            using var response = await http.GetAsync("/health");
            if (response.IsSuccessStatusCode) return;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
        }
        await Task.Delay(500);
    }
}

void Log(string message) => Console.WriteLine($"[{name} {TimeFormat.Time(clock.Now)}] {message}");

static string Env(string variable, string fallback) =>
    Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value ? value : fallback;

internal sealed record TimeReply(string Server, DateTime ServerTime);
