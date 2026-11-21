using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AaR.Server.Ob11;

namespace AaR.Server.Services;

/// <summary>OneBot v11 reverse WebSocket target (we are the WS client; the bot framework hosts the server).
/// 0.8.1：SelfId 账号绑定——一 bot 一连接。绑定后：事件按 self_id 过滤推送、该连接
/// 回传的 API 调用默认路由到绑定槽、握手 X-Self-ID 报绑定 uin。缺省（不绑）= 全量
/// 推送 + 主槽路由（0.8.0 兼容行为）。</summary>
public class ReverseWsTarget
{
    public string Url { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public int ReconnectMs { get; set; } = 5000;
    public string? SelfId { get; set; }              // 绑定账号 uin（hello 同口径）
}

/// <summary>OneBot v11 HTTP POST reporting target (we POST events; the upstream may reply with API calls).</summary>
public class HttpPostTarget
{
    public string Url { get; set; } = "";
    public string AccessToken { get; set; } = "";
}

public class ReverseOptions
{
    public List<ReverseWsTarget> WebSockets { get; set; } = new();
    public List<HttpPostTarget> HttpPost { get; set; } = new();
    public int HttpPostTimeoutMs { get; set; } = 8000;
}

/// <summary>
/// OneBot v11 reverse transports:
///  - reverse WebSocket: we connect out to the framework, push events, and serve
///    {"action","params","echo"} requests received on the same connection.
///  - reverse HTTP POST: we POST every event to the configured URL(s); a 2xx response
///    body may carry an array of API calls which we execute (results are logged).
/// </summary>
public class ReversePushService : BackgroundService
{
    private readonly Ob11Server _ob11;
    private readonly BridgeRegistry _bridge;
    private readonly ILogger<ReversePushService> _logger;
    private readonly ReverseOptions _opts;
    private readonly ConcurrentQueue<string> _queue = new();
    private readonly List<(ReverseWsTarget Target, ClientWebSocket? Ws)> _sockets = new();
    private readonly object _socketsLock = new();
    // per-target send locks: concurrent WebSocket.SendAsync on one socket throws
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _wsSendLocks = new();
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private string SelfUin => _ob11.SelfUin;

    public ReversePushService(Ob11Server ob11, BridgeRegistry bridge, AaROptions opts, ILogger<ReversePushService> logger)
    {
        _ob11 = ob11;
        _bridge = bridge;
        _logger = logger;
        var cfg = opts.Reverse ?? new ReverseOptions();
        _opts = cfg;
        _ob11.EventEmitted += evt => { _queue.Enqueue((string)evt); return Task.CompletedTask; };
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // start per-target reverse-ws connection loops
        for (int i = 0; i < _opts.WebSockets.Count; i++)
        {
            var target = _opts.WebSockets[i];
            _ = Task.Run(() => ReverseWsLoopAsync(target, i, ct), ct);
        }

        // drain the event queue: fan out to reverse-ws connections + http post urls
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_queue.TryDequeue(out var json))
                {
                    await FanOutWsAsync(json, ct);
                    await PostHttpAsync(json, ct);
                }
                else
                {
                    await Task.Delay(50, ct);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogWarning(ex, "reverse push loop error"); }
        }
    }

    // ---------- reverse WebSocket ----------

    private async Task ReverseWsLoopAsync(ReverseWsTarget target, int idx, CancellationToken ct)
    {
        var backoff = target.ReconnectMs;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();
                ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                if (!string.IsNullOrEmpty(target.AccessToken))
                    ws.Options.SetRequestHeader("Authorization", "Bearer " + target.AccessToken);
                // OneBot v11 reverse-ws spec: X-Self-ID + X-Client-Role on handshake
                // 0.8.1：绑定账号的目标报绑定 uin；未绑定回落主槽缓存（兼容）
                var handShakeSelf = !string.IsNullOrEmpty(target.SelfId) ? target.SelfId : _ob11.SelfUin;
                if (!string.IsNullOrEmpty(handShakeSelf))
                    ws.Options.SetRequestHeader("X-Self-ID", handShakeSelf);
                ws.Options.SetRequestHeader("X-Client-Role", "Universal");

                var uri = new Uri(target.Url);
                await ws.ConnectAsync(uri, ct);
                _logger.LogInformation("reverse-ws connected: {Url}", target.Url);
                backoff = target.ReconnectMs;

                lock (_socketsLock) { while (_sockets.Count <= idx) _sockets.Add((target, null)); _sockets[idx] = (target, ws); }

                // wait for self identity (login boot order: bridge connects before login completes)
                for (int w = 0; w < 30 && string.IsNullOrEmpty(SelfUin); w++)
                    await Task.Delay(500, ct);

                // announce self id on connect (spec: X-Self-ID header equivalent in-band)
                // 0.8.1：生命周期事件同样报绑定口径（未绑定回落主槽缓存）
                try
                {
                    await SendFrameAsync(idx, ws, Ob11Json.Serialize(new Dictionary<string, object?>
                    {
                        ["time"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        ["self_id"] = handShakeSelf,
                        ["post_type"] = "meta_event",
                        ["meta_event_type"] = "lifecycle",
                        ["sub_type"] = "connect",
                    }), ct);
                    _logger.LogInformation("reverse-ws lifecycle frame sent to {Url}", target.Url);
                }
                catch (Exception lex) { _logger.LogWarning("reverse-ws lifecycle send failed: {Err}", lex.Message); }

                // receive loop: framework sends {"action","params","echo"} API requests
                var buffer = new byte[1024 * 256];
                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var ms = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                            break;
                        }
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close) break;
                    var text = Encoding.UTF8.GetString(ms.ToArray());
                    _logger.LogInformation("reverse-ws frame received ({Len} bytes)", text.Length);
                    _ = HandleReverseApiFrameAsync(idx, ws, text, ct);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning("reverse-ws {Url} error: {Err}", target.Url, ex.Message);
            }
            finally
            {
                lock (_socketsLock) { if (idx < _sockets.Count) _sockets[idx] = (target, null); }
            }

            if (ct.IsCancellationRequested) break;
            _logger.LogInformation("reverse-ws {Url} reconnecting in {Ms}ms", target.Url, backoff);
            try { await Task.Delay(backoff, ct); } catch (OperationCanceledException) { break; }
            backoff = Math.Min(backoff * 2, 60000);
        }
    }

    private async Task HandleReverseApiFrameAsync(int idx, ClientWebSocket ws, string text, CancellationToken ct)
    {
        JsonElement? echoRaw = null;
        string? echo = null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.TryGetProperty("echo", out var e))
            {
                echoRaw = e.Clone(); // 标准：echo 原样返回
                echo = e.ToString();
            }
            var action = root.TryGetProperty("action", out var a) ? a.GetString() ?? "" : "";
            var p = new Dictionary<string, object?>();
            if (root.TryGetProperty("params", out var ps) && ps.ValueKind == JsonValueKind.Object)
                foreach (var prop in ps.EnumerateObject())
                    p[prop.Name] = prop.Value.Clone();
            // 0.8.1：绑定账号的连接，其回传 API 默认路由到绑定槽——注入 self_id
            //   缺省（调用方显式传 self_id 优先，TryAdd 语义）
            {
                string? bound = null;
                lock (_socketsLock) { if (idx < _sockets.Count) bound = _sockets[idx].Target.SelfId; }
                if (!string.IsNullOrEmpty(bound)) p.TryAdd("self_id", bound);
            }

            var response = await _ob11.HandleActionAsync(action, p, echo, echoRaw);
            await SendFrameAsync(idx, ws, response.ToJson(), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "reverse-ws api frame failed");
            try { await SendFrameAsync(idx, ws, Ob11Response.Fail(-1, ex.Message, echo: echo, echoRaw: echoRaw).ToJson(), ct); }
            catch { }
        }
    }

    private async Task SendFrameAsync(int idx, ClientWebSocket ws, string json, CancellationToken ct)
    {
        if (ws.State != WebSocketState.Open) return;
        var l = _wsSendLocks.GetOrAdd(idx, _ => new SemaphoreSlim(1, 1));
        await l.WaitAsync(ct);
        try
        {
            if (ws.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes(json);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
        }
        finally { l.Release(); }
    }

    private async Task FanOutWsAsync(string json, CancellationToken ct)
    {
        // 0.8.1：多账号过滤——事件 self_id 与目标绑定匹配才推；未绑定目标 = 全量
        //   （0.8.0 兼容）；事件无 self_id 时不过滤（无法归因则全推）
        var evtSelfId = ExtractEventSelfId(json);
        List<Task> sends;
        lock (_socketsLock)
        {
            sends = new List<Task>();
            for (int i = 0; i < _sockets.Count; i++)
            {
                var (target, ws) = _sockets[i];
                if (ws is not { State: WebSocketState.Open }) continue;
                if (!string.IsNullOrEmpty(target.SelfId) && evtSelfId is not null &&
                    target.SelfId != evtSelfId) continue;
                var i2 = i;
                // fire-and-forget but with the per-connection send lock held
                // until the frame is written
                sends.Add(Task.Run(async () =>
                {
                    try { await SendFrameAsync(i2, ws, json, ct); }
                    catch { /* reconnect loop owns failures */ }
                }, CancellationToken.None));
            }
        }
        try { await Task.WhenAll(sends); } catch { /* individual failures logged by loops */ }
    }

    /// <summary>事件 JSON 的 self_id 提取（只读字符串级，FanOut 过滤用）。</summary>
    private static string? ExtractEventSelfId(string json)
    {
        var m = System.Text.RegularExpressions.Regex.Match(json, "\"self_id\"\\s*:\\s*\"?([0-9]+)\"?");
        return m.Success ? m.Groups[1].Value : null;
    }

    // ---------- reverse HTTP POST ----------

    private async Task PostHttpAsync(string json, CancellationToken ct)
    {
        foreach (var target in _opts.HttpPost)
        {
            if (string.IsNullOrEmpty(target.Url)) continue;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(_opts.HttpPostTimeoutMs);
                using var req = new HttpRequestMessage(HttpMethod.Post, target.Url)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
                req.Headers.Add("X-Self-ID", SelfUin);
                req.Headers.Add("X-Client-Role", "Universal");
                if (!string.IsNullOrEmpty(target.AccessToken))
                    req.Headers.Add("Authorization", "Bearer " + target.AccessToken);

                using var resp = await _http.SendAsync(req, cts.Token);
                var body = await resp.Content.ReadAsStringAsync(cts.Token);

                // upstream may reply with an array of API calls to execute
                if ((int)resp.StatusCode is >= 200 and < 300 && !string.IsNullOrWhiteSpace(body))
                {
                    _ = ExecuteUpstreamCallsAsync(body, target.Url);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("http post {Url} failed: {Err}", target.Url, ex.Message);
            }
        }
    }

    private async Task ExecuteUpstreamCallsAsync(string body, string url)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var calls = root.ValueKind == JsonValueKind.Array
                ? root.EnumerateArray().ToList()
                : (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("action", out _)
                    ? new List<JsonElement> { root.Clone() }
                    : new List<JsonElement>());

            foreach (var call in calls)
            {
                if (call.ValueKind != JsonValueKind.Object) continue;
                var action = call.TryGetProperty("action", out var a) ? a.GetString() ?? "" : "";
                var echo = call.TryGetProperty("echo", out var e) ? e.ToString() : null;
                var p = new Dictionary<string, object?>();
                if (call.TryGetProperty("params", out var ps) && ps.ValueKind == JsonValueKind.Object)
                    foreach (var prop in ps.EnumerateObject())
                        p[prop.Name] = prop.Value.Clone();

                var response = await _ob11.HandleActionAsync(action, p, echo);
                _logger.LogInformation("upstream call from {Url}: {Action} -> {Status}:{Retcode} (echo={Echo})",
                    url, action, response.Status, response.Retcode, echo ?? "-");
                // results cannot be returned over plain HTTP POST; interested upstreams
                // should use echo-matched logging or one of the WS transports.
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "upstream call parse failed from {Url}", url);
        }
    }
}
