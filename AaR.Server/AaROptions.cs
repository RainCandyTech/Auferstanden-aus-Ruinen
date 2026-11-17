namespace AaR.Server;

/// <summary>Server-side configuration.</summary>
public class AaROptions
{
    public string AccessToken { get; set; } = "";
    public string BridgeHubPath { get; set; } = "/hub/aar";
    public string Ob11WsPath { get; set; } = "/ws";
    public int BridgeTimeoutMs { get; set; } = 45000;
    public int HeartbeatIntervalMs { get; set; } = 30000;
    public bool RequireBridgeForHttp { get; set; } = true;
    /// <summary>Enable the diagnostic debug_kernel action (raw kernel method passthrough).</summary>
    public bool EnableDebugKernel { get; set; } = false;
    /// <summary>
    /// Enable the in-chat #AaR debug command: a chat message consisting of exactly
    /// "#AaR" gets a sanitized status summary as a direct reply, and the full
    /// (unmasked) diagnostics are written to the server log. Set false to disable.
    /// </summary>
    public bool EnableDebugCommand { get; set; } = true;
    /// <summary>#AaR chat banner: project codename line.</summary>
    public string ProjectCodename { get; set; } = "从废墟中崛起";
    /// <summary>#AaR chat banner: deployment label (e.g. 测试副本 / 生产).</summary>
    public string DeploymentLabel { get; set; } = "测试副本";
    /// <summary>Where to read the QQ NT version (package.json "version", e.g. 3.2.35-54395).</summary>
    public string QQVersionJsonPath { get; set; } = "/opt/QQ/resources/app/package.json";
    /// <summary>Allow set_restart to terminate the process (service manager restarts it).</summary>
    public bool AllowRestart { get; set; } = true;
    /// <summary>OB11 storage: EF Core + SQLite ("sqlite" persisted / "memory" shared-cache in-memory).</summary>
    public StorageOptions Storage { get; set; } = new();
    public Services.ReverseOptions Reverse { get; set; } = new();
}

public class StorageOptions
{
    /// <summary>"sqlite"（落盘持久，默认）或 "memory"（SQLite 共享缓存内存库，进程生命周期）。</summary>
    public string Provider { get; set; } = "sqlite";
    /// <summary>SQLite 数据库文件路径（Provider=sqlite 时生效），相对路径基于 ContentRoot。</summary>
    public string Path { get; set; } = "data/aar.db";
}

public static class AaRAuth
{
    public enum AuthResult { Ok, TokenMissing, TokenInvalid }

    /// <summary>Detailed verdict so HTTP can honor the standard: 401 when a token
    /// is required but missing, 403 when it is present but wrong.</summary>
    public static AuthResult Evaluate(HttpContext ctx, AaROptions opts)
    {
        if (string.IsNullOrEmpty(opts.AccessToken)) return AuthResult.Ok;
        var token = opts.AccessToken;

        var auth = ctx.Request.Headers.Authorization.ToString();
        var headerGiven = auth.Length > 0;
        if (headerGiven)
        {
            if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) &&
                auth[7..].Trim() == token) return AuthResult.Ok;
            if (auth.Trim() == token) return AuthResult.Ok; // bare token without Bearer prefix
            return AuthResult.TokenInvalid;
        }
        if (ctx.Request.Headers["access_token"].ToString() is { Length: > 0 } h)
            return h == token ? AuthResult.Ok : AuthResult.TokenInvalid;
        if (ctx.Request.Query["access_token"].ToString() is { Length: > 0 } q)
            return q == token ? AuthResult.Ok : AuthResult.TokenInvalid;
        return AuthResult.TokenMissing;
    }

    public static bool Check(HttpContext ctx, AaROptions opts) => Evaluate(ctx, opts) == AuthResult.Ok;
}
