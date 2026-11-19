using System.Text.Json;
using System.Text.Json.Serialization;

namespace AaR.Server.Ob11;

/// <summary>OB11 requires lowerCamelCase JSON everywhere.</summary>
public static class Ob11Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(object o) => JsonSerializer.Serialize(o, Options);
}

/// <summary>OB11 protocol constants.</summary>
public static class Ob11
{
    public const string ProtocolVersion = "v11";

    public const int RetOk = 0;
    public const int RetParamMissing = 100;
    public const int RetParamInvalid = 102;
    public const int RetParamEmpty = 103;
    public const int RetParamBad = 104;
    public const int RetNetworkError = 201;
    public const int RetUnsupported = 1200;
    /// <summary>动作确定性失败（如发送被服务器拒绝 result≠0）——go-cqhttp ActionFailed 口径，同为 1200。</summary>
    public const int RetActionFailed = 1200;

    public static JsonElement Get(JsonElement obj, string name)
    {
        if (obj.ValueKind == JsonValueKind.Object &&
            obj.TryGetProperty(name, out var v)) return v;
        return default;
    }

    public static string? Str(JsonElement obj, string name)
    {
        var v = Get(obj, name);
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    public static long Long(JsonElement obj, string name, long def = 0)
    {
        var v = Get(obj, name);
        if (v.ValueKind == JsonValueKind.Number) return v.TryGetInt64(out var l) ? l : (long)v.GetDouble();
        if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var l2)) return l2;
        return def;
    }

    public static bool Bool(JsonElement obj, string name, bool def = false)
    {
        var v = Get(obj, name);
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => v.GetString() == "true" || v.GetString() == "1",
            JsonValueKind.Number => v.GetInt64() != 0,
            _ => def,
        };
    }
}

/// <summary>Standard OB11 response envelope.</summary>
public class Ob11Response
{
    public string Status { get; set; } = "ok";
    public long Retcode { get; set; } = 0;
    public object? Data { get; set; }
    public string? Wording { get; set; }
    public string? Echo { get; set; }
    /// <summary>echo 原样保真（标准：echo 可以是任意 JSON 类型）。设置后序列化优先于 Echo。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public JsonElement? EchoRaw { get; set; }

    public static Ob11Response Ok(object? data = null, string? echo = null, JsonElement? echoRaw = null) =>
        new() { Status = "ok", Retcode = Ob11.RetOk, Data = data ?? new object(), Echo = echo, EchoRaw = echoRaw };

    /// <summary>标准异步响应：status=async, retcode=1, data=null。</summary>
    public static Ob11Response Async(string? echo = null, JsonElement? echoRaw = null) =>
        new() { Status = "async", Retcode = 1, Data = null, Echo = echo, EchoRaw = echoRaw };

    public static Ob11Response Fail(int retcode, string wording = "", object? data = null, string? echo = null, JsonElement? echoRaw = null) =>
        new() { Status = "failed", Retcode = retcode, Wording = wording, Data = data ?? new object(), Echo = echo, EchoRaw = echoRaw };

    /// <summary>Wire JSON：status/retcode/data/wording/echo，echo 为原始 JSON 类型。</summary>
    public string ToJson()
    {
        var dict = new Dictionary<string, object?>
        {
            ["status"] = Status,
            ["retcode"] = Retcode,
            ["data"] = Data,
            ["wording"] = Wording, // null → 序列化时省略
            ["echo"] = EchoRaw ?? (object?)Echo,
        };
        return Ob11Json.Serialize(dict);
    }
}

/// <summary>标准：API 不存在（HTTP 404 / WS retcode 1404）。</summary>
public class Ob11UnknownActionException(string msg) : Exception(msg);
/// <summary>set_restart 等已受理但响应被中断的动作 → status=async。</summary>
public class Ob11AsyncSignal : Exception;
