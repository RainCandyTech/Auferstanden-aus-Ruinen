using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AaR.Server.Ob11;

namespace AaR.Server.Middleware;

/// <summary>
/// OneBot v11 forward-WebSocket endpoints (standard):
///   {Ob11WsPath}            → universal（API + 事件，同一连接）
///   {Ob11WsPath}/api        → 仅 API
///   {Ob11WsPath}/event      → 仅事件推送（收到的 API 请求被忽略）
/// 客户端发送 {"action","params","echo"}，得到 {"status","retcode","data","echo"}。
/// </summary>
public class Ob11WebSocketMiddleware
{
    private readonly Ob11Server _ob11;
    private readonly AaROptions _opts;
    private readonly ILogger<Ob11WebSocketMiddleware> _logger;

    public Ob11WebSocketMiddleware(ILogger<Ob11WebSocketMiddleware> logger, Ob11Server ob11, AaROptions opts)
    {
        _logger = logger;
        _ob11 = ob11;
        _opts = opts;
    }

    public async Task InvokeAsync(HttpContext ctx, RequestDelegate next)
    {
        var basePath = _opts.Ob11WsPath.TrimEnd('/');
        var path = ctx.Request.Path.Value ?? "";

        Ob11Server.WsClientMode mode;
        if (path.Equals(basePath, StringComparison.OrdinalIgnoreCase) ||
            path.Equals(basePath + "/", StringComparison.OrdinalIgnoreCase))
            mode = Ob11Server.WsClientMode.Universal;
        else if (path.Equals(basePath + "/api", StringComparison.OrdinalIgnoreCase) ||
                 path.Equals(basePath + "/api/", StringComparison.OrdinalIgnoreCase))
            mode = Ob11Server.WsClientMode.Api;
        else if (path.Equals(basePath + "/event", StringComparison.OrdinalIgnoreCase) ||
                 path.Equals(basePath + "/event/", StringComparison.OrdinalIgnoreCase))
            mode = Ob11Server.WsClientMode.Event;
        else
        {
            await next(ctx);
            return;
        }

        var auth = AaRAuth.Check(ctx, _opts);
        if (!auth)
        {
            ctx.Response.StatusCode = 401;
            await ctx.Response.WriteAsync("unauthorized");
            return;
        }

        // manual upgrade check: IsWebSocketRequest proved unreliable in this pipeline
        var isUpgrade = HttpMethods.IsGet(ctx.Request.Method)
            && ctx.Request.Headers.Upgrade.ToString().Contains("websocket", StringComparison.OrdinalIgnoreCase)
            && ctx.Request.Headers.Connection.ToString().Contains("Upgrade", StringComparison.OrdinalIgnoreCase)
            && ctx.Request.Headers.ContainsKey("Sec-WebSocket-Key")
            && ctx.Request.Headers.SecWebSocketVersion.ToString() == "13";
        if (!isUpgrade)
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsync("websocket required");
            return;
        }

        using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
        // register before the first send so the lifecycle frame and every later
        // frame share the server-side per-connection send lock
        var clientId = Guid.NewGuid().ToString("N");
        _ob11.RegisterWsClient(clientId, ws, mode);
        // P1-1: late-connecting clients must receive lifecycle connect immediately
        try
        {
            var connectEvt = new Dictionary<string, object?>
            {
                ["time"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ["self_id"] = _ob11.SelfUin,
                ["post_type"] = "meta_event",
                ["meta_event_type"] = "lifecycle",
                ["sub_type"] = "connect",
            };
            var connectBytes = Encoding.UTF8.GetBytes(Ob11Json.Serialize(connectEvt));
            await _ob11.SendWsFrameAsync(clientId, ws, connectBytes);
        }
        catch { }
        _logger.LogInformation("ob11 ws client connected: {Id} mode={Mode}", clientId, mode);

        var buffer = new byte[1024 * 256];
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ctx.RequestAborted);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", ctx.RequestAborted);
                        break;
                    }
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close) break;
                var text = Encoding.UTF8.GetString(ms.ToArray());
                // /event 专用连接不提供 API（标准：需要 API 用 /api 或 universal）
                if (mode == Ob11Server.WsClientMode.Event) continue;
                _ = HandleClientFrameAsync(clientId, ws, text);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger.LogWarning(ex, "ob11 ws error"); }
        finally
        {
            _ob11.RemoveWsClient(clientId);
            _logger.LogInformation("ob11 ws client gone: {Id}", clientId);
        }
    }

    private async Task HandleClientFrameAsync(string clientId, WebSocket ws, string text)
    {
        JsonElement? echoRaw = null;
        string? echo = null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.TryGetProperty("echo", out var e))
            {
                echoRaw = e.Clone(); // 标准：echo 可为任意 JSON 类型，原样返回
                echo = e.ToString();
            }
            var action = root.TryGetProperty("action", out var a) ? a.GetString() ?? "" : "";
            var p = new Dictionary<string, object?>();
            if (root.TryGetProperty("params", out var ps) && ps.ValueKind == JsonValueKind.Object)
                foreach (var prop in ps.EnumerateObject())
                    p[prop.Name] = prop.Value.Clone();

            var response = await _ob11.HandleActionAsync(action, p, echo, echoRaw);
            await SendAsync(clientId, ws, response.ToJson());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ob11 ws frame failed");
            await SendAsync(clientId, ws, Ob11Response.Fail(-1, ex.Message, echo: echo, echoRaw: echoRaw).ToJson());
        }
    }

    private async Task SendAsync(string clientId, WebSocket ws, string json)
    {
        if (ws.State != WebSocketState.Open) return;
        var bytes = Encoding.UTF8.GetBytes(json);
        // shared per-connection lock: responses race with event broadcasts
        await _ob11.SendWsFrameAsync(clientId, ws, bytes);
    }
}
