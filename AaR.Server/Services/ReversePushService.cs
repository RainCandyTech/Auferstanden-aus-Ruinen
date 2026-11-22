using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AaR.Server.Ob11;

namespace AaR.Server.Services;

/// <summary>OneBot v11 reverse WebSocket target (we are the WS client; the bot framework hosts the server).
/// 0.8.1：SelfId 账号绑定——一 bot 一连接。绑定后：事件按 self_id 过滤推送、该连接
/// 回传的 API 调用默认路由到绑定槽、握手 X-Self-ID 报绑定 uin。缺省（不绑）= 全量
/// 推送 + 主槽路由（0.8.0 兼容行为）。
/// 1.0.0-preview2：实例不再来自 appsettings，而来自 ReverseEndpointManager 的库表快照。</summary>
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

/// <summary>appsettings 反向端点初始配置（1.0.0-preview2 起仅作首次 seed 渠道，运行时权威在库表）。</summary>
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
/// 1.0.0-preview2：端点集来自 ReverseEndpointManager（aar.db ReverseTargets 表），
/// 增删改/启停即时应用（WS loop 按目标 Id 起/停；HTTP 推送集随快照即刷）。
/// </summary>
public class ReversePushService : BackgroundService
{
    private readonly Ob11Server _ob11;
    private readonly BridgeRegistry _bridge;
    private readonly ReverseEndpointManager _manager;
    private readonly ILogger<ReversePushService> _logger;
    private readonly ConcurrentQueue<string> _queue = new();
    // per-target (Id-keyed) connection state: target decl + live socket（null = 断开中）
    private readonly ConcurrentDictionary<long, (ReverseWsTarget Target, ClientWebSocket? Ws)> _sockets = new();
    // per-target WS loops (Id -> cts)：动态增删/启停/改配置的即停通道
    private readonly ConcurrentDictionary<long, CancellationTokenSource> _wsLoops = new();
    // per-target send locks: concurrent WebSocket.SendAsync on one socket throws
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _wsSendLocks = new();
    private readonly object _applyLock = new();
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private string SelfUin => _ob11.SelfUin;

    public ReversePushService(Ob11Server ob11, BridgeRegistry bridge,
        ReverseEndpointManager manager, ILogger<ReversePushService> logger)
    {
        _ob11 = ob11;
        _bridge = bridge;
        _manager = manager;
        _logger = logger;
        _ob11.EventEmitted += evt => { _queue.Enqueue((string)evt); return Task.CompletedTask; };
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await _manager.InitializeAsync();            // 幂等（Program.cs 已预跑 seed/装载）
        ApplySnapshot(_manager.Current);             // 初始集
        _manager.TargetsChanged += ApplySnapshot;    // 之后库表任何变更即时应用

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

    /// <summary>库表快照 → 运行集 diff 应用：
    /// 快照外的 loop 停（删除/停用）；URL/SelfId/Token/重连参数变化的重建；新增的起。</summary>
    private void ApplySnapshot(IReadOnlyList<ReverseTargetView> snap)
    {
        lock (_applyLock)
        {
            var wanted = new List<ReverseTargetView>();
            foreach (var t in snap)
                if (t.Kind == "ws" && t.Enabled && !string.IsNullOrEmpty(t.Url))
                    wanted.Add(t);

            // 停：运行中但不在快照集（删除/停用/改 kind）
            foreach (var id in _wsLoops.Keys)
                if (wanted.All(w => w.Id != id)) StopLoop(id);

            // 起或重建：无 loop、或任一连接参数变化
            foreach (var w in wanted)
            {
                var running = _wsLoops.TryGetValue(w.Id, out var cts) ? cts : null;
                var declSame = _sockets.TryGetValue(w.Id, out var s) &&
                    s.Target.Url == w.Url &&
                    s.Target.SelfId == w.SelfId &&
                    s.Target.AccessToken == (w.AccessToken ?? "") &&
                    s.Target.ReconnectMs == w.ReconnectMs;
                if (running is not null && declSame) continue;
                StopLoop(w.Id);
                var ncts = new CancellationTokenSource();
                _wsLoops[w.Id] = ncts;
                var target = new ReverseWsTarget
                {
                    Url = w.Url,
                    AccessToken = w.AccessToken ?? "",
                    SelfId = w.SelfId,
                    ReconnectMs = w.ReconnectMs > 0 ? w.ReconnectMs : 5000,
                };
                _sockets[w.Id] = (target, null);
                _ = Task.Run(() => ReverseWsLoopAsync(target, w.Id, ncts.Token));
                _logger.LogInformation("reverse-ws loop {State}: #{Id} {Url} self={Self}",
                    running is null ? "started" : "restarted", w.Id, w.Url, w.SelfId ?? "全量");
            }
        }
    }

    private void StopLoop(long id)
    {
        if (_wsLoops.TryRemove(id, out var cts))
        {
            try { cts.Cancel(); } catch { }
            // cts 不 Dispose：loop 内仍有延迟中的 Token 引用，交由 GC
        }
        if (_sockets.TryRemove(id, out _)) { }
    }

    // ---------- reverse WebSocket ----------

    private async Task ReverseWsLoopAsync(ReverseWsTarget target, long id, CancellationToken ct)
    {
        var backoff = target.ReconnectMs;
        while (!ct.IsCancellationRequested)
        {
            ClientWebSocket? born = null;
            try
            {
                using var ws = new ClientWebSocket();
                born = ws;
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

                _sockets[id] = (target, ws);

                // wait for self identity (login boot order: bridge connects before login completes)
                for (int w = 0; w < 30 && string.IsNullOrEmpty(SelfUin); w++)
                    await Task.Delay(500, ct);

                // announce self id on connect (spec: X-Self-ID header equivalent in-band)
                // 0.8.1：生命周期事件同样报绑定口径（未绑定回落主槽缓存）
                try
                {
                    await SendFrameAsync(id, ws, Ob11Json.Serialize(new Dictionary<string, object?>
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
                    _ = HandleReverseApiFrameAsync(id, ws, text, ct);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning("reverse-ws {Url} error: {Err}", target.Url, ex.Message);
            }
            finally
            {
                // 本 loop 的 socket 退役：仅当槽里还是自己这枚 ws 才清理——
                // 管理面重建（同 Id 新 target）塞进的新槽不动
                if (born is not null && _sockets.TryGetValue(id, out var cur) && ReferenceEquals(cur.Ws, born))
                    _sockets.TryRemove(id, out _);
            }

            if (ct.IsCancellationRequested) break;
            if (!_wsLoops.ContainsKey(id)) break;   // 管理面已停：不再重连
            _logger.LogInformation("reverse-ws {Url} reconnecting in {Ms}ms", target.Url, backoff);
            try { await Task.Delay(backoff, ct); } catch (OperationCanceledException) { break; }
            backoff = Math.Min(backoff * 2, 60000);
        }
    }

    private async Task HandleReverseApiFrameAsync(long id, ClientWebSocket ws, string text, CancellationToken ct)
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
            if (_sockets.TryGetValue(id, out var slot) && !string.IsNullOrEmpty(slot.Target.SelfId))
                p.TryAdd("self_id", slot.Target.SelfId);

            var response = await _ob11.HandleActionAsync(action, p, echo, echoRaw);
            await SendFrameAsync(id, ws, response.ToJson(), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "reverse-ws api frame failed");
            try { await SendFrameAsync(id, ws, Ob11Response.Fail(-1, ex.Message, echo: echo, echoRaw: echoRaw).ToJson(), ct); }
            catch { }
        }
    }

    private async Task SendFrameAsync(long id, ClientWebSocket ws, string json, CancellationToken ct)
    {
        if (ws.State != WebSocketState.Open) return;
        var l = _wsSendLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
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
        sends = new List<Task>();
        foreach (var kv in _sockets)
        {
            var (target, ws) = kv.Value;
            if (ws is not { State: WebSocketState.Open }) continue;
            if (!string.IsNullOrEmpty(target.SelfId) && evtSelfId is not null &&
                target.SelfId != evtSelfId) continue;
            var id2 = kv.Key;
            // fire-and-forget but with the per-connection send lock held
            // until the frame is written
            sends.Add(Task.Run(async () =>
            {
                try { await SendFrameAsync(id2, ws, json, ct); }
                catch { /* reconnect loop owns failures */ }
            }, CancellationToken.None));
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
        // 1.0.0-preview2：HTTP 端点集随库表快照即时变更（enabled 才推）
        var targets = _manager.Current
            .Where(t => t.Kind == "http" && t.Enabled && !string.IsNullOrEmpty(t.Url))
            .ToList();
        if (targets.Count == 0) return;

        var evtSelfId = ExtractEventSelfId(json);
        foreach (var target in targets)
        {
            try
            {
                // 绑定账号的 HTTP 目标：只推本账号事件（与反向 WS 0.8.1 同口径）
                if (!string.IsNullOrEmpty(target.SelfId) && evtSelfId is not null &&
                    target.SelfId != evtSelfId) continue;

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(8000);
                using var req = new HttpRequestMessage(HttpMethod.Post, target.Url)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
                req.Headers.Add("X-Self-ID", !string.IsNullOrEmpty(target.SelfId) ? target.SelfId : SelfUin);
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
