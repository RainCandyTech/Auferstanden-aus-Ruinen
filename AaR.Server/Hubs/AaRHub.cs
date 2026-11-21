using AaR.Server.Ob11;
using AaR.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AaR.Server.Hubs;

/// <summary>
/// Hub for the JS bridge inside the QQ NT main process.
/// JS answers InternalCall with InternalCallResult and pushes
/// raw kernel events via InternalEvent / InternalLog.
/// </summary>
public class AaRHub : Hub
{
    private readonly BridgeRegistry _registry;
    private readonly Ob11Server _ob11;
    private readonly AaROptions _opts;
    private readonly ILogger<AaRHub> _logger;

    public AaRHub(BridgeRegistry registry, Ob11Server ob11, AaROptions opts, ILogger<AaRHub> logger)
    {
        _registry = registry;
        _ob11 = ob11;
        _opts = opts;
        _logger = logger;
    }

    public override Task OnConnectedAsync()
    {
        var ctx = Context.GetHttpContext();
        var tokenOk = ctx is null || AaRAuth.Check(ctx, _opts);
        if (!tokenOk)
        {
            _logger.LogWarning("bridge rejected (bad token): {Conn}", Context.ConnectionId);
            Context.Abort();
            return Task.CompletedTask;
        }
        // L4v1：握手短身份（头优先 → query 兜底，docs/l4-contract-v1.md §1.2）；
        // 全缺 = legacy 单桥连接（今日 JS 桥 0.4.x）
        var identity = BridgeIdentity.FromContext(ctx);
        var prev = _registry.PreviousBridge;
        _registry.SetBridge(Context.ConnectionId, identity);
        // legacy 路径保留单桥踢旧语义；身份槽之间不互踢（同 uin 顶替在 hello 绑定时处理）
        if (identity is null && !string.IsNullOrEmpty(prev) && prev != Context.ConnectionId)
        {
            _ = Groups.RemoveFromGroupAsync(prev, "aar-bridge");
            try { Clients.Client(prev).SendAsync("InternalLog", "warn", "replaced by a newer bridge connection"); }
            catch { }
        }
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("[timing] OnDisconnected {Conn} at {T} ex={Ex}",
            Context.ConnectionId, DateTime.UtcNow.ToString("HH:mm:ss.fff"),
            exception?.GetType().Name ?? "null");
        _registry.ClearBridge(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    public void InternalCallResult(string callId, bool ok, string? dataJson, string? err)
        => _registry.Complete(callId, ok, dataJson, err);

    public async Task InternalEvent(string name, string payloadJson)
    {
        // L4v1 hello 保留名（docs/l4-contract-v1.md §1.2 时点二）：桥自报能力/uin/代际。
        // 注册表返回被同 uin 顶替的旧连接，由 hub 踢（与 legacy 踢旧同款动作）。
        if (name == "hello")
        {
            var displaced = _registry.Hello(Context.ConnectionId, payloadJson);
            if (!string.IsNullOrEmpty(displaced) && displaced != Context.ConnectionId)
            {
                _ = Groups.RemoveFromGroupAsync(displaced, "aar-bridge");
                try { await Clients.Client(displaced).SendAsync("InternalLog", "warn", "replaced by a newer connection for the same account"); }
                catch { }
            }
            return;
        }
        _logger.LogInformation("[event] {Name} ({Len} bytes) slot={Uin}", name, payloadJson.Length,
            _registry.UinOfConnection(Context.ConnectionId) ?? "-");
        await _ob11.OnInternalEventAsync(name, payloadJson, _registry.UinOfConnection(Context.ConnectionId));
    }

    public void InternalLog(string level, string line)
        => _logger.Log(LogLevelFrom(level), "[bridge] {Line}", line);

    private static LogLevel LogLevelFrom(string level) => level switch
    {
        "warn" => LogLevel.Warning,
        "error" => LogLevel.Error,
        _ => LogLevel.Information,
    };
}
