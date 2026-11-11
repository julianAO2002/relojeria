// Exercise 3 - InventoryService: verifies and reserves products.
//
// POST /api/inventory/reserve  { orderId, productId, quantity }
//      headers X-Lamport-Clock / X-Vector-Clock with the sender's clocks;
//      the response carries this service's clocks in the same headers.
// GET  /api/events             -> event log (physical time, Lamport, vector)
using System.Collections.Concurrent;
using Clocks;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var process = new EventProcess("InventoryService", "I", ["I", "O", "P"], SimulatedClock.FromEnvironment());
var stock = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase)
{
    ["P001"] = 10,
    ["P002"] = 3,
    ["P003"] = 0,
};

app.MapPost("/api/inventory/reserve", (ReserveRequest request, HttpRequest http, HttpResponse response) =>
{
    // 3. InventoryService registra la recepcion
    process.Receive($"Mensaje recibido de OrdersService (pedido {request.OrderId})",
        long.Parse(http.Headers[EventProcess.LamportHeader]!), http.Headers[EventProcess.VectorHeader]!);

    // 4. InventoryService verifica el inventario
    var available = stock.GetValueOrDefault(request.ProductId);
    var reserved = available >= request.Quantity &&
                   stock.TryUpdate(request.ProductId, available - request.Quantity, available);
    process.Internal($"Inventario verificado: {request.ProductId} stock {available}, pedido {request.Quantity} -> " +
                     (reserved ? "reservado" : "sin stock"));

    // 5. InventoryService envia una respuesta
    var sent = process.Send($"Respuesta enviada a OrdersService (pedido {request.OrderId})");
    response.Headers[EventProcess.LamportHeader] = sent.Lamport.ToString();
    response.Headers[EventProcess.VectorHeader] = sent.Vector;
    return Results.Ok(new { request.OrderId, Reserved = reserved, Available = available });
});

app.MapGet("/api/events", () => Results.Ok(process.Events));
app.MapGet("/health", () => Results.Ok(new { Status = "UP" }));

Console.WriteLine($"[{process.Name}] iniciado, reloj fisico {TimeFormat.Time(process.Physical.Now)}");
app.Run();

internal sealed record ReserveRequest(string OrderId, string ProductId, int Quantity);
