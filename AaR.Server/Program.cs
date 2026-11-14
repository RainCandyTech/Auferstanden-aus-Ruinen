using AaR.Server;
using AaR.Server.Services;
using AaR.Server.Hubs;
using AaR.Server.Middleware;
using AaR.Server.Ob11;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AaROptions>(builder.Configuration.GetSection("AaR"));
builder.Services.AddSingleton<AaROptions>(_ =>
    builder.Configuration.GetSection("AaR").Get<AaROptions>() ?? new AaROptions());
builder.Services.Configure<ReverseOptions>(builder.Configuration.GetSection("AaR:Reverse"));
builder.Services.AddSingleton<ReverseOptions>(_ =>
    builder.Configuration.GetSection("AaR:Reverse").Get<ReverseOptions>() ?? new ReverseOptions());
builder.Services.AddHostedService<ReversePushService>();
builder.Services.AddSingleton<BridgeRegistry>();
builder.Services.AddSingleton<Ob11Server>();
builder.Services.AddSignalR(o =>
{
    o.EnableDetailedErrors = true;
    o.KeepAliveInterval = TimeSpan.FromSeconds(10);
});
// kernel RPC payloads (message history records) exceed the 32KB default
builder.Services.Configure<Microsoft.AspNetCore.SignalR.HubOptions>(o =>
{
    o.MaximumReceiveMessageSize = 4 * 1024 * 1024;
    o.MaximumParallelInvocationsPerClient = 10;
});

var app = builder.Build();

// graceful shutdown: SIGTERM (systemd/pkill) cancels background loops cleanly
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    try
    {
        var ob = app.Services.GetRequiredService<Ob11Server>();
        var f = typeof(Ob11Server).GetField("_shutdownCts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        (f?.GetValue(ob) as CancellationTokenSource)?.Cancel();
    }
    catch { }
});

var opts = app.Services.GetRequiredService<AaROptions>();
var registry = app.Services.GetRequiredService<BridgeRegistry>();
var ob11 = app.Services.GetRequiredService<Ob11Server>();

// ---------- OB11 HTTP endpoints ----------
// POST /api/{action} and POST /{action}; GET variant with query params.
async Task<IResult> HandleOb11Action(HttpContext ctx, string action, Dictionary<string, object?> parameters)
{
    if (string.IsNullOrWhiteSpace(action)) return Results.BadRequest();
    var echo = ctx.Request.Query["echo"].ToString();
    if (string.IsNullOrEmpty(echo) && ctx.Request.Headers.TryGetValue("x-echo", out var hEcho)) echo = hEcho;

    var response = await ob11.HandleActionAsync(action, parameters, echo);
    return Results.Json(response, contentType: "application/json; charset=utf-8");
}

async Task<Dictionary<string, object?>> ReadJsonBody(HttpContext ctx)
{
    var dict = new Dictionary<string, object?>();
    try
    {
        using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
        foreach (var p in doc.RootElement.EnumerateObject())
            dict[p.Name] = p.Value.Clone();
    }
    catch { /* empty body allowed */ }
    return dict;
}

Dictionary<string, object?> QueryToParams(HttpContext ctx)
{
    var dict = new Dictionary<string, object?>();
    foreach (var (k, v) in ctx.Request.Query)
        dict[k] = v.ToString();
    return dict;
}

app.MapPost("/api/{action}", async (HttpContext ctx, string action) =>
{
    var p = await ReadJsonBody(ctx);
    foreach (var (k, v) in QueryToParams(ctx)) p.TryAdd(k, v);
    return await HandleOb11Action(ctx, action, p);
});
app.MapPost("/{action}", async (HttpContext ctx, string action) =>
{
    if (IsReservedPath(action)) return Results.NotFound();
    var p = await ReadJsonBody(ctx);
    foreach (var (k, v) in QueryToParams(ctx)) p.TryAdd(k, v);
    return await HandleOb11Action(ctx, action, p);
});
app.MapGet("/api/{action}", async (HttpContext ctx, string action) =>
    await HandleOb11Action(ctx, action, QueryToParams(ctx)));

static bool IsReservedPath(string path) => path is
    "hub" or "ws" or "health" or "api" or "negotiate" or "favicon.ico";

app.MapGet("/health", () => Results.Json(new
{
    ok = true,
    bridgeConnected = registry.IsBridgeConnected,
    eventsReceived = ob11.EventsReceived,
    eventsBroadcast = ob11.EventsBroadcast,
    wsClients = ob11.WsClientCount,
}));

// ---------- OB11 forward WebSocket clients ----------
app.UseWebSockets(); // enables Kestrel's IHttpUpgradeFeature
var wsMiddleware = new Ob11WebSocketMiddleware(app.Services.GetRequiredService<ILogger<Ob11WebSocketMiddleware>>(), ob11, opts);
app.Use(next => ctx => wsMiddleware.InvokeAsync(ctx, next));

// ---------- SignalR hub for the bridge ----------
app.MapHub<AaRHub>(opts.BridgeHubPath);

app.Run();
