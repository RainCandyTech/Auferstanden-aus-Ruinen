namespace AaR.Server;

/// <summary>Server-side configuration.</summary>
public class AaROptions
{
    public string AccessToken { get; set; } = "";
    public string BridgeHubPath { get; set; } = "/hub/aar";
    public string Ob11WsPath { get; set; } = "/ws";
    public int BridgeTimeoutMs { get; set; } = 15000;
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
    public Services.ReverseOptions Reverse { get; set; } = new();
}

public static class AaRAuth
{
    public static bool Check(HttpContext ctx, AaROptions opts)
    {
        if (string.IsNullOrEmpty(opts.AccessToken)) return true;
        var token = opts.AccessToken;

        var auth = ctx.Request.Headers.Authorization.ToString();
        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) &&
            auth[7..].Trim() == token) return true;

        if (ctx.Request.Headers["access_token"].ToString() == token) return true;

        var q = ctx.Request.Query["access_token"].ToString();
        return q == token;
    }
}
