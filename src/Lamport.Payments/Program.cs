// Exercise 3 - PaymentsService: processes payments.
//
// POST /api/payments  { orderId, amount }  (with X-Lamport-Clock / X-Vector-Clock headers)
// GET  /api/events    -> event log
//
// PRELOAD_EVENTS: internal events executed at startup (payments of other, unrelated orders),
// so this process starts with a Lamport clock ahead of the others.
using System.Globalization;
using Clocks;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var process = new EventProcess("PaymentsService", "P", ["I", "O", "P"], SimulatedClock.FromEnvironment());
var preload = int.Parse(Environment.GetEnvironmentVariable("PRELOAD_EVENTS") ?? "0", CultureInfo.InvariantCulture);
for (var i = 1; i <= preload; i++)
{
    process.Internal($"Pago de otro pedido procesado (#{i}, sin relacion con este flujo)");
}

app.MapPost("/api/payments", async (PaymentRequest request, HttpRequest http, HttpResponse response) =>
{
    // 7. PaymentsService registra y procesa la solicitud
    process.Receive($"Solicitud de pago recibida (pedido {request.OrderId})",
        long.Parse(http.Headers[EventProcess.LamportHeader]!), http.Headers[EventProcess.VectorHeader]!);
    await Task.Delay(50); // talking to the payment gateway
    process.Internal($"Pago procesado: ${request.Amount.ToString("0.00", CultureInfo.InvariantCulture)} aprobado");

    // 8. PaymentsService envia una confirmacion
    var sent = process.Send($"Confirmacion enviada a OrdersService (pedido {request.OrderId})");
    response.Headers[EventProcess.LamportHeader] = sent.Lamport.ToString();
    response.Headers[EventProcess.VectorHeader] = sent.Vector;
    return Results.Ok(new { request.OrderId, Approved = true });
});

app.MapGet("/api/events", () => Results.Ok(process.Events));
app.MapGet("/health", () => Results.Ok(new { Status = "UP" }));

Console.WriteLine($"[{process.Name}] iniciado, reloj fisico {TimeFormat.Time(process.Physical.Now)}");
app.Run();

internal sealed record PaymentRequest(string OrderId, decimal Amount);
