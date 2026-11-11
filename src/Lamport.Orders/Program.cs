// Exercise 3 - OrdersService: receives orders and coordinates inventory and payment.
//
// POST /api/orders    { productId, quantity, amount }  runs the 9-step sequence of the case study
// GET  /api/events    -> event log of this service
// GET  /api/timeline  -> events of the three services merged and ordered by Lamport and by
//                        physical time (text report). Reading the logs is an observation, not
//                        part of the computation, so it does not touch the clocks.
using System.Net.Http.Json;
using Clocks;
using Lamport.Orders;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient("inventory", c => c.BaseAddress = new Uri(Env("INVENTORY_URL", "http://inventory-service:8080")));
builder.Services.AddHttpClient("payments", c => c.BaseAddress = new Uri(Env("PAYMENTS_URL", "http://payments-service:8080")));
var app = builder.Build();

var process = new EventProcess("OrdersService", "O", ["I", "O", "P"], SimulatedClock.FromEnvironment());
var nextOrder = 0;

app.MapPost("/api/orders", async (OrderRequest request, IHttpClientFactory factory) =>
{
    var orderId = $"ORD-{Interlocked.Increment(ref nextOrder):000}";

    // 1. OrdersService recibe un pedido
    process.Internal($"Pedido recibido {orderId}: {request.Quantity} x {request.ProductId}");

    // 2. OrdersService envia una solicitud a InventoryService
    var reserve = await Exchange(factory.CreateClient("inventory"), "/api/inventory/reserve",
        new { OrderId = orderId, request.ProductId, request.Quantity },
        $"Mensaje enviado a InventoryService (pedido {orderId})",
        $"Respuesta de InventoryService recibida (pedido {orderId})");
    var reservation = await reserve.ReadFromJsonAsync<ReserveReply>();

    if (reservation is not { Reserved: true })
    {
        process.Internal($"Pedido {orderId} rechazado: sin stock");
        return Results.Conflict(new { OrderId = orderId, Status = "REJECTED" });
    }

    // 6. OrdersService solicita el procesamiento del pago
    var payment = await Exchange(factory.CreateClient("payments"), "/api/payments",
        new { OrderId = orderId, request.Amount },
        $"Solicitud de pago enviada a PaymentsService (pedido {orderId})",
        $"Confirmacion de pago recibida (pedido {orderId})");
    await payment.ReadFromJsonAsync<object>();

    // 9. OrdersService confirma el pedido
    var confirmed = process.Internal($"Pedido {orderId} confirmado");
    return Results.Ok(new { OrderId = orderId, Status = "CONFIRMED", confirmed.Lamport });
});

app.MapGet("/api/events", () => Results.Ok(process.Events));

app.MapGet("/api/timeline", async (IHttpClientFactory factory) =>
{
    var events = new List<LoggedEvent>(process.Events);
    events.AddRange(await factory.CreateClient("inventory").GetFromJsonAsync<List<LoggedEvent>>("/api/events") ?? []);
    events.AddRange(await factory.CreateClient("payments").GetFromJsonAsync<List<LoggedEvent>>("/api/events") ?? []);
    return Results.Text(Timeline.Render(events));
});

app.MapGet("/health", () => Results.Ok(new { Status = "UP" }));

Console.WriteLine($"[{process.Name}] iniciado, reloj fisico {TimeFormat.Time(process.Physical.Now)}");
app.Run();
return;

// Sends a message with the clocks in the headers and records the reply as a receive event.
async Task<HttpContent> Exchange(HttpClient client, string path, object body, string sendEvent, string receiveEvent)
{
    var sent = process.Send(sendEvent);
    using var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
    message.Headers.Add(EventProcess.LamportHeader, sent.Lamport.ToString());
    message.Headers.Add(EventProcess.VectorHeader, sent.Vector);
    var response = await client.SendAsync(message);
    process.Receive(receiveEvent,
        long.Parse(response.Headers.GetValues(EventProcess.LamportHeader).First()),
        response.Headers.GetValues(EventProcess.VectorHeader).First());
    return response.Content;
}

static string Env(string variable, string fallback) =>
    Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value ? value : fallback;

internal sealed record OrderRequest(string ProductId, int Quantity, decimal Amount);

internal sealed record ReserveReply(string OrderId, bool Reserved, int Available);
