using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AaR.Server.Ob11;

namespace AaR.Server.Middleware;

/// <summary>
/// OneBot v11 forward-WebSocket endpoint at /ws:
/// clients connect, send {"action","params","echo"} frames and receive
/// {"status","retcode","data","echo"} responses plus pushed events.
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
        if (!ctx.Request.Path.Equals(_opts.Ob11WsPath, StringComparison.OrdinalIgnoreCase))
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
        _ob11.RegisterWsClient(clientId, ws);
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
            var connectJson = Ob11Json.Serialize(connectEvt);
            var connectBytes = Encoding.UTF8.GetBytes(connectJson);
            await _ob11.SendWsFrameAsync(clientId, ws, connectBytes);
        }
        catch { }
        _logger.LogInformation("ob11 ws client connected: {Id}", clientId);

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
        string? echo = null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            echo = root.TryGetProperty("echo", out var e) ? e.ToString() : null;
            var action = root.TryGetProperty("action", out var a) ? a.GetString() ?? "" : "";
            var p = new Dictionary<string, object?>();
            if (root.TryGetProperty("params", out var ps) && ps.ValueKind == JsonValueKind.Object)
                foreach (var prop in ps.EnumerateObject())
                    p[prop.Name] = prop.Value.Clone();

            var response = await _ob11.HandleActionAsync(action, p, echo);
            await SendAsync(clientId, ws, response);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ob11 ws frame failed");
            await SendAsync(clientId, ws, Ob11Response.Fail(-1, ex.Message, echo: echo));
        }
    }

    private async Task SendAsync(string clientId, WebSocket ws, Ob11Response response)
    {
        if (ws.State != WebSocketState.Open) return;
        var json = Ob11Json.Serialize(response);
        var bytes = Encoding.UTF8.GetBytes(json);
        // shared per-connection lock: responses race with event broadcasts
        await _ob11.SendWsFrameAsync(clientId, ws, bytes);
    }
}
