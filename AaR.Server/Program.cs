using AaR.Server;
using AaR.Server.Components;
using AaR.Server.Services;
using AaR.Server.Hubs;
using AaR.Server.Middleware;
using AaR.Server.Ob11;
using AaR.Server.Storage;
using Microsoft.FluentUI.AspNetCore.Components;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AaROptions>(builder.Configuration.GetSection("AaR"));
builder.Services.AddSingleton<AaROptions>(_ =>
    builder.Configuration.GetSection("AaR").Get<AaROptions>() ?? new AaROptions());
builder.Services.Configure<ReverseOptions>(builder.Configuration.GetSection("AaR:Reverse"));
builder.Services.AddSingleton<ReverseOptions>(_ =>
    builder.Configuration.GetSection("AaR:Reverse").Get<ReverseOptions>() ?? new ReverseOptions());
builder.Services.AddHostedService<ReversePushService>();
builder.Services.AddSingleton<BridgeRegistry>();
builder.Services.AddSingleton<Ob11Store>();
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

// ---------- WebUI 管理面板（1.0.0-preview1 里程碑）：Blazor Interactive Server + FluentUI ----------
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddFluentUIComponents();
// 日志环形缓冲 + OB11 调用审计（WebUI 总览/日志页数据源）
var logRing = new LogRing(3000);
builder.Logging.AddProvider(new LogRingLoggerProvider(logRing));
builder.Services.AddSingleton(logRing);
builder.Services.AddSingleton<Ob11Audit>();

var app = builder.Build();

// graceful shutdown: SIGTERM (systemd/pkill) cancels background loops cleanly
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    try { app.Services.GetRequiredService<Ob11Server>().Shutdown(); }
    catch { }
});

var opts = app.Services.GetRequiredService<AaROptions>();
var registry = app.Services.GetRequiredService<BridgeRegistry>();
var ob11 = app.Services.GetRequiredService<Ob11Server>();
var store = app.Services.GetRequiredService<Ob11Store>();
var audit = app.Services.GetRequiredService<Ob11Audit>();
await store.InitializeAsync(); // SQLite schema / 内存库锚连接 —— 在接受任何流量前完成

// ---------- OB11 HTTP endpoints ----------
// 标准 /:action 形态：POST/GET 均可（/api/{action} 与 /{action}），
// 传参支持 JSON 正文、urlencoded 表单、query 三种（标准）。
async Task<IResult> HandleOb11Action(HttpContext ctx, string action)
{
    if (string.IsNullOrWhiteSpace(action)) return Results.BadRequest();
    // 鉴权（标准）：token 缺失 → 401；token 错误 → 403
    var auth = AaRAuth.Evaluate(ctx, opts);
    if (auth == AaRAuth.AuthResult.TokenMissing)
        return Results.Content(Ob11Response.Fail(1401, "access token missing").ToJson(), "application/json", Encoding.UTF8, 401);
    if (auth == AaRAuth.AuthResult.TokenInvalid)
        return Results.Content(Ob11Response.Fail(1403, "access token invalid").ToJson(), "application/json", Encoding.UTF8, 403);
    // honor the (previously dead) config: reject OB11 calls until the bridge is up
    if (opts.RequireBridgeForHttp && !registry.IsBridgeConnected)
        return Results.Content(Ob11Response.Fail(Ob11.RetUnsupported, "bridge not connected").ToJson(), "application/json", Encoding.UTF8, 503);

    var p = new Dictionary<string, object?>();
    if (HttpMethods.IsPost(ctx.Request.Method))
    {
        var ct = (ctx.Request.ContentType ?? "").ToLowerInvariant();
        if (ct.Contains("application/json"))
        {
            try
            {
                using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return Results.Content(Ob11Response.Fail(1400, "request body must be a JSON object").ToJson(), "application/json", Encoding.UTF8, 400);
                }
                foreach (var prop in doc.RootElement.EnumerateObject())
                    p[prop.Name] = prop.Value.Clone();
            }
            catch (JsonException)
            {
                // 标准：POST 正文格式不正确 → 400
                return Results.Content(Ob11Response.Fail(1400, "malformed JSON body").ToJson(), "application/json", Encoding.UTF8, 400);
            }
        }
        else if (ct.Contains("x-www-form-urlencoded"))
        {
            var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
            foreach (var (k, v) in form) p[k] = v.ToString();
        }
        else if (ct.Length > 0 && !ct.Contains("multipart/form-data"))
        {
            // 标准：Content-Type 不支持 → 406
            return Results.Content(Ob11Response.Fail(1400, "unsupported content-type").ToJson(), "application/json", Encoding.UTF8, 406);
        }
    }
    // query 参数兜底合并（GET 形态；也允许 JSON + query 混合传参）
    foreach (var (k, v) in ctx.Request.Query)
        p.TryAdd(k, v.ToString());

    var echo = ctx.Request.Query["echo"].ToString();
    if (string.IsNullOrEmpty(echo))
    {
        echo = ctx.Request.Headers.TryGetValue("x-echo", out var hEcho) ? hEcho.ToString() : null;
        if (string.IsNullOrEmpty(echo)) echo = null; // 标准：无 echo → 响应省略该字段
    }

    var callStart = System.Diagnostics.Stopwatch.GetTimestamp();
    var response = await ob11.HandleActionAsync(action, p, echo);
    // 1.0.0-preview1：调用审计（WebUI 总览「最近调用」）
    var swMs = (int)((System.Diagnostics.Stopwatch.GetTimestamp() - callStart) / TimeSpan.TicksPerMillisecond);
    audit.Record(action, (int)response.Retcode, swMs, p.TryGetValue("self_id", out var auditTarget) ? auditTarget?.ToString() : null);
    // 标准：API 不存在 → HTTP 404（body 仍为标准 envelope，retcode 1404）
    return Results.Content(response.ToJson(), "application/json", Encoding.UTF8,
        response.Retcode == 1404 ? 404 : 200);
}


app.MapPost("/api/{action}", async (HttpContext ctx, string action) => await HandleOb11Action(ctx, action));
app.MapPost("/{action}", async (HttpContext ctx, string action) =>
    IsReservedPath(action) ? Results.NotFound() : await HandleOb11Action(ctx, action));
app.MapGet("/api/{action}", async (HttpContext ctx, string action) => await HandleOb11Action(ctx, action));
app.MapGet("/{action}", async (HttpContext ctx, string action) =>
    IsReservedPath(action) ? Results.NotFound() : await HandleOb11Action(ctx, action));

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

// ---------- WebUI 管理面板（/ui）----------
// 静态资产端点（net9+ 形态）：wwwroot/_content（FluentUI css/组件 js）+
// 框架资产（blazor.web.js）——缺它则 404 双断供（CSS 裸奔 + 电路不起）
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
