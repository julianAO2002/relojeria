// Exercise 2 - Participant node of Berkeley's algorithm.
//
// GET  /api/clock          -> current time of the node's simulated clock
// POST /api/clock/adjust   -> { "adjustmentMs": 1234.5 } applies the adjustment sent by the coordinator
using Clocks;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var name = Environment.GetEnvironmentVariable("NODE_NAME") ?? "nodo";
var clock = SimulatedClock.FromEnvironment();

app.MapGet("/api/clock", () => Results.Ok(new ClockReply(name, clock.Now)));

app.MapPost("/api/clock/adjust", (AdjustRequest request) =>
{
    var adjustment = TimeSpan.FromMilliseconds(request.AdjustmentMs);
    var before = clock.Now;
    clock.Adjust(adjustment);
    var after = clock.Now;
    Console.WriteLine($"[{name}] ajuste recibido {TimeFormat.Delta(adjustment)}: " +
                      $"{TimeFormat.Time(before)} -> {TimeFormat.Time(after)}");
    return Results.Ok(new ClockReply(name, after));
});

app.MapGet("/health", () => Results.Ok(new { Status = "UP" }));

Console.WriteLine($"[{name}] nodo iniciado, hora local {TimeFormat.Time(clock.Now)} (deriva {clock.DriftPpm} ppm)");
app.Run();

internal sealed record ClockReply(string Name, DateTime Time);

internal sealed record AdjustRequest(double AdjustmentMs);
