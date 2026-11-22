using AaR.Server.Storage;

namespace AaR.Server.Services;

/// <summary>反向端点运行视图（WebUI / 管理 API 的读写面；Id 为库主键）。</summary>
public sealed class ReverseTargetView
{
    public long Id { get; init; }
    public string Kind { get; init; } = "ws";      // "ws" | "http"
    public string Url { get; init; } = "";
    public string? AccessToken { get; init; }
    public string? SelfId { get; init; }           // 绑定账号（null = 全量）
    public int ReconnectMs { get; init; } = 5000;
    public bool Enabled { get; init; } = true;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// 反向端点的唯一权威（1.0.0-preview2）：aar.db ReverseTargets 表。
///   · 启动 seed：表空且 appsettings AaR:Reverse 非空 → 一次性导入，之后表为权威
///     （appsettings 配置保留为首次部署的初始渠道）；
///   · 增删改/启停 → 写库 + 快照重建 + TargetsChanged 事件，
///     ReversePushService 订阅并即时应用（WS 连接按目标起停、HTTP 推送集即刷）；
///   · 读面：WebUI 设置页与管理 API /_aar/reverse 共用本服务。
/// </summary>
public sealed class ReverseEndpointManager
{
    private readonly Ob11Store _store;
    private readonly AaROptions _opts;
    private readonly ILogger<ReverseEndpointManager> _logger;
    private readonly Lock _sync = new();
    private IReadOnlyList<ReverseTargetView> _snapshot = Array.Empty<ReverseTargetView>();
    private bool _initialized;

    /// <summary>端点集变更（增/删/改/启停后触发，携带最新快照）。</summary>
    public event Action<IReadOnlyList<ReverseTargetView>>? TargetsChanged;

    public ReverseEndpointManager(Ob11Store store, AaROptions opts, ILogger<ReverseEndpointManager> logger)
    {
        _store = store;
        _opts = opts;
        _logger = logger;
    }

    /// <summary>启动装载：seed（如需）→ 全表快照。幂等，只跑一次。</summary>
    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        // seed：appsettings 既有配置一次性入库（表非空则跳过）
        try
        {
            var cfg = _opts.Reverse ?? new ReverseOptions();
            var seed = new List<ReverseTargetRecord>();
            foreach (var w in cfg.WebSockets)
                if (!string.IsNullOrEmpty(w.Url))
                    seed.Add(new ReverseTargetRecord
                    {
                        Kind = "ws", Url = w.Url, AccessToken = w.AccessToken,
                        SelfId = string.IsNullOrEmpty(w.SelfId) ? null : w.SelfId,
                        ReconnectMs = w.ReconnectMs > 0 ? w.ReconnectMs : 5000,
                    });
            foreach (var h in cfg.HttpPost)
                if (!string.IsNullOrEmpty(h.Url))
                    seed.Add(new ReverseTargetRecord
                    {
                        Kind = "http", Url = h.Url, AccessToken = h.AccessToken, SelfId = null,
                    });
            var seeded = await _store.SeedReverseTargetsAsync(seed);
            if (seeded > 0)
                _logger.LogInformation("reverse targets seeded from appsettings: {N} 个（此后以库表为权威）", seeded);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "reverse seed failed"); }

        await RebuildSnapshotAsync();
        _logger.LogInformation("reverse endpoints loaded: {N} 个（ws={W} http={H}）",
            _snapshot.Count,
            _snapshot.Count(t => t.Kind == "ws" && t.Enabled),
            _snapshot.Count(t => t.Kind == "http" && t.Enabled));
    }

    public IReadOnlyList<ReverseTargetView> Current
    {
        get { lock (_sync) return _snapshot; }
    }

    private async Task RebuildSnapshotAsync()
    {
        var rows = await _store.ListReverseTargetsAsync();
        var snap = rows.Select(r => new ReverseTargetView
        {
            Id = r.Id,
            Kind = r.Kind,
            Url = r.Url,
            AccessToken = r.AccessToken,
            SelfId = r.SelfId,
            ReconnectMs = r.ReconnectMs,
            Enabled = r.Enabled,
            CreatedAt = r.CreatedAt,
        }).ToList();
        Publish(snap);
    }

    private void Publish(IReadOnlyList<ReverseTargetView> snap)
    {
        lock (_sync) _snapshot = snap;
        try { TargetsChanged?.Invoke(snap); }
        catch (Exception ex) { _logger.LogWarning(ex, "reverse TargetsChanged subscriber failed"); }
    }

    /// <summary>新增端点（存库 + 即时应用）。返回新 Id；kind 非法或 url 为空返回 null。</summary>
    public async Task<ReverseTargetView?> AddAsync(string kind, string url, string? accessToken, string? selfId, int reconnectMs)
    {
        kind = kind.Trim().ToLowerInvariant();
        if (kind is not ("ws" or "http")) return null;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out _)) return null;
        var rec = new ReverseTargetRecord
        {
            Kind = kind,
            Url = url.Trim(),
            AccessToken = string.IsNullOrWhiteSpace(accessToken) ? null : accessToken.Trim(),
            SelfId = string.IsNullOrWhiteSpace(selfId) ? null : selfId.Trim(),
            ReconnectMs = reconnectMs > 0 ? reconnectMs : 5000,
            Enabled = true,
        };
        var added = await _store.AddReverseTargetAsync(rec);
        if (added is null) return null;
        await RebuildSnapshotAsync();
        _logger.LogInformation("reverse target added: #{Id} {Kind} {Url} self={Self}", added.Id, added.Kind, added.Url, added.SelfId ?? "全量");
        return SnapshotOf(added.Id);
    }

    /// <summary>启停（存库 + 即时应用：停 = 断开该目标的 WS loop / 停止 HTTP 推送）。</summary>
    public async Task<bool> SetEnabledAsync(long id, bool enabled)
    {
        var rec = await _store.GetReverseTargetAsync(id);
        if (rec is null) return false;
        rec.Enabled = enabled;
        if (!await _store.UpdateReverseTargetAsync(rec)) return false;
        await RebuildSnapshotAsync();
        _logger.LogInformation("reverse target #{Id} {State}", id, enabled ? "enabled" : "disabled");
        return true;
    }

    /// <summary>修改端点（url/token/selfId/reconnect 任一变化都会重启该目标的连接 loop）。</summary>
    public async Task<bool> UpdateAsync(long id, string url, string? accessToken, string? selfId, int reconnectMs)
    {
        var rec = await _store.GetReverseTargetAsync(id);
        if (rec is null) return false;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out _)) return false;
        rec.Url = url.Trim();
        rec.AccessToken = string.IsNullOrWhiteSpace(accessToken) ? null : accessToken.Trim();
        rec.SelfId = string.IsNullOrWhiteSpace(selfId) ? null : selfId.Trim();
        rec.ReconnectMs = reconnectMs > 0 ? reconnectMs : rec.ReconnectMs;
        if (!await _store.UpdateReverseTargetAsync(rec)) return false;
        await RebuildSnapshotAsync();
        _logger.LogInformation("reverse target #{Id} updated: {Url} self={Self}", id, rec.Url, rec.SelfId ?? "全量");
        return true;
    }

    /// <summary>删除（存库 + 即时断开）。</summary>
    public async Task<bool> RemoveAsync(long id)
    {
        if (!await _store.DeleteReverseTargetAsync(id)) return false;
        await RebuildSnapshotAsync();
        _logger.LogInformation("reverse target #{Id} removed", id);
        return true;
    }

    private ReverseTargetView? SnapshotOf(long id)
    {
        lock (_sync) return _snapshot.FirstOrDefault(t => t.Id == id);
    }
}
