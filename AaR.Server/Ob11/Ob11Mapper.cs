using System.Security.Cryptography;
using System.Text.Json;
using AaR.Server.Hubs;
using AaR.Server.Services;
using Microsoft.AspNetCore.SignalR;

namespace AaR.Server.Ob11;

/// <summary>
/// Conversion layer between internal QQ NT data and OneBot v11 shapes.
/// Internal element numeric types (verified against wrapper.node):
///   TEXT=1, PIC=2, FILE=3, PTT=4, VIDEO=5, FACE=6, REPLY=7, GreyTip=8, ARK=10.
/// Ats inside messages are TEXT elements with atType: 2=person, 1=all.
/// </summary>
public static class Ob11Mapper
{
    // ---------- OB11 segments -> internal elements ----------

    public static async Task<List<Dictionary<string, object?>>> SegmentsToElementsAsync(
        JsonElement message, BridgeRegistry registry, IHubContext<AaRHub> hub,
        Dictionary<string, string> msgIdToPeer, int chatType, string peerUidForReply,
        Func<string, Task<string?>> resolveUidAsync)
    {
        var elements = new List<Dictionary<string, object?>>();
        var segments = NormalizeSegments(message);

        foreach (var seg in segments)
        {
            var type = seg.TryGetValue("type", out var t) ? t?.ToString() : null;
            // data can arrive as JsonElement (native JSON), Dictionary (CQ parse) or
            // anonymous object (array with plain-string items) — normalize to JsonElement
            var data = seg.TryGetValue("data", out var d)
                ? d switch
                {
                    JsonElement de when de.ValueKind == JsonValueKind.Object => de,
                    Dictionary<string, object?> dict => JsonSerializer.SerializeToElement(dict),
                    _ => JsonSerializer.SerializeToElement(d ?? new { }),
                }
                : (JsonElement)default;
            string? S(string k) => data.ValueKind == JsonValueKind.Object && data.TryGetProperty(k, out var sv)
                ? sv.ToString()
                : null;

            switch (type)
            {
                case "text":
                    elements.Add(TextEl(S("text") ?? ""));
                    break;

                case "at":
                    {
                        var qq = S("qq") ?? "";
                        if (qq == "all")
                        {
                            var el = TextEl("@全体成员 ");
                            SetAt(el, atType: 1, atUid: "");
                            elements.Add(el);
                        }
                        else
                        {
                            var uid = await resolveUidAsync(qq);
                            var nick = S("name") ?? qq;
                            // NT at semantics: atUid carries the uin, atNtUid carries the uid
                            var el = TextEl("@" + nick + " ");
                            SetAt(el, atType: 2, atUid: qq, atNtUid: uid ?? "");
                            elements.Add(el);
                        }
                        break;
                    }

                case "face":
                    elements.Add(new Dictionary<string, object?>
                    {
                        ["elementType"] = 6,
                        ["elementId"] = "",
                        ["faceElement"] = new Dictionary<string, object?>
                        {
                            ["faceIndex"] = S("id") is { } id && long.TryParse(id, out var fi) ? fi : 0,
                            // NT rule: classic faces (id < 222) use OldFace(1), newer use Normal(2)
                            ["faceType"] = S("id") is { } id2 && long.TryParse(id2, out var fi2) && fi2 >= 222 ? 2 : 1,
                            ["faceText"] = "",
                            ["packId"] = "0",
                            ["stickerId"] = "0",
                            ["sourceType"] = 1,
                            ["stickerType"] = 1,
                            ["randomType"] = 0,
                        },
                    });
                    break;

                case "reply":
                    {
                        var replyMsgId = S("id") ?? "";
                        var seq = msgIdToPeer.TryGetValue(replyMsgId, out var info) && info.Contains('|')
                            ? info.Split('|')[2] : "0";
                        elements.Add(new Dictionary<string, object?>
                        {
                            ["elementType"] = 7,
                            ["elementId"] = "",
                            ["replyElement"] = new Dictionary<string, object?>
                            {
                                ["replayMsgSeq"] = seq,
                                ["replayMsgId"] = replyMsgId,
                                ["senderUin"] = "",
                                ["senderUinStr"] = "",
                                ["sourceMsgIdInRecords"] = replyMsgId,
                            },
                        });
                        break;
                    }

                case "image":
                    {
                        var b64 = S("base64");
                        var pic = await BuildPicElementAsync(registry, hub, S("file"), S("url"), b64);
                        if (pic is not null) elements.Add(pic);
                        break;
                    }

                case "record":
                    {
                        var ptt = await BuildStagedElementAsync(registry, hub, S("file"), kind: "record", elementType: 4, extHint: "amr");
                        if (ptt is null) throw new Ob11BadException("record file not resolvable: " + S("file"));
                        var ext = Path.GetExtension(ptt.Value.path).ToLowerInvariant();
                        var bytes = await File.ReadAllBytesAsync(ptt.Value.path);
                        // duration heuristic: AMR 12.2kbps ≈ 1600 B/s; silk frames ≈ 28 B / 20ms
                        int duration = ext == ".amr" ? Math.Max(1, bytes.Length / 1600) : ext == ".silk" ? Math.Max(1, bytes.Length / 1400) : 1;
                        elements.Add(new Dictionary<string, object?>
                        {
                            ["elementType"] = 4,
                            ["elementId"] = "",
                            ["pttElement"] = new Dictionary<string, object?>
                            {
                                ["fileName"] = ptt.Value.fileName,
                                ["filePath"] = ptt.Value.path,
                                ["md5HexStr"] = ptt.Value.md5,
                                ["fileSize"] = ptt.Value.fileSize.ToString(),
                                ["duration"] = duration,
                                ["formatType"] = ext == ".amr" ? 2 : 1,
                                ["voiceType"] = 1,
                                ["voiceChangeType"] = 0,
                                ["canConvert2Text"] = true,
                                ["waveAmplitudes"] = new[] { 0, 18, 9, 23, 16, 17, 16, 15, 44, 17, 24, 20, 14, 15, 17 },
                                ["fileSubId"] = "",
                                ["playState"] = 1,
                                ["autoConvertText"] = 0,
                                ["storeID"] = 0,
                                ["otherBusinessInfo"] = new Dictionary<string, object?> { ["aiVoiceType"] = 0 },
                            },
                        });
                        break;
                    }

                case "video":
                    {
                        var vid = await BuildStagedElementAsync(registry, hub, S("file"), kind: "video", elementType: 5, extHint: "mp4");
                        if (vid is null) throw new Ob11BadException("video file not resolvable: " + S("file"));
                        // thumbnail: NT wants a real file at Thumb/<md5>_0.png inside the media store
                        var thumbPng = DefaultVideoThumb();
                        var staged = await KernelAsync(registry, hub, "msg.prepareMedia", new
                        {
                            srcPath = vid.Value.path,
                            fileName = vid.Value.fileName,
                            elementType = 5,
                            md5HexStr = vid.Value.md5,
                            thumbPngBase64 = Convert.ToBase64String(thumbPng),
                        });
                        var (tw, th) = ProbeImageSize(thumbPng);
                        elements.Add(new Dictionary<string, object?>
                        {
                            ["elementType"] = 5,
                            ["elementId"] = "",
                            ["videoElement"] = new Dictionary<string, object?>
                            {
                                ["fileName"] = Str2(staged, "fileName") ?? vid.Value.fileName,
                                ["filePath"] = Str2(staged, "path") ?? vid.Value.path,
                                ["videoMd5"] = vid.Value.md5,
                                ["thumbMd5"] = Str2(staged, "thumbMd5HexStr") ?? "",
                                ["fileTime"] = 15,
                                ["thumbPath"] = new Dictionary<string, object?> { ["0"] = Str2(staged, "thumbPath") ?? "" },
                                ["thumbSize"] = Ob11.Long(staged, "thumbSize", thumbPng.Length),
                                ["thumbWidth"] = tw > 0 ? tw : 320,
                                ["thumbHeight"] = th > 0 ? th : 180,
                                ["fileSize"] = vid.Value.fileSize.ToString(),
                            },
                        });
                        break;
                    }

                case "file":
                    {
                        var fe = await BuildStagedElementAsync(registry, hub, S("file"), kind: "file", elementType: 3, extHint: "bin");
                        if (fe is null) throw new Ob11BadException("file not resolvable: " + S("file"));
                        elements.Add(new Dictionary<string, object?>
                        {
                            ["elementType"] = 3,
                            ["elementId"] = "",
                            ["fileElement"] = new Dictionary<string, object?>
                            {
                                ["fileName"] = S("name") ?? fe.Value.fileName,
                                ["folderId"] = "",
                                ["filePath"] = fe.Value.path,
                                ["fileSize"] = fe.Value.fileSize.ToString(),
                            },
                        });
                        break;
                    }

                case "json":
                    elements.Add(new Dictionary<string, object?>
                    {
                        ["elementType"] = 10,
                        ["elementId"] = "",
                        ["arkElement"] = new Dictionary<string, object?>
                        {
                            ["bytesData"] = S("data") ?? "{}",
                            ["linkInfo"] = null,
                            ["subElementType"] = null,
                        },
                    });
                    break;

                case "markdown":
                    // NT kernel rejects markdownElement for normal accounts — be explicit
                    // instead of producing a silently-empty message
                    throw new Ob11UnsupportedException(
                        "markdown segment cannot be sent: NT kernel restricts markdownElement to whitelisted official bot accounts");

                case "node":
                case "forward":
                    // merged-forward requires multiForwardMsg pipeline; use send_*_forward_msg
                    throw new Ob11UnsupportedException("forward/node segments are not supported inline; use send_group_forward_msg / send_private_forward_msg");

                default:
                    throw new Ob11UnsupportedException("unknown segment type: " + type);
            }
        }

        if (elements.Count == 0)
            throw new Ob11EmptyException("message is empty");
        return elements;
    }

    public static Dictionary<string, object?> TextEl(string content) => new()
    {
        ["elementType"] = 1,
        ["elementId"] = "",
        ["textElement"] = new Dictionary<string, object?>
        {
            ["content"] = content,
            ["atType"] = 0,
            ["atUid"] = "0",
            ["atTinyId"] = "0",
            ["atNtUid"] = "",
            ["subElementType"] = 0,
            ["atChannelId"] = "0",
            ["needNotify"] = 0,
        },
    };

    public static void SetAt(Dictionary<string, object?> textEl, int atType, string atUid, string? atNtUid = null)
    {
        if (textEl["textElement"] is Dictionary<string, object?> te)
        {
            te["atType"] = atType;
            te["atUid"] = atUid;
            te["atNtUid"] = atNtUid ?? atUid;
        }
    }

    private static List<Dictionary<string, object?>> NormalizeSegments(JsonElement message)
    {
        var list = new List<Dictionary<string, object?>>();
        switch (message.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var s in message.EnumerateArray())
                {
                    if (s.ValueKind == JsonValueKind.Object)
                        list.Add(ObjToDict(s));
                    else if (s.ValueKind == JsonValueKind.String)
                        list.Add(new Dictionary<string, object?> { ["type"] = "text", ["data"] = new { text = s.GetString() } });
                }
                break;
            case JsonValueKind.String:
                list.AddRange(CqCodeToSegments(message.GetString() ?? ""));
                break;
        }
        return list;
    }

    /// <summary>Minimal CQ-code string parser: [CQ:type,k=v,...] with plain text around.</summary>
    public static List<Dictionary<string, object?>> CqCodeToSegments(string raw)
    {
        var result = new List<Dictionary<string, object?>>();
        var text = new System.Text.StringBuilder();
        int i = 0;
        while (i < raw.Length)
        {
            if (raw[i] == '[' && i + 1 < raw.Length && raw[i + 1] == 'C' && raw.IndexOf(']', i) > 0)
            {
                var end = raw.IndexOf(']', i);
                var body = raw[(i + 4)..end]; // skip [CQ:
                if (text.Length > 0) { result.Add(Seg("text", new Dictionary<string, object?> { ["text"] = UnescapeCq(text.ToString()) })); text.Clear(); }
                var parts = body.Split(',');
                var type = parts[0].Trim();
                var data = new Dictionary<string, object?>();
                for (int j = 1; j < parts.Length; j++)
                {
                    var kv = parts[j].Split('=', 2);
                    if (kv.Length == 2) data[kv[0].Trim()] = UnescapeCq(kv[1].Trim());
                }
                result.Add(Seg(type, data));
                i = end + 1;
            }
            else { text.Append(raw[i]); i++; }
        }
        if (text.Length > 0) result.Add(Seg("text", new Dictionary<string, object?> { ["text"] = UnescapeCq(text.ToString()) }));
        return result;
    }

    private static Dictionary<string, object?> Seg(string type, Dictionary<string, object?> data) => new()
    {
        ["type"] = type,
        ["data"] = data,
    };

    private static string UnescapeCq(string s) => s
        .Replace("&#91;", "[").Replace("&#93;", "]")
        .Replace("&#44;", ",").Replace("&amp;", "&");

    private static Dictionary<string, object?> ObjToDict(JsonElement obj)
    {
        var d = new Dictionary<string, object?>();
        foreach (var p in obj.EnumerateObject())
            d[p.Name] = p.Value.Clone();
        return d;
    }

    // ---------- media file helpers ----------

    /// <summary>Raw kernel RPC helper for media staging (protocol logic stays server-side).</summary>
    private static async Task<JsonElement> KernelAsync(BridgeRegistry registry, IHubContext<Hubs.AaRHub> hub, string method, object rpcParams)
    {
        var (ok, data, err) = await registry.CallJsonAsync(hub, method, rpcParams);
        if (!ok) throw new Ob11BadException($"kernel call {method} failed: {err}");
        return data ?? JsonSerializer.SerializeToElement(new object());
    }

    /// <summary>320x180 dark PNG used as a minimal video thumbnail (no ffmpeg dependency).</summary>
    private static byte[] DefaultVideoThumb()
    {
        const int w = 320, h = 180;
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        void Chunk(string type, byte[] data)
        {
            var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
            var len = BitConverter.GetBytes(data.Length);
            if (BitConverter.IsLittleEndian) Array.Reverse(len);
            ms.Write(len);
            ms.Write(typeBytes);
            ms.Write(data);
            var crcInput = new byte[4 + data.Length];
            typeBytes.CopyTo(crcInput, 0);
            data.CopyTo(crcInput, 4);
            uint c = 0xFFFFFFFF;
            foreach (var by in crcInput) c = CrcTable[(c ^ by) & 0xFF] ^ (c >> 8);
            c ^= 0xFFFFFFFF;
            var cb = BitConverter.GetBytes(c);
            if (BitConverter.IsLittleEndian) Array.Reverse(cb);
            ms.Write(cb);
        }
        var ihdr = new byte[13];
        unchecked { ihdr[2] = (byte)(w >> 8); ihdr[3] = (byte)w; ihdr[6] = (byte)(h >> 8); ihdr[7] = (byte)h; }
        ihdr[8] = 8; ihdr[9] = 2; // 8-bit RGB
        Chunk("IHDR", ihdr);
        var raw = new byte[(w * 3 + 1) * h];
        int i = 0;
        for (int y = 0; y < h; y++)
        {
            raw[i++] = 0;
            for (int x = 0; x < w; x++) { raw[i++] = 24; raw[i++] = 24; raw[i++] = 28; }
        }
        Chunk("IDAT", Compress(raw));
        Chunk("IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    private static uint[]? _crcTable;
    private static uint[] CrcTable => _crcTable ??= BuildCrcTable();
    private static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    private static byte[] Compress(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var z = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionLevel.Fastest, true))
            z.Write(data, 0, data.Length);
        return ms.ToArray();
    }

    private static string? DetectImageExtension(byte[] b) => b.Length switch
    {
        _ when b.Length > 12 && b[0] == 0x89 && b[1] == 0x50 => "png",
        _ when b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 => "jpg",
        _ when b.Length > 3 && b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46 => "gif",
        _ when b.Length > 12 && b[0] == 0x52 && b[1] == 0x49 && b[8] == 0x57 && b[9] == 0x45 => "webp",
        _ when b.Length > 2 && b[0] == 0x42 && b[1] == 0x4D => "bmp",
        _ => null,
    };

    private static (int W, int H) ProbeWebpSize(byte[] b)
    {
        // RIFF....WEBPVP8 / VP8L / VP8X
        if (b.Length < 30 || b[8] != 0x56) return (0, 0);
        if (b[12] == 0x56 && b[13] == 0x58) // "VP8X"
            return (1 + (b[24] | (b[25] << 8) | (b[26] << 16)), 1 + (b[27] | (b[28] << 8) | (b[29] << 16)));
        if (b[12] == 0x56 && b[13] == 0x50 && b[14] == 0x20) // "VP8 "
        {
            int w = ((b[26] | (b[27] << 8)) & 0x3FFF), h = ((b[28] | (b[29] << 8)) & 0x3FFF);
            return (w, h);
        }
        return (0, 0);
    }


    private static async Task<Dictionary<string, object?>?> BuildPicElementAsync(
        BridgeRegistry registry, IHubContext<Hubs.AaRHub> hub, string? file, string? url, string? base64Data = null)
    {
        if (string.IsNullOrEmpty(file) && string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(base64Data))
        {
            var b64 = base64Data.Contains("base64,") ? base64Data[(base64Data.IndexOf("base64,") + 7)..] : base64Data;
            var bytes = Convert.FromBase64String(b64);
            var ext = DetectImageExtension(bytes) ?? "bin";
            var tmp = Path.Combine(Path.GetTempPath(), "aar", "img-" + Guid.NewGuid().ToString("N") + "." + ext);
            Directory.CreateDirectory(Path.GetDirectoryName(tmp)!);
            await File.WriteAllBytesAsync(tmp, bytes);
            file = tmp;
        }
        var path = await MaterializeFileAsync(file, url, "img");
        if (path is null) throw new Ob11BadException("image file not resolvable: " + (file ?? url));
        // normalize extension by magic bytes when it is missing or generic (.bin)
        var curExt = Path.GetExtension(path).ToLowerInvariant().TrimStart('.');
        if (curExt is "" or "bin")
        {
            var head = await File.ReadAllBytesAsync(path);
            var detected = DetectImageExtension(head);
            if (detected is not null && detected != curExt)
            {
                var renamed = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "." + detected);
                File.Move(path, renamed, overwrite: true);
                path = renamed;
            }
        }
        var bytes2 = await File.ReadAllBytesAsync(path);
        var md5 = Convert.ToHexString(MD5.HashData(bytes2)).ToLowerInvariant();
        var (w, h) = ProbeImageSize(bytes2);
        if ((w, h) == (0, 0) && bytes2.Length > 12 && bytes2[0] == 0x52 && bytes2[8] == 0x57)
            (w, h) = ProbeWebpSize(bytes2);
        var ext2 = Path.GetExtension(path).ToLowerInvariant().TrimStart('.');
        var picType = ext2 switch
        {
            "png" => 1001,
            "gif" => 2000,
            "bmp" => 1005,
            "webp" => 1002,
            _ => 1000,
        };
        // stage into QQ's own media store — sendMsg rejects foreign paths
        // ("文件被修改，请稍后再试") unless the file lives in nt_data
        var staged = await KernelAsync(registry, hub, "msg.prepareMedia", new
        {
            srcPath = path,
            fileName = Path.GetFileName(path),
            elementType = 2,
            md5HexStr = md5,
        });
        var sPath = Str2(staged, "path") ?? path;
        var sName = Str2(staged, "fileName") ?? Path.GetFileName(path);
        return new Dictionary<string, object?>
        {
            ["elementType"] = 2,
            ["elementId"] = "",
            ["picElement"] = new Dictionary<string, object?>
            {
                ["md5HexStr"] = md5,
                ["fileSize"] = bytes2.Length.ToString(),
                ["picWidth"] = w,
                ["picHeight"] = h,
                ["fileName"] = sName,
                ["sourcePath"] = sPath,
                ["original"] = true,
                ["picType"] = picType,
                ["picSubType"] = 0,
                ["fileUuid"] = "",
                ["fileSubId"] = "",
                ["thumbFileSize"] = 0,
                ["summary"] = "[图片]",
            },
        };
    }

    /// <summary>Materialize a local/url/base64 source and stage it in QQ's media store.</summary>
    private static async Task<(string path, string fileName, string md5, long fileSize)?> BuildStagedElementAsync(
        BridgeRegistry registry, IHubContext<Hubs.AaRHub> hub, string? file, string kind, int elementType, string extHint)
    {
        var path = await MaterializeFileAsync(file, null, kind, extHint);
        if (path is null) return null;
        var bytes = await File.ReadAllBytesAsync(path);
        var md5 = Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();
        var staged = await KernelAsync(registry, hub, "msg.prepareMedia", new
        {
            srcPath = path,
            fileName = Path.GetFileName(path),
            elementType,
            md5HexStr = md5,
        });
        return (
            Str2(staged, "path") ?? path,
            Str2(staged, "fileName") ?? Path.GetFileName(path),
            md5,
            Ob11.Long(staged, "fileSize", bytes.Length));
    }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private static async Task<string?> MaterializeFileAsync(string? file, string? url, string kind, string extHint = "bin")
    {
        // accepted: local path, file://, http(s) url, base64://, or bare md5-hinted path
        string? candidate = null;
        if (!string.IsNullOrEmpty(url) && (url.StartsWith("http://") || url.StartsWith("https://")))
            candidate = url;
        if (candidate is null && !string.IsNullOrEmpty(file))
        {
            if (file.StartsWith("file://")) candidate = file[7..];
            else if (file.StartsWith("base64://"))
            {
                var b64 = file[9..];
                var tmp = Path.Combine(Path.GetTempPath(), "aar", kind + "-" + Guid.NewGuid().ToString("N") + ".bin");
                Directory.CreateDirectory(Path.GetDirectoryName(tmp)!);
                await File.WriteAllBytesAsync(tmp, Convert.FromBase64String(b64));
                return tmp;
            }
            else if (file.StartsWith("http://") || file.StartsWith("https://")) candidate = file;
            else if (File.Exists(file)) return file;
        }
        if (candidate is null) return null;
        var ext = Path.GetExtension(new Uri(candidate).AbsolutePath);
        if (string.IsNullOrEmpty(ext)) ext = "." + extHint;
        var tmp2 = Path.Combine(Path.GetTempPath(), "aar", kind + "-" + Guid.NewGuid().ToString("N") + ext);
        Directory.CreateDirectory(Path.GetDirectoryName(tmp2)!);
        using var resp = await Http.GetAsync(candidate);
        resp.EnsureSuccessStatusCode();
        await using var fs = File.Create(tmp2);
        await resp.Content.CopyToAsync(fs);
        return tmp2;
    }

    /// <summary>Minimal dimension probe for png/jpeg/gif/bmp.</summary>
    public static (int W, int H) ProbeImageSize(byte[] b)
    {
        try
        {
            if (b.Length > 24 && b[0] == 0x89 && b[1] == 0x50) // PNG
                return (ReadInt(b, 16), ReadInt(b, 20));
            if (b.Length > 10 && b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46) // GIF
                return (b[6] | (b[7] << 8), b[8] | (b[9] << 8));
            if (b.Length > 26 && b[0] == 0x42 && b[1] == 0x4D) // BMP
                return (ReadInt(b, 18), ReadInt(b, 22));
            if (b.Length > 4 && b[0] == 0xFF && b[1] == 0xD8) // JPEG
            {
                int i = 2;
                while (i + 9 < b.Length)
                {
                    if (b[i] != 0xFF) { i++; continue; }
                    var marker = b[i + 1];
                    if (marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7
                        or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF)
                        return ((b[i + 7] << 8) | b[i + 8], (b[i + 5] << 8) | b[i + 6]);
                    var len = (b[i + 2] << 8) | b[i + 3];
                    i += 2 + len;
                }
            }
        }
        catch { }
        return (0, 0);
    }

    private static int ReadInt(byte[] b, int off) => (b[off] << 24) | (b[off + 1] << 16) | (b[off + 2] << 8) | b[off + 3];

    // ---------- internal elements -> OB11 segments ----------

    public static List<Dictionary<string, object?>> ElementsToSegments(JsonElement elements, Func<string, string?> uidToUin)
    {
        var segments = new List<Dictionary<string, object?>>();
        if (elements.ValueKind != JsonValueKind.Array) return segments;

        foreach (var el in elements.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;
            var etype = el.TryGetProperty("elementType", out var et) && et.TryGetInt32(out var eti) ? eti : -1;

            if (Has(el, "textElement"))
            {
                var te = el.GetProperty("textElement");
                var content = te.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
                var atType = te.TryGetProperty("atType", out var at) && at.TryGetInt32(out var ati) ? ati : 0;
                if (atType == 1)
                    segments.Add(Seg("at", new Dictionary<string, object?> { ["qq"] = "all" }));
                else if (atType == 2)
                {
                    // NT at semantics: atUid carries the uin, atNtUid carries the uid.
                    // Prefer a numeric atUid directly; resolve atNtUid via uid->uin otherwise.
                    var atUid = te.TryGetProperty("atUid", out var au) ? au.GetString() ?? "" : "";
                    var atNtUid = te.TryGetProperty("atNtUid", out var au2) ? au2.GetString() ?? "" : "";
                    string? uin = null;
                    if (!string.IsNullOrEmpty(atUid) && atUid.All(char.IsDigit))
                        uin = atUid;
                    else if (!string.IsNullOrEmpty(atNtUid))
                        uin = uidToUin(atNtUid) ?? (atNtUid.All(char.IsDigit) ? atNtUid : null);
                    else if (!string.IsNullOrEmpty(atUid))
                        uin = uidToUin(atUid);
                    segments.Add(Seg("at", new Dictionary<string, object?> { ["qq"] = uin ?? "" }));
                }
                if (content.Length > 0) segments.Add(Seg("text", new Dictionary<string, object?> { ["text"] = content }));
            }
            else if (Has(el, "faceElement"))
            {
                var fe = el.GetProperty("faceElement");
                segments.Add(Seg("face", new Dictionary<string, object?>
                {
                    ["id"] = fe.TryGetProperty("faceIndex", out var fi) ? fi.GetRawText() : "0",
                }));
            }
            else if (Has(el, "picElement"))
            {
                var pe = el.GetProperty("picElement");
                var md5 = Str2(pe, "md5HexStr") ?? "";
                // NT v2 urls (appid=1406/1407) require an rkey we cannot fetch without
                // the packet stack; if the url already carries one use it, otherwise
                // fall back to the legacy md5-addressed qpic url.
                string? url2 = null;
                var origin = Str2(pe, "originImageUrl") ?? Str2(pe, "peerOriginImageUrl");
                if (!string.IsNullOrEmpty(origin))
                {
                    url2 = origin.Contains("rkey=")
                        ? "https://multimedia.nt.qq.com.cn" + origin
                        : null;
                }
                if (string.IsNullOrEmpty(url2) && !string.IsNullOrEmpty(md5))
                    url2 = "https://gchat.qpic.cn/gchatpic_new/0/0-0-" + md5.ToUpperInvariant() + "/0";
                segments.Add(Seg("image", new Dictionary<string, object?>
                {
                    ["file"] = md5,
                    ["url"] = url2 ?? "",
                }));
            }
            else if (Has(el, "replyElement"))
            {
                var re = el.GetProperty("replyElement");
                // sourceMsgIdInRecords holds NT's cloud-side id for the quoted message
                // (not queryable via getByMsgIds); replayMsgSeq is the reliable local
                // key — we ship both and let the server rewrite `id` from its seq index
                var rid = Str2(re, "sourceMsgIdInRecords") ?? Str2(re, "replayMsgId") ?? Str2(re, "replyMsgId") ?? "";
                var rseq = Str2(re, "replayMsgSeq") ?? "0";
                segments.Add(Seg("reply", new Dictionary<string, object?>
                {
                    ["id"] = rid,
                    ["seq"] = rseq,
                }));
            }
            else if (Has(el, "pttElement"))
            {
                var pe = el.GetProperty("pttElement");
                var recordData = new Dictionary<string, object?>
                {
                    ["file"] = Str2(pe, "md5HexStr") ?? "",
                    ["file_size"] = Str2(pe, "fileSize") ?? "",
                };
                var pttPath = Str2(pe, "filePath");
                if (!string.IsNullOrEmpty(pttPath) && File.Exists(pttPath))
                    recordData["path"] = pttPath;
                var pttUrl = Str2(pe, "url");
                if (!string.IsNullOrEmpty(pttUrl))
                    recordData["url"] = pttUrl;
                segments.Add(Seg("record", recordData));
            }
            else if (Has(el, "videoElement"))
            {
                var ve = el.GetProperty("videoElement");
                var videoData = new Dictionary<string, object?>
                {
                    ["file"] = Str2(ve, "videoMd5") ?? Str2(ve, "fileMd5") ?? "",
                    ["file_size"] = Str2(ve, "fileSize") ?? "",
                };
                var vPath = Str2(ve, "filePath");
                if (!string.IsNullOrEmpty(vPath) && File.Exists(vPath))
                    videoData["path"] = vPath;
                segments.Add(Seg("video", videoData));
            }
            else if (Has(el, "marketFaceElement"))
            {
                var mf = el.GetProperty("marketFaceElement");
                segments.Add(Seg("mface", new Dictionary<string, object?>
                {
                    ["emoji_id"] = Str2(mf, "emojiId") ?? "",
                    ["emoji_name"] = Str2(mf, "emojiName") ?? "",
                    ["key"] = Str2(mf, "key") ?? "",
                }));
            }
            else if (Has(el, "fileElement"))
            {
                var fe = el.GetProperty("fileElement");
                segments.Add(Seg("file", new Dictionary<string, object?>
                {
                    ["file_id"] = Str2(fe, "fileUuid") ?? "",
                    ["name"] = Str2(fe, "fileName") ?? "",
                    ["size"] = Str2(fe, "fileSize") ?? "0",
                    ["md5"] = Str2(fe, "fileMd5") ?? "",
                }));
            }
            else if (Has(el, "arkElement"))
            {
                var ae = el.GetProperty("arkElement");
                segments.Add(Seg("json", new Dictionary<string, object?>
                {
                    ["data"] = Str2(ae, "bytesData") ?? "{}",
                }));
            }
            else if (Has(el, "markdownElement"))
            {
                var me = el.GetProperty("markdownElement");
                segments.Add(Seg("markdown", new Dictionary<string, object?>
                {
                    ["content"] = Str2(me, "content") ?? "",
                }));
            }
            else if (Has(el, "grayTipElement"))
            {
                // gray tips are surfaced as notices by the event pipeline, not as segments
            }
            // elementType fallback for exotic types: emit nothing (logged upstream)
            else if (etype is -1) { }
        }
        return segments;
    }

    private static bool Has(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object &&
        v.EnumerateObject().Any();

    /// <summary>Indexable media reference extracted from a message record.</summary>
    public sealed record MediaRef(
        string Kind, string Key, string ElementId, string MsgId, int ChatType, string PeerUid,
        string? LocalPath, string? Url, string? Name, string? Size);

    /// <summary>Collect pic/ptt/video/file media info for the media index (get_image/get_record/get_file).</summary>
    public static List<MediaRef> CollectMedia(JsonElement elements, string msgId, int chatType, string peerUid)
    {
        var list = new List<MediaRef>();
        if (elements.ValueKind != JsonValueKind.Array) return list;
        foreach (var el in elements.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;
            var elementId = el.TryGetProperty("elementId", out var eid) ? eid.GetString() ?? "" : "";
            void Add(string kind, string? key, string? localPath, string? url, string? name, string? size)
            {
                if (string.IsNullOrEmpty(key) || key == "0") return;
                list.Add(new MediaRef(kind, key.ToLowerInvariant(), elementId, msgId, chatType, peerUid, localPath, url, name, size));
            }
            if (Has(el, "picElement"))
            {
                var pe = el.GetProperty("picElement");
                var md5 = Str2(pe, "md5HexStr");
                var origin = Str2(pe, "originImageUrl") ?? Str2(pe, "peerOriginImageUrl");
                var url = origin != null
                    ? (origin.Contains("rkey=") ? "https://multimedia.nt.qq.com.cn" + origin : null)
                    : null;
                Add("image", md5, Str2(pe, "sourcePath") ?? Str2(pe, "filePath"), url,
                    Str2(pe, "fileName"), Str2(pe, "fileSize"));
            }
            if (Has(el, "pttElement"))
            {
                var pe = el.GetProperty("pttElement");
                Add("record", Str2(pe, "md5HexStr"), Str2(pe, "filePath"), Str2(pe, "url"),
                    Str2(pe, "fileName"), Str2(pe, "fileSize"));
            }
            if (Has(el, "videoElement"))
            {
                var ve = el.GetProperty("videoElement");
                Add("video", Str2(ve, "videoMd5") ?? Str2(ve, "fileMd5"), Str2(ve, "filePath"), null,
                    Str2(ve, "fileName"), Str2(ve, "fileSize"));
            }
            if (Has(el, "fileElement"))
            {
                var fe = el.GetProperty("fileElement");
                var uuid = Str2(fe, "fileUuid");
                Add("file", uuid, Str2(fe, "filePath") ?? Str2(fe, "localFilePath"), null,
                    Str2(fe, "fileName"), Str2(fe, "fileSize"));
                Add("file", Str2(fe, "fileMd5"), Str2(fe, "filePath") ?? Str2(fe, "localFilePath"), null,
                    Str2(fe, "fileName"), Str2(fe, "fileSize"));
            }
        }
        return list;
    }


    private static string? Str2(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static string BuildRawMessage(List<Dictionary<string, object?>> segments)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var s in segments)
        {
            var type = s.TryGetValue("type", out var t) ? t?.ToString() : "";
            var data = s.TryGetValue("data", out var d) && d is Dictionary<string, object?> dd ? dd : new Dictionary<string, object?>();
            switch (type)
            {
                case "text": sb.Append(data.TryGetValue("text", out var tx) ? tx : ""); break;
                case "at": sb.Append("[CQ:at,qq=").Append(data.TryGetValue("qq", out var q) ? q : "").Append(']'); break;
                case "face": sb.Append("[CQ:face,id=").Append(data.TryGetValue("id", out var f) ? f : "").Append(']'); break;
                case "reply": sb.Append("[CQ:reply,id=").Append(data.TryGetValue("id", out var r) ? r : "").Append(']'); break;
                case "image": sb.Append("[CQ:image,file=").Append(data.TryGetValue("file", out var im) ? im : "").Append(']'); break;
                case "record": sb.Append("[CQ:record,file=").Append(data.TryGetValue("file", out var rc) ? rc : "").Append(']'); break;
                case "video": sb.Append("[CQ:video,file=").Append(data.TryGetValue("file", out var vd) ? vd : "").Append(']'); break;
                case "file": sb.Append("[CQ:file,name=").Append(data.TryGetValue("name", out var fn) ? fn : "").Append(']'); break;
                case "json": sb.Append("[CQ:json,data=").Append(data.TryGetValue("data", out var js) ? js : "").Append(']'); break;
                case "markdown": sb.Append("[CQ:markdown,content=").Append(data.TryGetValue("content", out var md) ? md : "").Append(']'); break;
                case "mface": sb.Append("[CQ:mface,emoji_id=").Append(data.TryGetValue("emoji_id", out var mf) ? mf : "").Append(']'); break;
            }
        }
        return sb.ToString();
    }
}

public class Ob11UnsupportedException(string msg) : Exception(msg);
public class Ob11BadException(string msg) : Exception(msg);
public class Ob11EmptyException(string msg) : Exception(msg);
