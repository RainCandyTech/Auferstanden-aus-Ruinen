using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using AaR.Server.Hubs;
using AaR.Server.Ob11;

namespace AaR.Server.Services;

/// <summary>
/// 连接握手身份（docs/l4-contract-v1.md §1.2）。头优先、query 兜底；全缺 = legacy 单桥语义。
/// 浏览器形态 WebSocket API 无自定义头能力（Electron 40 全局 WS 实测），query 是全客户端兜底通道；
/// 头是能力完备客户端（.NET host 等）的首选干净通道。
/// </summary>
public sealed record BridgeIdentity(string Bridge, string Core, string Version, string Slot)
{
    public static BridgeIdentity? FromContext(HttpContext? ctx)
    {
        if (ctx is null) return null;
        string? Val(string queryKey, string headerName)
        {
            var h = ctx.Request.Headers[headerName].FirstOrDefault();
            if (!string.IsNullOrEmpty(h)) return h;
            var q = ctx.Request.Query[queryKey].FirstOrDefault();
            return string.IsNullOrEmpty(q) ? null : q;
        }
        var bridge = Val("aar_bridge", "X-AaR-Bridge");
        if (string.IsNullOrEmpty(bridge)) return null;
        return new BridgeIdentity(
            bridge,
            Val("aar_core", "X-AaR-Core") ?? "v1",
            Val("aar_version", "X-AaR-Version") ?? "",
            Val("aar_slot", "X-AaR-Slot") ?? "");
    }
}

/// <summary>一个连接 = 一个账号槽位（pending 至 hello 绑 uin）。</summary>
public sealed class BridgeSlot
{
    public string ConnectionId = "";
    public string Bridge = "js";
    public string Core = "v1";
    public string Version = "";
    public string Slot = "";             // 桥侧账号实例序（JS 形态对应 nt_k）
    public string? Uin;                 // hello 绑定
    public long Gen;                     // 该连接会话代际
    public bool HelloSeen;               // hello 到达后守卫才生效（之前按满能力假定）
    public readonly HashSet<string> Ext = new(StringComparer.Ordinal);
}

/// <summary>
/// Tracks bridge connections and provides RPC into them.
/// L4v1 slot-per-account registry（docs/l4-contract-v1.md §1.3）：
///   · legacy 槽（无握手身份，今日 JS 桥 0.4.x）：单桥语义原样保留——新连踢旧（hub 负责）、
///     承担缺省路由、按满能力假定；
///   · 身份槽：pending 直到 hello 绑 uin；同 uin 新连顶旧（注册表返回被顶替连接，hub 踢）；
///   · 能力守卫（MissingCapability）只对 hello 过的槽生效。
/// RPC: CallBridgeAsync -> hub invokes InternalCall on the target connection ->
/// bridge answers via InternalCallResult -> pending TCS completes.
/// </summary>
public sealed class BridgeRegistry
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string[]>> _pending = new();
    private readonly ConcurrentDictionary<string, BridgeSlot> _slots = new();   // connectionId -> slot
    private readonly ConcurrentDictionary<string, string> _byUin = new();       // uin -> connectionId
    private readonly ILogger<BridgeRegistry> _logger;
    private volatile string? _legacyConnectionId;      // 无握手身份的连接（今日单桥）
    private volatile string? _primarySlotConnectionId; // 缺省路由：首个绑定的身份槽
    private AaROptions _opts;

    public BridgeRegistry(ILogger<BridgeRegistry> logger, AaROptions opts)
    {
        _logger = logger;
        _opts = opts;
    }

    public bool IsBridgeConnected => _legacyConnectionId is not null || !_slots.IsEmpty;

    public event Action? BridgeChanged;

    public string? PreviousBridge { get; private set; }   // legacy 路径记账（hub 踢旧用）

    public void SetBridge(string connectionId, BridgeIdentity? identity = null)
    {
        if (identity is null)
        {
            PreviousBridge = _legacyConnectionId;
            _legacyConnectionId = connectionId;
            _logger.LogInformation("legacy bridge connected: {Conn} (replacing {Prev})",
                connectionId, PreviousBridge ?? "-");
        }
        else
        {
            _slots[connectionId] = new BridgeSlot
            {
                ConnectionId = connectionId,
                Bridge = identity.Bridge,
                Core = identity.Core,
                Version = identity.Version,
                Slot = identity.Slot,
            };
            if (_primarySlotConnectionId is null) _primarySlotConnectionId = connectionId;
            _logger.LogInformation(
                "bridge slot connected: {Conn} bridge={Bridge} core={Core} v={Ver} slot={Slot} (uin pending hello)",
                connectionId, identity.Bridge, identity.Core, identity.Version, identity.Slot);
        }
        BridgeChanged?.Invoke();
    }

    /// <summary>hello 事件（L4v1 §1.2 时点二）：绑定/刷新槽位身份、能力与代际。
    /// 返回被同 uin 顶替的旧连接 id（hub 负责踢），null = 无顶替；legacy 连接忽略。</summary>
    public string? Hello(string connectionId, string payloadJson)
    {
        if (!_slots.TryGetValue(connectionId, out var slot)) return null;
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("bridge", out var b) && b.ValueKind == JsonValueKind.String)
                slot.Bridge = b.GetString() ?? slot.Bridge;
            if (root.TryGetProperty("core", out var c) && c.ValueKind == JsonValueKind.String)
                slot.Core = c.GetString() ?? slot.Core;
            if (root.TryGetProperty("gen", out var g))
            {
                if (g.ValueKind == JsonValueKind.Number && g.TryGetInt64(out var gn)) slot.Gen = gn;
                else if (g.ValueKind == JsonValueKind.String && long.TryParse(g.GetString(), out var gs)) slot.Gen = gs;
            }
            slot.Ext.Clear();
            if (root.TryGetProperty("ext", out var ex) && ex.ValueKind == JsonValueKind.Array)
                foreach (var e in ex.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.String && e.GetString() is { Length: > 0 } name)
                        slot.Ext.Add(name);
            if (root.TryGetProperty("uin", out var u) && u.GetString() is { Length: > 0 } uin)
            {
                slot.Uin = uin;
                var existing = _byUin.GetOrAdd(uin, connectionId);
                if (existing != connectionId && _byUin.TryUpdate(uin, connectionId, existing))
                {
                    _logger.LogInformation("bridge slot bound: {Conn} uin={Uin} (displacing {Old})",
                        connectionId, uin, existing);
                    return existing;
                }
                _logger.LogInformation("bridge slot bound: {Conn} uin={Uin}", connectionId, uin);
            }
            slot.HelloSeen = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("hello parse failed from {Conn}: {Err}", connectionId, ex.Message);
        }
        return null;
    }

    public void ClearBridge(string connectionId)
    {
        if (_legacyConnectionId == connectionId)
        {
            _legacyConnectionId = null;
            _logger.LogWarning("legacy bridge disconnected: {Conn}", connectionId);
            BridgeChanged?.Invoke();
            return;
        }
        if (_slots.TryRemove(connectionId, out var slot))
        {
            if (slot.Uin is { Length: > 0 } uin &&
                _byUin.TryGetValue(uin, out var bound) && bound == connectionId)
                _byUin.TryRemove(uin, out _);
            if (_primarySlotConnectionId == connectionId)
                _primarySlotConnectionId = _slots.Keys.FirstOrDefault();
            _logger.LogWarning("bridge slot disconnected: {Conn} uin={Uin}", connectionId, slot.Uin ?? "-");
            BridgeChanged?.Invoke();
        }
    }

    public void Complete(string callId, bool ok, string? dataJson, string? err)
    {
        if (_pending.TryRemove(callId, out var tcs))
        {
            tcs.TrySetResult([ok ? "1" : "0", dataJson ?? "", err ?? ""]);
        }
    }

    private string? ResolveTarget(string? targetUin)
    {
        if (!string.IsNullOrEmpty(targetUin) && _byUin.TryGetValue(targetUin, out var bound))
            return bound;
        if (_legacyConnectionId is not null) return _legacyConnectionId;
        if (_primarySlotConnectionId is not null) return _primarySlotConnectionId;
        return !_slots.IsEmpty ? _slots.Keys.FirstOrDefault() : null;
    }

    /// <summary>Invoke a kernel method on the bridge. Returns (ok, dataJson, err).</summary>
    public async Task<(bool Ok, string? DataJson, string? Err)> CallBridgeAsync(
        IHubContext<AaRHub> hub, string method, object? paramsObj, int? timeoutMs = null, string? targetUin = null)
    {
        var conn = ResolveTarget(targetUin);
        if (conn is null) return (false, null, "bridge not connected");

        var callId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[callId] = tcs;

        try
        {
            var paramsJson = paramsObj is null ? null : JsonSerializer.Serialize(paramsObj);
            await hub.Clients.Client(conn).SendAsync("InternalCall", callId, method, paramsJson);
            var timeout = timeoutMs ?? _opts.BridgeTimeoutMs;
            var winner = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
            if (winner != tcs.Task)
            {
                _pending.TryRemove(callId, out _);
                return (false, null, $"bridge RPC timeout: {method}");
            }
            var r = tcs.Task.Result;
            return (r[0] == "1", string.IsNullOrEmpty(r[1]) ? null : r[1], string.IsNullOrEmpty(r[2]) ? null : r[2]);
        }
        catch (Exception ex)
        {
            _pending.TryRemove(callId, out _);
            return (false, null, "bridge RPC error: " + ex.Message);
        }
    }

    /// <summary>Typed helper: call + deserialize the data JSON.</summary>
    public async Task<(bool Ok, JsonElement? Data, string? Err)> CallJsonAsync(
        IHubContext<AaRHub> hub, string method, object? paramsObj, int? timeoutMs = null, string? targetUin = null)
    {
        var (ok, dataJson, err) = await CallBridgeAsync(hub, method, paramsObj, timeoutMs, targetUin);
        if (!ok || dataJson is null) return (ok, null, err);
        try
        {
            using var doc = JsonDocument.Parse(dataJson);
            return (true, doc.RootElement.Clone(), null);
        }
        catch (Exception ex)
        {
            return (false, null, "bad kernel data json: " + ex.Message);
        }
    }

    /// <summary>能力守卫（docs/l4-contract-v1.md §3.3/§6）：null = 放行；否则返回缺失的扩展组名。
    /// 只对 hello 申报过的槽生效——legacy 与 pending 槽按满能力假定（过渡兼容）。</summary>
    public string? MissingCapability(string action)
    {
        var required = L4Dispatch.RequiredExt(action);
        if (required is null) return null;
        var conn = ResolveTarget(null);
        if (conn is null) return null;                 // 无桥 → 既有 "bridge not connected" 路径
        if (conn == _legacyConnectionId) return null;  // legacy = 满能力假定
        if (!_slots.TryGetValue(conn, out var slot)) return null;
        if (!slot.HelloSeen) return null;             // pending = 满能力假定
        return slot.Ext.Contains(required) ||
               slot.Ext.Any(d => d.StartsWith(required + "-v", StringComparison.Ordinal))
            ? null
            : required;
    }
}
