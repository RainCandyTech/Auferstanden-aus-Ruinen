using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using AaR.Server.Hubs;

namespace AaR.Server.Services;

/// <summary>
/// Tracks the single bridge (JS) connection and provides RPC into it.
/// RPC: CallBridgeAsync -> hub invokes InternalCall on the bridge ->
/// bridge answers via InternalCallResult -> pending TCS completes.
/// </summary>
public sealed class BridgeRegistry
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string[]>> _pending = new();
    private readonly ILogger<BridgeRegistry> _logger;
    private volatile string? _bridgeConnectionId;
    private AaROptions _opts;

    public BridgeRegistry(ILogger<BridgeRegistry> logger, AaROptions opts)
    {
        _logger = logger;
        _opts = opts;
    }

    public bool IsBridgeConnected => _bridgeConnectionId is not null;

    public event Action? BridgeChanged;

    public void SetBridge(string connectionId)
    {
        // single-bridge policy: remember the previous one so the hub can kick it
        PreviousBridge = _bridgeConnectionId;
        _bridgeConnectionId = connectionId;
        _logger.LogInformation("bridge connected: {Conn} (replacing {Prev})", connectionId, PreviousBridge ?? "-");
        BridgeChanged?.Invoke();
    }

    public string? PreviousBridge { get; private set; }

    public void ClearBridge(string connectionId)
    {
        if (_bridgeConnectionId == connectionId)
        {
            _bridgeConnectionId = null;
            _logger.LogWarning("bridge disconnected: {Conn}", connectionId);
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

    /// <summary>Invoke a kernel method on the bridge. Returns (ok, dataJson, err).</summary>
    public async Task<(bool Ok, string? DataJson, string? Err)> CallBridgeAsync(
        IHubContext<AaRHub> hub, string method, object? paramsObj, int? timeoutMs = null)
    {
        var conn = _bridgeConnectionId;
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
        IHubContext<AaRHub> hub, string method, object? paramsObj, int? timeoutMs = null)
    {
        var (ok, dataJson, err) = await CallBridgeAsync(hub, method, paramsObj, timeoutMs);
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
}
