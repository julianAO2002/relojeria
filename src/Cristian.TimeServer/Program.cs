// Exercise 1 - Time server for Cristian's algorithm.
//
// GET /api/time                    -> current time of the server clock
// GET /api/time?requestDelayMs=X&responseDelayMs=Y
//      simulates network latency: waits X ms before reading the clock (request travelling
//      to the server) and Y ms after reading it (response travelling back).
using Clocks;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var name = Environment.GetEnvironmentVariable("SERVER_NAME") ?? "time-server";
var clock = SimulatedClock.FromEnvironment();

app.MapGet("/api/time", async (int? requestDelayMs, int? responseDelayMs, string? client) =>
{
    if (requestDelayMs > 0) await Task.Delay(requestDelayMs.Value);
    var now = clock.Now;
    Console.WriteLine($"[{name}] hora {TimeFormat.Time(now)} -> {client ?? "?"}" +
                      (requestDelayMs > 0 || responseDelayMs > 0
                          ? $" (latencia simulada: ida {requestDelayMs ?? 0} ms, vuelta {responseDelayMs ?? 0} ms)"
                          : ""));
    if (responseDelayMs > 0) await Task.Delay(responseDelayMs.Value);
    return Results.Ok(new { Server = name, ServerTime = now });
});

app.MapGet("/health", () => Results.Ok(new { Status = "UP" }));

Console.WriteLine($"[{name}] servidor de tiempo iniciado, hora local {TimeFormat.Time(clock.Now)}");
app.Run();
