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
        var prev = _registry.PreviousBridge;
        _registry.SetBridge(Context.ConnectionId);
        // single-bridge policy: kick a stale previous connection if still attached
        if (!string.IsNullOrEmpty(prev) && prev != Context.ConnectionId)
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
        _logger.LogInformation("[event] {Name} ({Len} bytes)", name, payloadJson.Length);
        await _ob11.OnInternalEventAsync(name, payloadJson);
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
