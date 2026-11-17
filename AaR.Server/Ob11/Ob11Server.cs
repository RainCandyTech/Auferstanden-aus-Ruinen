using System.Collections.Concurrent;
using System.Text.Json;
using AaR.Server.Hubs;
using AaR.Server.Services;
using Microsoft.AspNetCore.SignalR;

namespace AaR.Server.Ob11;

/// <summary>
/// OneBot v11 protocol endpoint: action dispatch (HTTP/WS clients) and
/// internal-event -> OB11-event mapping. All protocol logic lives here.
/// </summary>
public class Ob11Server
{
    private readonly BridgeRegistry _bridge;
    private readonly AaROptions _opts;
    private readonly ILogger<Ob11Server> _logger;
    private readonly IHubContext<AaRHub> _hub;
    private readonly Storage.Ob11Store _store;

    public volatile string SelfUid = "";
    public volatile string SelfUin = "";
    public volatile string SelfNick = "";

    /// <summary>Server version reported by get_version_info and the #AaR debug command.</summary>
    public const string ServerVersion = "0.5.0";

    // msgId -> "chatType|peerUid|seq" for delete_msg / get_msg / reply building
    private readonly ConcurrentDictionary<string, string> _msgIndex = new();
    // FIFO eviction queues for the bounded in-memory indexes: unlike the previous
    // arbitrary Keys.Take(N) trimming this drops the *oldest inserted* entries,
    // so freshly seen message ids always survive
    private readonly ConcurrentQueue<string> _msgIndexOrder = new();
    // "chatType|peerUid|seq" -> msgId, to resolve reply quotes (replayMsgSeq)
    private readonly ConcurrentDictionary<string, string> _seqIndex = new();
    private readonly ConcurrentQueue<string> _seqIndexOrder = new();
    // media key (md5/uuid) -> media reference for get_image / get_record / get_file
    private readonly ConcurrentDictionary<string, Ob11Mapper.MediaRef> _mediaIndex = new();
    private readonly ConcurrentQueue<string> _mediaIndexOrder = new();
    // recall event dedup: msgId -> emitted tick (shared by both recall paths)
    private readonly ConcurrentDictionary<string, long> _recallEmitted = new();
    // last recall emission per peer ("chatType|peerUid" -> tick): the listUpdate and
    // grayTip paths describe the *same* recall event milliseconds apart — this keeps
    // them from double-emitting without swallowing distinct recalls (>3s apart)
    private readonly ConcurrentDictionary<string, long> _recentRecallPeer = new();
    // peer -> (operatorUin, tick) learned from grayTip revoke tips; the listUpdate
    // path has the real message_id but no operator info, so it borrows this
    private readonly ConcurrentDictionary<string, (string OperatorUin, long Tick)> _recallOperator = new();
    // sent-message marker -> real msgId (delivered via onMsgInfoListUpdate)
    private readonly ConcurrentDictionary<string, string> _markerToRealId = new();
    private readonly ConcurrentQueue<string> _markerOrder = new();
    private readonly CancellationTokenSource _shutdownCts = new();
    // uid -> uin cache, learned from every incoming record and kernel list calls
    private readonly ConcurrentDictionary<string, string> _uidToUin = new();
    // per-connection send locks: concurrent WebSocket.SendAsync on one socket
    // throws InvalidOperationException (.NET forbids parallel sends)
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _wsSendLocks = new();
    // #AaR debug command: last trigger tick per peer ("chatType|peerUid") for a
    // small anti-spam / anti-loop cooldown
    private readonly ConcurrentDictionary<string, long> _debugCmdPeerTick = new();

    /// <summary>Force OneBot id-ish fields to strings (JS frameworks overflow >2^53 ints).</summary>
    private static void NormalizeIdFields(Dictionary<string, object?> evt)
    {
        foreach (var key in new[] { "message_id", "user_id", "group_id", "operator_id", "target_id", "self_id", "res_id" })
        {
            if (evt.TryGetValue(key, out var v) && v is not null && v.GetType() != typeof(string))
                evt[key] = v.ToString();
            else if (evt.TryGetValue(key, out var v2) && v2 is null && key != "res_id" && key != "group_id")
                evt[key] = "";
        }
        if (evt.TryGetValue("sender", out var snd) && snd is Dictionary<string, object?> sd &&
            sd.TryGetValue("user_id", out var suid) && suid is not null && suid.GetType() != typeof(string))
            sd["user_id"] = suid.ToString();
    }
    // connected OB11 WS clients (forward ws) + per-client mode (standard /api /event / universal)
    private readonly ConcurrentDictionary<string, System.Net.WebSockets.WebSocket> _wsClients = new();
    private readonly ConcurrentDictionary<string, WsClientMode> _wsModes = new();
    private DateTime _startedAt = DateTime.UtcNow;

    public Ob11Server(BridgeRegistry bridge, AaROptions opts, ILogger<Ob11Server> logger, IHubContext<AaRHub> hub, Storage.Ob11Store store)
    {
        _bridge = bridge;
        _opts = opts;
        _logger = logger;
        _hub = hub;
        _store = store;
        _bridge.BridgeChanged += () =>
        {
            if (_bridge.IsBridgeConnected)
                _ = Task.Run(async () =>
                {
                    await RefreshSelfLoopAsync();
                    // prime the uid->uin cache right away so events arriving before
                    // any get_friend_list / get_group_list call still resolve uins
                    await PrimeUinLookupAsync();
                });
        };
        _ = Task.Run(() => HeartbeatLoopAsync(_shutdownCts.Token));
    }

    /// <summary>Forward-WS client capability: 标准 /api /event 分路与 universal 全能。</summary>
    public enum WsClientMode { Universal, Api, Event }

    public void RegisterWsClient(string id, System.Net.WebSockets.WebSocket ws, WsClientMode mode = WsClientMode.Universal)
    {
        _wsClients[id] = ws;
        _wsModes[id] = mode;
        _wsSendLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
    }

    public void RemoveWsClient(string id)
    {
        _wsClients.TryRemove(id, out _);
        _wsModes.TryRemove(id, out _);
        if (_wsSendLocks.TryRemove(id, out var l)) l.Dispose();
    }

    /// <summary>Serialize a text frame onto a registered forward-WS client.</summary>
    public async Task SendWsFrameAsync(string clientId, System.Net.WebSockets.WebSocket ws, byte[] bytes)
    {
        var l = _wsSendLocks.GetOrAdd(clientId, _ => new SemaphoreSlim(1, 1));
        await l.WaitAsync();
        try
        {
            if (ws.State != System.Net.WebSockets.WebSocketState.Open) return;
            await ws.SendAsync(new ArraySegment<byte>(bytes),
                System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
        }
        finally { l.Release(); }
    }

    /// <summary>Cancel background loops on host shutdown (called from Program.cs).</summary>
    public void Shutdown() => _shutdownCts.Cancel();
    public int WsClientCount => _wsClients.Count;

    private async Task RefreshSelfLoopAsync()
    {
        for (int i = 0; i < 60 && string.IsNullOrEmpty(SelfUin); i++)
        {
            await RefreshSelfAsync();
            if (string.IsNullOrEmpty(SelfUin)) await Task.Delay(5000);
        }
    }

    private async Task RefreshSelfAsync()
    {
        try
        {
            var (ok, dataOpt, err) = await _bridge.CallJsonAsync(_hub, "self.info", null, 10000);
            if (ok && dataOpt.HasValue)
            {
                var data = dataOpt.Value;
                SelfUid = Ob11.Str(data, "uid") ?? SelfUid;
                SelfUin = Ob11.Str(data, "uin") ?? SelfUin;
                SelfNick = Ob11.Str(data, "nick") ?? SelfNick;
                _logger.LogInformation("self info: uin={Uin} uid={Uid} nick={Nick}", SelfUin, SelfUid, SelfNick);
                await BroadcastAsync(new Dictionary<string, object?>
                {
                    ["time"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    ["self_id"] = SelfUin,
                    ["post_type"] = "meta_event",
                    ["meta_event_type"] = "lifecycle",
                    ["sub_type"] = "connect",
                });
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "self.info refresh failed"); }
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(Math.Max(5, _opts.HeartbeatIntervalMs), ct); }
            catch (OperationCanceledException) { break; }
            // broadcast unconditionally: forward-ws clients AND reverse transports
            // (EventEmitted subscribers) all need heartbeats for liveness detection
            await BroadcastAsync(new Dictionary<string, object?>
            {
                ["time"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ["self_id"] = SelfUin,
                ["post_type"] = "meta_event",
                ["meta_event_type"] = "heartbeat",
                ["status"] = new Dictionary<string, object?>
                {
                    ["online"] = _bridge.IsBridgeConnected,
                    ["good"] = _bridge.IsBridgeConnected,
                },
                ["interval"] = _opts.HeartbeatIntervalMs,
            });
        }
    }

    // ================= internal events -> OB11 events =================

    public volatile int EventsReceived;
    public volatile int EventsBroadcast;

    // shared HttpClient for download_file (a per-call new HttpClient() leaks sockets)
    private static readonly HttpClient DownloadHttp = new() { Timeout = TimeSpan.FromSeconds(120) };

    public async Task OnInternalEventAsync(string name, string payloadJson)
    {
        EventsReceived++;
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement.Clone();
            _logger.LogInformation("mapping {Name}: {Kind}", name, root.ValueKind);

            switch (name)
            {
                case "onRecvMsg":
                    await HandleRecvMsgAsync(root);
                    break;
                case "onMsgInfoListUpdate":
                    await HandleMsgListUpdateAsync(root);
                    break;
                case "onBuddyReqChange":
                    await HandleBuddyReqAsync(root);
                    break;
                default:
                    break; // raw kernel events not mapped in v1
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "event mapping failed for {Name}", name);
        }
    }

    private async Task HandleRecvMsgAsync(JsonElement root)
    {
        // payload is the listener args array: [ msgRecords ]
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0) return;
        if (root[0].ValueKind != JsonValueKind.Array) return;

        foreach (var rec in root[0].EnumerateArray())
        {
            if (rec.ValueKind != JsonValueKind.Object) continue;
            var chatType = Ob11.Long(rec, "chatType");
            if (chatType != 1 && chatType != 2) continue; // private / group only in v1
            var msgType = Ob11.Long(rec, "msgType");
            if (msgType == 5) { await HandleGrayTipsAsync(rec); continue; } // gray-tip only records

            var msgId = Ob11.Str(rec, "msgId") ?? "";
            var peerUid = Ob11.Str(rec, "peerUid") ?? "";

            var isGroup = chatType == 2;
            var senderUid = Ob11.Str(rec, "senderUid") ?? "";
            var senderUin = Ob11.Str(rec, "senderUin") ?? "";
            RememberMessage(rec, msgId, chatType, peerUid, isSelfSent: senderUid == SelfUid);
            RememberSeq(chatType.ToString(), peerUid, Ob11.Str(rec, "msgSeq") ?? "0", msgId);
            LearnUin(senderUid, senderUin);
            if (string.IsNullOrEmpty(senderUin)) senderUin = await ResolveUinCachedAsync(senderUid);
            if (isGroup) _ = EnsureMemberListAsync(peerUid); // one-shot role/card/level preheat

            var segments = await Ob11Mapper.ElementsToSegmentsAsync(
                rec.TryGetProperty("elements", out var els) ? els : default,
                ResolveUinForMapperAsync);
            if (segments.Count == 0) continue;

            IndexMedia(rec, msgId, chatType, peerUid);
            IndexReplyTargets(segments, chatType, peerUid);
            IndexForwardSegments(segments, msgId, chatType, peerUid);

            var senderRole = "member";
            var senderLevel = "";
            var senderCard = isGroup ? (Ob11.Str(rec, "sendMemberName") ?? "") : "";
            if (isGroup && _memberCache.TryGetValue($"{peerUid}|{senderUin}", out var mc))
            {
                senderRole = mc.Role;
                senderLevel = mc.Level;
                if (senderCard.Length == 0) senderCard = mc.Card;
            }

            // #AaR in-chat debug command: fire-and-forget so a slow kernel send
            // never delays event mapping; self-sent records are skipped to
            // guarantee the reply can never re-trigger the command
            if (_opts.EnableDebugCommand && senderUid != SelfUid)
                _ = TryHandleDebugCommandAsync(chatType, peerUid, senderUid, senderUin, msgId, segments);

            var evt = new Dictionary<string, object?>
            {
                ["time"] = Ob11.Long(rec, "msgTime"),
                ["self_id"] = SelfUin,
                ["post_type"] = "message",
                ["message_type"] = isGroup ? "group" : "private",
                ["sub_type"] = Ob11.Str(rec, "subMsgType") == "1" ? "normal" : (isGroup ? "normal" : "friend"),
                ["message_id"] = msgId,
                ["user_id"] = senderUin,
                ["message"] = segments,
                ["raw_message"] = Ob11Mapper.BuildRawMessage(segments),
                ["font"] = 0,
                ["sender"] = new Dictionary<string, object?>
                {
                    ["user_id"] = senderUin,
                    ["nickname"] = Ob11.Str(rec, "sendNickName") ?? "",
                    ["card"] = senderCard,
                    ["sex"] = "unknown",
                    ["age"] = 0,
                    ["role"] = senderRole,
                },
            };
            if (senderLevel.Length > 0) ((Dictionary<string, object?>)evt["sender"]!)["level"] = senderLevel;
            if (isGroup) evt["group_id"] = peerUid;
            // group file upload notice (OneBot v11 notice_type=group_upload)
            if (isGroup && segments.Any(s => s.TryGetValue("type", out var t) && t?.ToString() == "file"))
            {
                var fileSeg = segments.First(s => s.TryGetValue("type", out var t) && t?.ToString() == "file");
                var fd = fileSeg.TryGetValue("data", out var dObj) && dObj is Dictionary<string, object?> fdd ? fdd : new Dictionary<string, object?>();
                await BroadcastAsync(new Dictionary<string, object?>
                {
                    ["time"] = Ob11.Long(rec, "msgTime"),
                    ["self_id"] = SelfUin,
                    ["post_type"] = "notice",
                    ["notice_type"] = "group_upload",
                    ["group_id"] = peerUid,
                    ["user_id"] = senderUin,
                    ["file"] = new Dictionary<string, object?>
                    {
                        ["id"] = fd.TryGetValue("file_id", out var fu) ? fu : "",
                        ["name"] = fd.TryGetValue("name", out var fn) ? fn : "",
                        ["size"] = long.TryParse(fd.TryGetValue("size", out var fz) ? fz?.ToString() : "0", out var sz) ? sz : 0,
                        ["busid"] = 0,
                    },
                });
            }
            await BroadcastAsync(evt);
        }
    }

    private void IndexReplyTargets(List<Dictionary<string, object?>> segments, long chatType, string peerUid)
    {
        // rewrite reply ids to the message_id the framework actually saw: NT's
        // sourceMsgIdInRecords is a cloud-side id, but replayMsgSeq resolves
        // against our local seq index
        foreach (var s in segments)
        {
            if (!s.TryGetValue("type", out var t) || t?.ToString() != "reply") continue;
            if (s.TryGetValue("data", out var d) && d is Dictionary<string, object?> dd)
            {
                if (dd.TryGetValue("seq", out var sq) && sq?.ToString() is { } seq && seq != "0" &&
                    _seqIndex.TryGetValue($"{chatType}|{peerUid}|{seq}", out var realId))
                    dd["id"] = realId;
                dd.Remove("seq"); // internal-only field, keep the wire format standard
            }
        }
    }

    private void RememberSeq(string chatType, string peerUid, string seq, string msgId)
    {
        if (seq.Length == 0 || seq == "0" || msgId.Length == 0) return;
        var key = $"{chatType}|{peerUid}|{seq}";
        if (_seqIndex.TryAdd(key, msgId)) _seqIndexOrder.Enqueue(key);
        TrimSeqIndex();
    }

    private void IndexMedia(JsonElement rec, string msgId, long chatType, string peerUid)
    {
        if (!rec.TryGetProperty("elements", out var els) || els.ValueKind != JsonValueKind.Array) return;
        foreach (var m in Ob11Mapper.CollectMedia(els, msgId, (int)chatType, peerUid))
        {
            _mediaIndex[m.Key] = m;
            _mediaIndexOrder.Enqueue(m.Key); // duplicates OK: trim dedups via TryRemove
            TrimMediaIndex();
            _ = _store.SaveMediaAsync(new Storage.StoredMedia
            {
                Key = m.Key, Kind = m.Kind, ElementId = m.ElementId, MsgId = m.MsgId,
                ChatType = m.ChatType, PeerUid = m.PeerUid,
                LocalPath = m.LocalPath, Url = m.Url, Name = m.Name, Size = m.Size,
            });
        }
    }

    private void TrimMediaIndex()
    {
        if (_mediaIndex.Count <= 3000) return;
        // FIFO: drop the oldest inserted media references first
        while (_mediaIndex.Count > 3000 && _mediaIndexOrder.TryDequeue(out var k))
            _mediaIndex.TryRemove(k, out _);
    }

    private void RememberMsgId(string msgId, string idx)
    {
        if (msgId.Length == 0) return;
        _msgIndex[msgId] = idx;
        _msgIndexOrder.Enqueue(msgId);
        TrimIndex();
    }

    /// <summary>媒体索引回查：内存 L1 miss → SQLite L2（命中回填 L1）。</summary>
    private async Task<Ob11Mapper.MediaRef?> FindMediaAsync(string key)
    {
        var m = await _store.GetMediaAsync(key.ToLowerInvariant());
        if (m is null) return null;
        var r = new Ob11Mapper.MediaRef(m.Kind, m.Key, m.ElementId, m.MsgId, m.ChatType, m.PeerUid,
            m.LocalPath, m.Url, m.Name, m.Size);
        _mediaIndex[r.Key] = r;
        _mediaIndexOrder.Enqueue(r.Key);
        return r;
    }

    /// <summary>Record-level indexing: memory L1 + SQLite L2 (fire-and-forget persistence).</summary>
    private void RememberMessage(JsonElement rec, string msgId, long chatType, string peerUid, bool isSelfSent)
    {
        if (msgId.Length == 0) return;
        var seq = Ob11.Str(rec, "msgSeq") ?? "0";
        RememberMsgId(msgId, $"{chatType}|{peerUid}|{seq}");
        RememberSeq(chatType.ToString(), peerUid, seq, msgId);
        _ = _store.SaveMessageAsync(new Storage.StoredMessage
        {
            MessageId = msgId,
            ChatType = chatType,
            PeerUid = peerUid,
            Seq = seq,
            SenderUid = Ob11.Str(rec, "senderUid") ?? "",
            SenderUin = Ob11.Str(rec, "senderUin") ?? "",
            MsgTime = Ob11.Long(rec, "msgTime"),
            IsSelfSent = isSelfSent,
        });
    }

    /// <summary>msgId → "chatType|peerUid|seq": L1 memory first, then the SQLite store.</summary>
    private async Task<string?> FindIndexAsync(string msgId)
    {
        if (_msgIndex.TryGetValue(msgId, out var idx)) return idx;
        var m = await _store.GetMessageAsync(msgId);
        if (m is null) return null;
        idx = $"{m.ChatType}|{m.PeerUid}|{m.Seq}";
        RememberMsgId(msgId, idx);
        return idx;
    }

    /// <summary>Index merged-forward containers: real msgId ↔ resId (forward 段 id), memory + store.</summary>
    private void IndexForwardSegments(List<Dictionary<string, object?>> segments, string msgId, long chatType, string peerUid)
    {
        foreach (var s in segments)
        {
            if (!s.TryGetValue("type", out var t) || t?.ToString() != "forward") continue;
            if (!s.TryGetValue("data", out var d) || d is not Dictionary<string, object?> dd) continue;
            var resId = dd.TryGetValue("id", out var i) ? i?.ToString() ?? "" : "";
            if (resId.Length == 0 || resId == msgId) continue;
            RememberMsgId(resId, $"{chatType}|{peerUid}|0");
            _ = _store.SaveForwardAsync(new Storage.StoredForward
            {
                MessageId = msgId, ResId = resId, ChatType = chatType, PeerUid = peerUid,
            });
        }
    }

    /// <summary>Wait briefly for the listUpdate to teach us the real msgId behind a send marker.</summary>
    private async Task<string?> WaitForRealMsgIdAsync(string marker)
    {
        if (_markerToRealId.TryGetValue(marker, out var rid)) return rid;
        var stored = await _store.GetMarkerAsync(marker);
        if (stored is not null) return stored;
        for (int i = 0; i < 20; i++) // ~3s cap; the kernel normally reports within a few hundred ms
        {
            await Task.Delay(150);
            if (_markerToRealId.TryGetValue(marker, out rid)) return rid;
        }
        return await _store.GetMarkerAsync(marker) ?? marker;
    }

    // ---------- member cache (role/level/card) ----------

    private readonly ConcurrentDictionary<string, (string Role, string Level, string Card, long Tick)> _memberCache = new();
    private readonly HashSet<string> _memberListFetched = new();
    private readonly object _memberFetchLock = new();

    private void LearnMember(string gid, string? uin, string? role, string? level, string? card)
    {
        if (string.IsNullOrEmpty(uin)) return;
        _memberCache[$"{gid}|{uin}"] = (RoleToString(role), level ?? "", card ?? "",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        TrimMemberCache();
    }

    private void TrimMemberCache()
    {
        if (_memberCache.Count <= 8000) return;
        var cutoff = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 3600_000;
        foreach (var kv in _memberCache)
            if (kv.Value.Tick < cutoff) _memberCache.TryRemove(kv.Key, out _);
    }

    /// <summary>One-shot per-group member list fetch (NapCat-style preheat) to fill role/card/level.</summary>
    private async Task EnsureMemberListAsync(string gid)
    {
        lock (_memberFetchLock)
        {
            if (!_memberListFetched.Add(gid)) return;
        }
        try
        {
            await DispatchAsync("get_group_member_list", new Dictionary<string, object?> { ["group_id"] = gid });
        }
        catch (Exception ex) { _logger.LogDebug(ex, "member list preheat failed for {Gid}", gid); }
    }

    private async Task HandleMsgListUpdateAsync(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0) return;
        if (root[0].ValueKind != JsonValueKind.Array) return;

        foreach (var rec in root[0].EnumerateArray())
        {
            if (rec.ValueKind != JsonValueKind.Object) continue;
            var chatType = Ob11.Long(rec, "chatType");
            if (chatType != 1 && chatType != 2) continue;

            // recall notices
            var recallTime = Ob11.Str(rec, "recallTime") ?? "0";
            if (recallTime != "0")
            {
                var recallMsgId = Ob11.Str(rec, "msgId") ?? "";
                var nowTick = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var peerKey = $"{chatType}|{Ob11.Str(rec, "peerUid") ?? ""}";
                if (_recallEmitted.TryGetValue(recallMsgId, out var lastTick) && nowTick - lastTick < 30000) continue;
                // the grayTip path emits for the same recall event milliseconds apart
                if (_recentRecallPeer.TryGetValue(peerKey, out var peerTick) && nowTick - peerTick < 3000) continue;
                _recallEmitted[recallMsgId] = nowTick;
                _recentRecallPeer[peerKey] = nowTick;
                TrimRecallDedup();
                var isGroup = chatType == 2;
                var senderUid = Ob11.Str(rec, "senderUid") ?? "";
                var senderUin = Ob11.Str(rec, "senderUin") ?? "";
                LearnUin(senderUid, senderUin);
                if (string.IsNullOrEmpty(senderUin)) senderUin = await ResolveUinCachedAsync(senderUid);
                // operator: prefer the grayTip-learned operator for this peer; when the
                // sender revoked their own message the operator is the sender; otherwise
                // (unknown admin revoke) fall back to self
                string operatorUin;
                if (_recallOperator.TryGetValue(peerKey, out var op) && nowTick - op.Tick < 15000 &&
                    !string.IsNullOrEmpty(op.OperatorUin))
                    operatorUin = op.OperatorUin;
                else if (!string.IsNullOrEmpty(senderUid) && senderUid == SelfUid)
                    operatorUin = senderUin;
                else
                    operatorUin = SelfUin;
                await BroadcastAsync(new Dictionary<string, object?>
                {
                    ["time"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    ["self_id"] = SelfUin,
                    ["post_type"] = "notice",
                    ["notice_type"] = isGroup ? "group_recall" : "friend_recall",
                    ["group_id"] = isGroup ? (Ob11.Str(rec, "peerUid") ?? "") : null,
                    ["user_id"] = senderUin,
                    ["operator_id"] = operatorUin,
                    ["message_id"] = Ob11.Str(rec, "msgId") ?? "",
                });
                continue;
            }

            // self-sent confirmation -> message_sent event
            var sendStatus = Ob11.Long(rec, "sendStatus");
            var sendType = Ob11.Long(rec, "sendType");
            _logger.LogInformation("listUpdate rec: sendType={T} sendStatus={S} chatType={C}", sendType, sendStatus, Ob11.Long(rec, "chatType"));
            // sendType: 1=text send, 5=merged forward
            if (sendType is 1 or 5 && sendStatus is 2 or 3)
            {
                var msgId = Ob11.Str(rec, "msgId") ?? "";
                var peerUid = Ob11.Str(rec, "peerUid") ?? "";
                RememberMessage(rec, msgId, chatType, peerUid, isSelfSent: true);
                // guildId on self-sent records is our generated send marker -> map to real id
                var marker = Ob11.Str(rec, "guildId") ?? "";
                if (!string.IsNullOrEmpty(marker) && marker.StartsWith("7") && marker != msgId)
                {
                    _markerToRealId[marker] = msgId;
                    _markerOrder.Enqueue(marker);
                    TrimMarkerIndex();
                    _ = _store.SaveMarkerAsync(marker, msgId);
                }

                var isGroup = chatType == 2;
                var senderUid = Ob11.Str(rec, "senderUid") ?? "";
                var senderUin = Ob11.Str(rec, "senderUin") ?? "";
                if (string.IsNullOrEmpty(senderUin)) senderUin = LookupUin(senderUid);
                if (string.IsNullOrEmpty(senderUin)) senderUin = SelfUin;

                var segments = await Ob11Mapper.ElementsToSegmentsAsync(
                    rec.TryGetProperty("elements", out var els) ? els : default,
                    ResolveUinForMapperAsync);
                if (segments.Count == 0) continue;

                IndexMedia(rec, msgId, chatType, peerUid);
                IndexReplyTargets(segments, chatType, peerUid);
                IndexForwardSegments(segments, msgId, chatType, peerUid);

                var selfRole = "member";
                var selfLevel = "";
                var selfCard = "";
                if (isGroup && _memberCache.TryGetValue($"{peerUid}|{senderUin}", out var smc))
                {
                    selfRole = smc.Role; selfLevel = smc.Level; selfCard = smc.Card;
                }

                var evt = new Dictionary<string, object?>
                {
                    ["time"] = Ob11.Long(rec, "msgTime"),
                    ["self_id"] = SelfUin,
                    ["post_type"] = "message_sent",
                    ["message_type"] = isGroup ? "group" : "private",
                    ["sub_type"] = "normal",
                    ["message_id"] = msgId,
                    ["user_id"] = SelfUin,
                    ["message"] = segments,
                    ["raw_message"] = Ob11Mapper.BuildRawMessage(segments),
                    ["font"] = 0,
                    ["sender"] = new Dictionary<string, object?>
                    {
                        ["user_id"] = SelfUin,
                        ["nickname"] = SelfNick,
                        ["card"] = selfCard,
                        ["sex"] = "unknown",
                        ["age"] = 0,
                        ["role"] = selfRole,
                    },
                };
                if (selfLevel.Length > 0) ((Dictionary<string, object?>)evt["sender"]!)["level"] = selfLevel;
                if (isGroup) evt["group_id"] = peerUid;
                await BroadcastAsync(evt);
            }
        }
    }

    private async Task HandleGrayTipsAsync(JsonElement rec)
    {
        if (!rec.TryGetProperty("elements", out var els) || els.ValueKind != JsonValueKind.Array) return;
        foreach (var el in els.EnumerateArray())
        {
            if (!el.TryGetProperty("grayTipElement", out var gt) || gt.ValueKind != JsonValueKind.Object) continue;
            if (gt.TryGetProperty("subElementType", out var sub) && sub.TryGetInt32(out var subI))
            {
                if (subI == 1) // revoke
                {
                    var isGroup = Ob11.Long(rec, "chatType") == 2;
                    var peerKey = $"{Ob11.Long(rec, "chatType")}|{Ob11.Str(rec, "peerUid") ?? ""}";
                    var nowTick = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    // NT revoke gray tips carry the operator identity — cache it so the
                    // onMsgInfoListUpdate path (real message_id, no operator info) can
                    // emit the correct operator_id for the same recall event
                    var revokeOperatorUin = "";
                    if (gt.TryGetProperty("revokeElement", out var rv) && rv.ValueKind == JsonValueKind.Object)
                    {
                        var operatorUid = Ob11.Str(rv, "operatorUid") ?? "";
                        if (!string.IsNullOrEmpty(operatorUid))
                            revokeOperatorUin = LookupUin(operatorUid);
                        // learn the original sender too when present
                        LearnUin(Ob11.Str(rv, "operatorUid"), revokeOperatorUin);
                    }
                    _recallOperator[peerKey] = (revokeOperatorUin, nowTick);
                    var tipMsgId = Ob11.Str(rec, "msgId") ?? "";
                    if (_recallEmitted.TryGetValue(tipMsgId, out var tipTick) && nowTick - tipTick < 30000) continue;
                    if (_recentRecallPeer.TryGetValue(peerKey, out var peerTick) && nowTick - peerTick < 3000) continue;
                    _recallEmitted[tipMsgId] = nowTick;
                    _recentRecallPeer[peerKey] = nowTick;
                    TrimRecallDedup();
                    var senderUin = Ob11.Str(rec, "senderUin") ?? "";
                    if (string.IsNullOrEmpty(senderUin)) senderUin = await ResolveUinCachedAsync(Ob11.Str(rec, "senderUid") ?? "");
                    var operatorUin = !string.IsNullOrEmpty(revokeOperatorUin)
                        ? revokeOperatorUin
                        : (!string.IsNullOrEmpty(senderUin) ? senderUin : SelfUin);
                    await BroadcastAsync(new Dictionary<string, object?>
                    {
                        ["time"] = Ob11.Long(rec, "msgTime"),
                        ["self_id"] = SelfUin,
                        ["post_type"] = "notice",
                        ["notice_type"] = isGroup ? "group_recall" : "friend_recall",
                        ["group_id"] = isGroup ? (Ob11.Str(rec, "peerUid") ?? "") : null,
                        ["user_id"] = senderUin,
                        ["operator_id"] = operatorUin,
                        ["message_id"] = tipMsgId,
                    });
                }
                else if (subI == 4 && gt.TryGetProperty("groupElement", out var ge))
                {
                    // group member add/quit/kick tips (NT groupElement.type)
                    var tipType = ge.TryGetProperty("type", out var tt) && tt.TryGetInt32(out var tti) ? tti : 0;
                    var memberUid = ge.TryGetProperty("memberUid", out var mu) ? mu.GetString() ?? "" : "";
                    var memberUin = LookupUin(memberUid);
                    if (tipType == 1)
                    {
                        // admin approved / invited a member
                        await BroadcastAsync(new Dictionary<string, object?>
                        {
                            ["time"] = Ob11.Long(rec, "msgTime"),
                            ["self_id"] = SelfUin,
                            ["post_type"] = "notice",
                            ["notice_type"] = "group_increase",
                            ["group_id"] = Ob11.Str(rec, "peerUid") ?? "",
                            ["user_id"] = memberUin,
                            ["operator_id"] = LookupUin(ge.TryGetProperty("adminUid", out var au) ? au.GetString() ?? "" : ""),
                            ["sub_type"] = "approve",
                        });
                    }
                    else if (tipType is 2 or 3)
                    {
                        // 2 = kicked by admin, 3 = left voluntarily
                        var selfLeft = memberUin == SelfUin;
                        await BroadcastAsync(new Dictionary<string, object?>
                        {
                            ["time"] = Ob11.Long(rec, "msgTime"),
                            ["self_id"] = SelfUin,
                            ["post_type"] = "notice",
                            ["notice_type"] = "group_decrease",
                            ["group_id"] = Ob11.Str(rec, "peerUid") ?? "",
                            ["user_id"] = memberUin,
                            ["operator_id"] = tipType == 2
                                ? LookupUin(ge.TryGetProperty("adminUid", out var au2) ? au2.GetString() ?? "" : "")
                                : memberUin,
                            ["sub_type"] = tipType == 2 ? (selfLeft ? "kick_me" : "kick") : "leave",
                        });
                    }
                    else if (tipType == 8 && ge.TryGetProperty("shutUp", out var su))
                    {
                        var admin = su.TryGetProperty("admin", out var ad) ? ad : default;
                        var member = su.TryGetProperty("member", out var me) ? me : default;
                        var duration = Ob11.Long(su, "duration");
                        await BroadcastAsync(new Dictionary<string, object?>
                        {
                            ["time"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                            ["self_id"] = SelfUin,
                            ["post_type"] = "notice",
                            ["notice_type"] = "group_ban",
                            ["sub_type"] = duration > 0 ? "ban" : "lift_ban",
                            ["group_id"] = Ob11.Str(rec, "peerUid") ?? "",
                            ["operator_id"] = LookupUin(admin.TryGetProperty("uid", out var adu) ? adu.GetString() ?? "" : ""),
                            ["user_id"] = LookupUin(member.TryGetProperty("uid", out var meu) ? meu.GetString() ?? "" : ""),
                            ["duration"] = duration,
                        });
                    }
                }
                else if (subI == 17 && gt.TryGetProperty("jsonGrayTipElement", out var jg))
                {
                    var jsonStr = jg.TryGetProperty("jsonStr", out var js) ? js.GetString() ?? "" : "";
                    if (jsonStr.Contains("\"poke\""))
                    {
                        try
                        {
                            using var pj = JsonDocument.Parse(jsonStr);
                            var p = pj.RootElement;
                            var isGroup = Ob11.Long(rec, "chatType") == 2;
                            var target = p.TryGetProperty("target_str", out var ts) ? ts.GetString() ?? "" : "";
                            await BroadcastAsync(new Dictionary<string, object?>
                            {
                                ["time"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                                ["self_id"] = SelfUin,
                                ["post_type"] = "notice",
                                ["notice_type"] = "notify",
                                ["sub_type"] = "poke",
                                ["group_id"] = isGroup ? (Ob11.Str(rec, "peerUid") ?? "") : null,
                                ["user_id"] = LookupUin(p.TryGetProperty("operator_str", out var os) ? os.GetString() ?? "" : ""),
                                ["target_id"] = LookupUin(target),
                            });
                        }
                        catch { }
                    }
                }
            }
        }
    }

    private async Task HandleBuddyReqAsync(JsonElement root)
    {
        // payload: [ ret ] where ret.reqList holds friend requests
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0) return;
        foreach (var el in root.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty("reqList", out var reqs)) continue;
            if (reqs.ValueKind != JsonValueKind.Array) continue;
            foreach (var req in reqs.EnumerateArray())
            {
                var flagUid = Ob11.Str(req, "uid") ?? "";
                var friendUid = Ob11.Str(req, "friendUid") ?? flagUid;
                await BroadcastAsync(new Dictionary<string, object?>
                {
                    ["time"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    ["self_id"] = SelfUin,
                    ["post_type"] = "request",
                    ["request_type"] = "friend",
                    ["user_id"] = LookupUin(friendUid),
                    ["comment"] = Ob11.Str(req, "msg") ?? "",
                    ["flag"] = friendUid + "|" + (Ob11.Str(req, "reqTime") ?? "0"),
                });
            }
        }
    }

    // ================= #AaR in-chat debug command =================

    private const string DebugCommandText = "#AaR";
    private static readonly TimeSpan DebugCommandCooldown = TimeSpan.FromSeconds(5);

    private void TrimDebugCmdPeers(long nowMs)
    {
        if (_debugCmdPeerTick.Count <= 500) return;
        foreach (var kv in _debugCmdPeerTick)
            if (nowMs - kv.Value > 60000) _debugCmdPeerTick.TryRemove(kv.Key, out _);
    }

    /// <summary>Plain text content of a segment list (text segments concatenated).</summary>
    private static string PlainTextOf(List<Dictionary<string, object?>> segments)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var s in segments)
            if (s.TryGetValue("type", out var t) && t?.ToString() == "text" &&
                s.TryGetValue("data", out var d) && d is Dictionary<string, object?> dd &&
                dd.TryGetValue("text", out var tx))
                sb.Append(tx);
        return sb.ToString();
    }

    /// <summary>Keep only the last 4 characters of an id (chat-safe masking).</summary>
    private static string MaskId(string? id)
    {
        if (string.IsNullOrEmpty(id)) return "(未知)";
        return id.Length <= 4 ? "****" : "****" + id[^4..];
    }

    private static string FormatUptime(TimeSpan t) =>
        $"{(int)t.TotalDays}d{t.Hours}h{t.Minutes}m{t.Seconds}s";

    /// <summary>
    /// Handles the "#AaR" chat command: full diagnostics go to the server log,
    /// a sanitized one-screen summary is replied directly into the same chat.
    /// Never throws — failures are logged and swallowed.
    /// </summary>
    private async Task TryHandleDebugCommandAsync(
        long chatType, string peerUid, string senderUid, string senderUin, string msgId,
        List<Dictionary<string, object?>> segments)
    {
        try
        {
            var text = PlainTextOf(segments).Trim();
            if (!string.Equals(text, DebugCommandText, StringComparison.Ordinal)) return;

            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var peerKey = $"{chatType}|{peerUid}";
            if (_debugCmdPeerTick.TryGetValue(peerKey, out var last) && nowMs - last < DebugCommandCooldown.TotalMilliseconds)
                return;
            _debugCmdPeerTick[peerKey] = nowMs;
            TrimDebugCmdPeers(nowMs);

            // full, unmasked diagnostics -> server log (operator eyes only)
            var fullInfo = await BuildFullDebugInfoAsync();
            _logger.LogInformation(
                "#AaR debug command in {ChatType}: peer={PeerUid} senderUid={SenderUid} senderUin={SenderUin} msgId={MsgId}{NewLine}{FullInfo}",
                chatType == 2 ? "group" : "private",
                peerUid, senderUid, senderUin, msgId,
                Environment.NewLine, fullInfo);

            // sanitized summary -> chat reply via the kernel
            var reply = BuildSanitizedDebugInfo(chatType);
            var elements = new List<Dictionary<string, object?>> { Ob11Mapper.TextEl(reply) };
            await CallKernel("msg.send", new { chatType, peerUid, elements });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "#AaR debug reply failed for peer {Peer}", peerUid);
        }
    }

    /// <summary>QQ NT version from package.json (e.g. 3.2.35-54395); cached, "" when unreadable.</summary>
    private volatile string _qqVersion = "";
    private string ResolveQQVersion()
    {
        if (_qqVersion.Length > 0) return _qqVersion;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(_opts.QQVersionJsonPath));
            _qqVersion = doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";
        }
        catch { }
        return _qqVersion;
    }

    private static string PlatformString()
    {
        var arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "X86-64",
            System.Runtime.InteropServices.Architecture.X86 => "X86",
            System.Runtime.InteropServices.Architecture.Arm64 => "ARM64",
            System.Runtime.InteropServices.Architecture.Arm => "ARM",
            _ => System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
        };
        return "Linux " + arch;
    }

    /// <summary>Uptime as 3小时12分钟 (or 12分钟 under one hour).</summary>
    private static string UptimeHoursMinutes(TimeSpan t) =>
        (int)t.TotalHours > 0 ? $"{(int)t.TotalHours}小时{t.Minutes}分钟" : $"{t.Minutes}分钟";

    /// <summary>
    /// Sanitized banner for the chat reply (no uins/uids/urls/tokens). Fixed
    /// six-line format; full diagnostics go to the server log only.
    /// </summary>
    private string BuildSanitizedDebugInfo(long chatType)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"项目代号\"{_opts.ProjectCodename}\"，{_opts.DeploymentLabel}");
        sb.AppendLine($"版本{ServerVersion}");
        sb.AppendLine($"平台{PlatformString()}");
        sb.AppendLine($"运行时间{UptimeHoursMinutes(DateTime.UtcNow - _startedAt)}");
        sb.AppendLine($".NET{Environment.Version}/QQ {ResolveQQVersion()}");
        sb.Append("SignalR-JSRPC模式");
        return sb.ToString();
    }

    /// <summary>Full diagnostics for the server log.</summary>
    private async Task<string> BuildFullDebugInfoAsync()
    {
        var sb = new System.Text.StringBuilder();
        string ReverseTokenCount<T>(List<T>? list, Func<T, string?> token) =>
            list is null ? "-" : $"{list.Count(x => !string.IsNullOrEmpty(token(x)))}/{list.Count}";
        try
        {
            using var proc = System.Diagnostics.Process.GetCurrentProcess();
            sb.AppendLine("==== AaR debug info (full) ====");
            sb.AppendLine($"version     : AaR {ServerVersion}");
            sb.AppendLine($"runtime     : {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
            sb.AppendLine($"os          : {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
            sb.AppendLine($"uptime      : session {FormatUptime(DateTime.UtcNow - _startedAt)} / process {FormatUptime(DateTime.Now - proc.StartTime)}");
            sb.AppendLine($"memory      : working set {proc.WorkingSet64 / 1024 / 1024} MB, GC heap {GC.GetTotalMemory(false) / 1024 / 1024} MB, threads {proc.Threads.Count}");
            sb.AppendLine($"self        : uin={SelfUin} uid={SelfUid} nick={SelfNick}");
            sb.AppendLine($"bridge      : {(_bridge.IsBridgeConnected ? "connected" : "NOT connected")}");
            sb.AppendLine($"auth        : token {(string.IsNullOrEmpty(_opts.AccessToken) ? "NOT set (auth disabled)" : $"set ({_opts.AccessToken.Length} chars)")}");
            sb.AppendLine($"endpoints   : hub={_opts.BridgeHubPath} ws={_opts.Ob11WsPath}");
            sb.AppendLine($"flags       : RequireBridgeForHttp={_opts.RequireBridgeForHttp} EnableDebugKernel={_opts.EnableDebugKernel} EnableDebugCommand={_opts.EnableDebugCommand}");
            sb.AppendLine($"reverse-ws  : targets with token {ReverseTokenCount(_opts.Reverse?.WebSockets, t => t.AccessToken)}");
            foreach (var t in _opts.Reverse?.WebSockets ?? [])
                sb.AppendLine($"  ws  -> {t.Url} (reconnect {t.ReconnectMs}ms, token {(string.IsNullOrEmpty(t.AccessToken) ? "no" : "yes")})");
            sb.AppendLine($"reverse-http: targets with token {ReverseTokenCount(_opts.Reverse?.HttpPost, t => t.AccessToken)}");
            foreach (var t in _opts.Reverse?.HttpPost ?? [])
                sb.AppendLine($"  http -> {t.Url} (token {(string.IsNullOrEmpty(t.AccessToken) ? "no" : "yes")})");
            sb.AppendLine($"events      : received={EventsReceived} broadcast={EventsBroadcast}");
            sb.AppendLine($"ob11-ws     : clients={_wsClients.Count}");
            sb.AppendLine($"indexes     : msg={_msgIndex.Count}/5000 seq={_seqIndex.Count}/5000 media={_mediaIndex.Count}/3000 uidCache={_uidToUin.Count} markers={_markerToRealId.Count}/2000");
            var (sm, sd, su) = await _store.StatsAsync();
            sb.AppendLine($"storage     : provider={_opts.Storage.Provider} messages={sm} media={sd} uidmap={su}");
            sb.AppendLine($"debug-cmd   : cooldown peers tracked={_debugCmdPeerTick.Count}");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"(full info generation error: {ex.Message})");
        }
        return sb.ToString().TrimEnd();
    }

    // ================= OB11 action dispatch =================

    private async Task<JsonElement> SendForwardAsync(JsonElement j, int chatType, string peerUid)
    {
        var msgIds = new List<string>();
        if (Ob11.Get(j, "messages").ValueKind == JsonValueKind.Array)
        {
            foreach (var n in Ob11.Get(j, "messages").EnumerateArray())
            {
                var nodeType = n.TryGetProperty("type", out var nt) ? nt.GetString() : null;
                var data = n.TryGetProperty("data", out var dd) ? dd : (JsonElement)default;
                if (nodeType != "node")
                    throw new Ob11UnsupportedException("only node segments are accepted here");
                string? S2(string k) => data.ValueKind == JsonValueKind.Object && data.TryGetProperty(k, out var dv) ? dv.ToString() : null;
                var id = S2("id");
                if (string.IsNullOrEmpty(id))
                    throw new Ob11UnsupportedException("custom-content forward nodes are not supported (reference existing message ids)");
                if (await FindIndexAsync(id) is not { } nidx)
                    throw new Ob11BadException("forward node references unknown message_id: " + id);
                var np = nidx.Split('|');
                if (int.Parse(np[0]) != chatType || np[1] != peerUid)
                    throw new Ob11BadException("forward nodes must belong to the target chat");
                msgIds.Add(id);
            }
        }
        if (msgIds.Count == 0) throw new Ob11ParamException("messages");

        var r = await CallKernel("msg.sendForward", new { chatType, peerUid, peerUin = peerUid, msgIds });
        var outMsgId = r.TryGetProperty("msgId", out var ri) ? ri.GetString() : null;
        var outRes = r.TryGetProperty("resId", out var re) ? re.GetString() : null;
        return ToElement(new Dictionary<string, object?> { ["res_id"] = outRes, ["message_id"] = outMsgId });
    }

    private async Task<JsonElement> BuildHistoryResponse(JsonElement r, string peerId)
    {
        var items = new List<object?>();
        if (r.TryGetProperty("msgList", out var ml) && ml.ValueKind == JsonValueKind.Array)
        {
            foreach (var rec in ml.EnumerateArray())
            {
                var segs = await Ob11Mapper.ElementsToSegmentsAsync(
                    rec.TryGetProperty("elements", out var els) ? els : default,
                    ResolveUinForMapperAsync);
                var mid = Ob11.Str(rec, "msgId") ?? "";
                var senderUin = Ob11.Str(rec, "senderUin") ?? await ResolveUinCachedAsync(Ob11.Str(rec, "senderUid") ?? "");
                LearnUin(Ob11.Str(rec, "senderUid"), senderUin);
                var chatType = Ob11.Long(rec, "chatType");
                RememberMessage(rec, mid, chatType, Ob11.Str(rec, "peerUid") ?? peerId, isSelfSent: senderUin == SelfUin);
                IndexMedia(rec, mid, chatType, Ob11.Str(rec, "peerUid") ?? peerId);
                IndexReplyTargets(segs, chatType, Ob11.Str(rec, "peerUid") ?? peerId);
                var item = new Dictionary<string, object?>
                {
                    ["time"] = Ob11.Long(rec, "msgTime"),
                    ["self_id"] = SelfUin,
                    ["post_type"] = chatType == 2 ? "group" : "private",
                    ["message_id"] = mid,
                    ["user_id"] = senderUin,
                    ["message"] = segs,
                    ["raw_message"] = Ob11Mapper.BuildRawMessage(segs),
                };
                if (chatType == 2) item["group_id"] = Ob11.Str(rec, "peerUid") ?? peerId;
                items.Add(item);
            }
        }
        // kernel returns the window in chronological order already
        return ToElement(items.ToArray());
    }

    private string LookupUin(string uid)
    {
        if (string.IsNullOrEmpty(uid)) return "";
        if (uid.StartsWith('u'))
        {
            // server-side cache first (learned from records / member lists / priming),
            // then the prime-time snapshot func, so chat participants resolve even
            // before any friend/group list was fetched
            if (_uidToUin.TryGetValue(uid, out var v) && !string.IsNullOrEmpty(v)) return v;
            if (_primedUinLookup is not null && _primedUinLookup(uid) is { } primed && primed.Length > 0)
            {
                _uidToUin[uid] = primed;
                return primed;
            }
            return "";
        }
        return uid; // already numeric
    }

    private Func<string, string?>? _primedUinLookup;

    /// <summary>uid -> uin for the OB11 mapper: cache first, then a bounded kernel RPC.
    /// Failures return null (empty qq field) instead of aborting the whole mapping.</summary>
    private async Task<string?> ResolveUinForMapperAsync(string uid)
    {
        if (string.IsNullOrEmpty(uid)) return "";
        if (!uid.StartsWith('u')) return uid;
        if (_uidToUin.TryGetValue(uid, out var v) && !string.IsNullOrEmpty(v)) return v;
        string uin;
        try { uin = await ResolveUinAsync(uid); } // kernel uid.toUin (bridge caches mappings too)
        catch { return null; }
        LearnUin(uid, uin);
        return string.IsNullOrEmpty(uin) ? null : uin;
    }

    /// <summary>Cache-backed async resolve used by event handlers: memory → store → kernel.</summary>
    private async Task<string> ResolveUinCachedAsync(string uid)
    {
        if (string.IsNullOrEmpty(uid)) return "";
        if (!uid.StartsWith('u')) return uid;
        if (_uidToUin.TryGetValue(uid, out var v) && !string.IsNullOrEmpty(v)) return v;
        var stored = await _store.GetUinAsync(uid);
        if (!string.IsNullOrEmpty(stored)) { _uidToUin[uid] = stored; return stored; }
        try { return await ResolveUinAsync(uid); }
        catch { return ""; }
    }

    /// <summary>Learn a uid->uin pair from any record or list entry (memory + persistent store).</summary>
    private void LearnUin(string? uid, string? uin)
    {
        if (string.IsNullOrEmpty(uid) || !uid.StartsWith('u')) return;
        if (string.IsNullOrEmpty(uin) || !uin.All(char.IsDigit)) return;
        var changed = !_uidToUin.TryGetValue(uid, out var prev) || prev != uin;
        _uidToUin[uid] = uin;
        if (changed) _ = _store.LearnUinAsync(uid, uin);
    }

    private void TrimIndex()
    {
        if (_msgIndex.Count <= 5000) return;
        while (_msgIndex.Count > 5000 && _msgIndexOrder.TryDequeue(out var k))
            _msgIndex.TryRemove(k, out _);
    }

    private void TrimSeqIndex()
    {
        if (_seqIndex.Count <= 5000) return;
        while (_seqIndex.Count > 5000 && _seqIndexOrder.TryDequeue(out var k))
            _seqIndex.TryRemove(k, out _);
    }

    private void TrimMarkerIndex()
    {
        if (_markerToRealId.Count <= 2000) return;
        while (_markerToRealId.Count > 2000 && _markerOrder.TryDequeue(out var k))
            _markerToRealId.TryRemove(k, out _);
    }

    private void TrimRecallDedup()
    {
        if (_recallEmitted.Count <= 2000) return;
        var cutoff = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 60000;
        foreach (var kv in _recallEmitted)
            if (kv.Value < cutoff) _recallEmitted.TryRemove(kv.Key, out _);
    }

    public async Task<Ob11Response> HandleActionAsync(string action, Dictionary<string, object?> p, string? echo)
        => await HandleActionAsync(action, p, echo, echoRaw: null);

    public async Task<Ob11Response> HandleActionAsync(string action, Dictionary<string, object?> p, string? echo, JsonElement? echoRaw)
    {
        // 标准：所有 API 都支持 _async / _rate_limited 后缀——后台执行，立即返回 async
        if (action.EndsWith("_async", StringComparison.Ordinal) || action.EndsWith("_rate_limited", StringComparison.Ordinal))
        {
            var stripped = action.EndsWith("_async", StringComparison.Ordinal) ? action[..^6] : action[..^13];
            _ = Task.Run(async () =>
            {
                try { await DispatchAsync(stripped, p); }
                catch (Exception ex) { _logger.LogWarning(ex, "async action {Action} failed", stripped); }
            });
            return Ob11Response.Async(echo, echoRaw);
        }
        try
        {
            var data = await DispatchAsync(action, p);
            return Ob11Response.Ok(data, echo, echoRaw);
        }
        catch (Ob11AsyncSignal)
        {
            return Ob11Response.Async(echo, echoRaw);
        }
        catch (Ob11UnknownActionException ex)
        {
            return Ob11Response.Fail(1404, ex.Message, echo: echo, echoRaw: echoRaw);
        }
        catch (Ob11UnsupportedException ex)
        {
            return Ob11Response.Fail(Ob11.RetUnsupported, ex.Message, echo: echo, echoRaw: echoRaw);
        }
        catch (Ob11ParamException ex)
        {
            return Ob11Response.Fail(Ob11.RetParamMissing, ex.Message, echo: echo, echoRaw: echoRaw);
        }
        catch (Ob11EmptyException ex)
        {
            return Ob11Response.Fail(Ob11.RetParamEmpty, ex.Message, echo: echo, echoRaw: echoRaw);
        }
        catch (Ob11BadException ex)
        {
            return Ob11Response.Fail(Ob11.RetParamBad, ex.Message, echo: echo, echoRaw: echoRaw);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "action {Action} failed", action);
            return Ob11Response.Fail(-1, ex.Message, echo: echo, echoRaw: echoRaw);
        }
    }

    private static JsonElement ToElement(object o)
    {
        var json = JsonSerializer.Serialize(o);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private async Task<JsonElement> CallKernel(string method, object? rpcParams, int? timeoutMs = null)
    {
        var (ok, dataOpt, err) = await _bridge.CallJsonAsync(_hub, method, rpcParams, timeoutMs);
        if (!ok) throw new Ob11BadException($"kernel call {method} failed: {err}");
        return dataOpt ?? JsonSerializer.SerializeToElement(new object());
    }

    private async Task<Dictionary<string, object?>> LoadSelfAsync()
    {
        if (string.IsNullOrEmpty(SelfUin))
            await RefreshSelfAsync();
        return new Dictionary<string, object?> { ["user_id"] = SelfUin, ["nickname"] = SelfNick };
    }

    private async Task<string> ResolveUidAsync(string uin)
    {
        var r = await CallKernel("uin.toUid", new { uin });
        var uid = Ob11.Str(r, "uid");
        if (string.IsNullOrEmpty(uid)) throw new Ob11BadException("cannot resolve uid for uin " + uin);
        return uid;
    }

    private async Task<string> ResolveUinAsync(string uid)
    {
        var r = await CallKernel("uid.toUin", new { uid });
        return Ob11.Str(r, "uin") ?? "";
    }

    private (int ChatType, string PeerUid) ParsePeer(JsonElement p, bool requireUser, bool requireGroup)
    {
        if (requireUser)
        {
            var userId = Ob11.Str(p, "user_id");
            if (string.IsNullOrEmpty(userId)) throw new Ob11ParamException("user_id");
            return (1, userId);
        }
        if (requireGroup)
        {
            var groupId = Ob11.Str(p, "group_id");
            if (string.IsNullOrEmpty(groupId)) throw new Ob11ParamException("group_id");
            return (2, groupId);
        }
        var mt = Ob11.Str(p, "message_type");
        if (mt == "group")
        {
            var gid = Ob11.Str(p, "group_id") ?? throw new Ob11ParamException("group_id");
            return (2, gid);
        }
        // OneBot v11: send_msg infers the chat from the params — group_id present
        // without message_type means a group message
        if (mt is null && Ob11.Str(p, "group_id") is { } gid2)
            return (2, gid2);
        var uid2 = Ob11.Str(p, "user_id") ?? throw new Ob11ParamException("user_id");
        return (1, uid2);
    }

    private async Task<JsonElement> DispatchAsync(string action, Dictionary<string, object?> p)
    {
        var j = ToElement(p);
        switch (action)
        {
            case "get_login_info":
                return ToElement(await LoadSelfAsync());

            case "send_private_msg":
            case "send_group_msg":
            case "send_msg":
                {
                    var (chatType, peerUid) = action switch
                    {
                        "send_private_msg" => ParsePeer(j, requireUser: true, requireGroup: false),
                        "send_group_msg" => ParsePeer(j, requireUser: false, requireGroup: true),
                        _ => ParsePeer(j, requireUser: false, requireGroup: false),
                    };
                    // for private: uin -> uid
                    var uid = peerUid;
                    if (chatType == 1 && !peerUid.StartsWith('u'))
                        uid = await ResolveUidAsync(peerUid);
                    var message = Ob11.Get(j, "message");
                    // reply 引用持久化回查：内存 L1 miss 的 reply id 先查 SQLite（重启后回复链不丢）
                    foreach (var rid in Ob11Mapper.CollectReplyIds(message).Where(id => !_msgIndex.ContainsKey(id)))
                    {
                        var sm = await _store.GetMessageAsync(rid);
                        if (sm is not null) RememberMsgId(rid, $"{sm.ChatType}|{sm.PeerUid}|{sm.Seq}");
                    }
                    var elements = await Ob11Mapper.SegmentsToElementsAsync(
                        message, _bridge, _hub, _msgIndex, chatType, uid,
                        async uin => (await CallKernel("uin.toUid", new { uin })).TryGetProperty("uid", out var u) ? u.GetString() : null);
                    var autoEscape = Ob11.Bool(j, "auto_escape");
                    if (autoEscape)
                        elements = new List<Dictionary<string, object?>> { Ob11Mapper.TextEl(BuildRawText(j)) };
                    // 群文件上传走 highway 云通道，耗时可达分钟级——file 元素单独放宽 RPC 超时
                    var hasFile = elements.Any(e => e.TryGetValue("elementType", out var et) && et is int i3 && i3 == 3);
                    var ret = await CallKernel("msg.send", new { chatType, peerUid = uid, elements }, hasFile ? 180_000 : null);
                    var delivered = ret.TryGetProperty("delivered", out var dv) && dv.ValueKind == JsonValueKind.True;
                    // the generated uniqueId marker differs from the real msgId QQ assigns
                    // (real = marker + 1 in practice); we tracked the real id via the
                    // onMsgInfoListUpdate record — look it up by marker in the msg index.
                    var marker = ret.TryGetProperty("msgId", out var mi) ? mi.GetString() : null;
                    // the generated uniqueId marker differs from the real msgId QQ assigns —
                    // wait briefly for onMsgInfoListUpdate (memory) or the store to teach us
                    var realMsgId = marker is not null ? await WaitForRealMsgIdAsync(marker) : null;
                    return ToElement(new Dictionary<string, object?>
                    {
                        ["message_id"] = realMsgId ?? marker ?? "",
                        ["res_id"] = "",
                        ["delivered"] = delivered,
                        // kernel-side verdict for diagnostics (result 0 = ok)
                        ["result"] = ret.TryGetProperty("result", out var rv) ? rv : null,
                        ["errMsg"] = ret.TryGetProperty("errMsg", out var ev) ? ev.GetString() : null,
                    });
                }

            case "delete_msg":
                {
                    var msgId = Ob11.Str(j, "message_id") ?? throw new Ob11ParamException("message_id");
                    var idx = await FindIndexAsync(msgId)
                        ?? throw new Ob11BadException("unknown message_id (not seen in this session or store): " + msgId);
                    var parts = idx.Split('|');
                    var ret = await CallKernel("msg.recall", new
                    {
                        chatType = long.Parse(parts[0]),
                        peerUid = parts[1],
                        msgId,
                    });
                    return ToElement(new object());
                }

            case "get_msg":
                {
                    var msgId = Ob11.Str(j, "message_id") ?? throw new Ob11ParamException("message_id");
                    var idx = await FindIndexAsync(msgId)
                        ?? throw new Ob11BadException("unknown message_id: " + msgId);
                    var parts = idx.Split('|');
                    var ret = await CallKernel("msg.getByMsgIds", new
                    {
                        chatType = long.Parse(parts[0]),
                        peerUid = parts[1],
                        msgIds = new[] { msgId },
                    });
                    if (ret.TryGetProperty("msgList", out var list) && list.ValueKind == JsonValueKind.Array &&
                        list.GetArrayLength() > 0)
                    {
                        var rec = list[0];
                        var isGroup = Ob11.Long(rec, "chatType") == 2;
                        var segments = await Ob11Mapper.ElementsToSegmentsAsync(
                            rec.TryGetProperty("elements", out var els) ? els : default,
                            ResolveUinForMapperAsync);
                        IndexMedia(rec, msgId, Ob11.Long(rec, "chatType"), parts[1]);
                        IndexReplyTargets(segments, Ob11.Long(rec, "chatType"), parts[1]);
                        var senderUin = Ob11.Str(rec, "senderUin") ?? await ResolveUinCachedAsync(Ob11.Str(rec, "senderUid") ?? "");
                        LearnUin(Ob11.Str(rec, "senderUid"), senderUin);
                        RememberMessage(rec, msgId, Ob11.Long(rec, "chatType"), parts[1], isSelfSent: senderUin == SelfUin);
                        var senderCard = "";
                        if (_memberCache.TryGetValue($"{parts[1]}|{senderUin}", out var gmc)) senderCard = gmc.Card;
                        var data = new Dictionary<string, object?>
                        {
                            ["time"] = Ob11.Long(rec, "msgTime"),
                            ["self_id"] = SelfUin,
                            ["real_id"] = msgId,
                            ["post_type"] = isGroup ? "group" : "private",
                            ["message_type"] = isGroup ? "group" : "private",
                            ["message_id"] = msgId,
                            ["user_id"] = senderUin,
                            ["message"] = segments,
                            ["raw_message"] = Ob11Mapper.BuildRawMessage(segments),
                            ["sender"] = new Dictionary<string, object?>
                            {
                                ["user_id"] = senderUin,
                                ["nickname"] = Ob11.Str(rec, "sendNickName") ?? "",
                                ["card"] = senderCard,
                            },
                        };
                        if (isGroup) data["group_id"] = parts[1];
                        return ToElement(data);
                    }
                    throw new Ob11BadException("message not found: " + msgId);
                }

            case "mark_msg_as_read":
                {
                    var msgId = Ob11.Str(j, "message_id") ?? throw new Ob11ParamException("message_id");
                    var idx = await FindIndexAsync(msgId);
                    if (idx is not null)
                    {
                        var parts = idx.Split('|');
                        await CallKernel("msg.setRead", new { chatType = long.Parse(parts[0]), peerUid = parts[1] });
                    }
                    return ToElement(new object());
                }

            case "get_friend_list":
                {
                    var r = await CallKernel("buddy.list", null);
                    var friends = new List<object?>();
                    if (r.TryGetProperty("friends", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var f in arr.EnumerateArray())
                        {
                            var uin = Ob11.Str(f, "uin") ?? "";
                            friends.Add(new Dictionary<string, object?>
                            {
                                ["user_id"] = uin,
                                ["nickname"] = Ob11.Str(f, "nick") ?? "",
                                ["remark"] = Ob11.Str(f, "remark") ?? "",
                            });
                        }
                    }
                    // feed the uid->uin lookup cache from kernel mappings
                    await PrimeUinLookupAsync();
                    return ToElement(friends.ToArray());
                }

            case "get_stranger_info":
                {
                    var uin = Ob11.Str(j, "user_id") ?? throw new Ob11ParamException("user_id");
                    var uid = await ResolveUidAsync(uin);
                    var r = await CallKernel("user.detail", new { uid });
                    var prof = r.TryGetProperty("profile", out var pr) ? pr : default;
                    return ToElement(new Dictionary<string, object?>
                    {
                        ["user_id"] = uin,
                        ["nickname"] = NickFromProfile(prof),
                        ["sex"] = "unknown",
                        ["age"] = 0,
                    });
                }

            case "get_group_list":
                {
                    await PrimeUinLookupAsync();
                    var r = await CallKernel("group.list", new { forced = Ob11.Bool(j, "no_cache") });
                    var groups = new List<object?>();
                    if (r.TryGetProperty("groups", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var g in arr.EnumerateArray())
                        {
                            var gc = Ob11.Str(g, "groupCode") ?? "";
                            groups.Add(new Dictionary<string, object?>
                            {
                                ["group_id"] = gc,
                                ["group_name"] = Ob11.Str(g, "groupName") ?? "",
                                ["member_count"] = Ob11.Long(g, "memberCount"),
                                ["max_member_count"] = Ob11.Long(g, "maxMember"),
                            });
                        }
                    }
                    return ToElement(groups.ToArray());
                }

            case "get_group_info":
                {
                    var gid = Ob11.Str(j, "group_id") ?? throw new Ob11ParamException("group_id");
                    // prefer the authoritative group detail from the kernel
                    try
                    {
                        var r = await CallKernel("group.info", new { groupCode = gid });
                        var info = r.TryGetProperty("info", out var inf) && inf.ValueKind == JsonValueKind.Object ? inf : default;
                        if (info.ValueKind == JsonValueKind.Object && !string.IsNullOrEmpty(Str3(info, "groupName") ?? Str3(info, "groupCode")))
                        {
                            return ToElement(new Dictionary<string, object?>
                            {
                                ["group_id"] = Str3(info, "groupCode") ?? gid,
                                ["group_name"] = Str3(info, "groupName") ?? "",
                                ["member_count"] = info.TryGetProperty("memberCount", out var mc) && mc.TryGetInt32(out var mci) ? mci : 0,
                                ["max_member_count"] = info.TryGetProperty("maxMember", out var mm) && mm.TryGetInt32(out var mmi) ? mmi : 0,
                            });
                        }
                    }
                    catch { /* fall through to the list scan */ }
                    var list = await DispatchAsync("get_group_list", new Dictionary<string, object?>());
                    foreach (var g in list.EnumerateArray())
                        if (Ob11.Str(g, "group_id") == gid) return g.Clone();
                    return ToElement(new Dictionary<string, object?>
                    {
                        ["group_id"] = gid,
                        ["group_name"] = "",
                        ["member_count"] = 0,
                        ["max_member_count"] = 0,
                    });
                }

            case "get_group_member_list":
                {
                    var gid = Ob11.Str(j, "group_id") ?? throw new Ob11ParamException("group_id");
                    var r = await CallKernel("group.memberList", new { groupCode = gid, forced = Ob11.Bool(j, "no_cache") });
                    var members = new List<object?>();
                    if (r.TryGetProperty("members", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var m in arr.EnumerateArray())
                        {
                            // learn uid->uin pairs + role/card/level so at/reply/sender work offline
                            var memberUid = Ob11.Str(m, "uid");
                            var memberUin = Ob11.Str(m, "uin") ?? "";
                            LearnUin(memberUid, memberUin);
                            LearnMember(gid, memberUin, RoleToString(Ob11.Str(m, "role")) , Ob11.Long(m, "level").ToString(), Ob11.Str(m, "card"));
                            members.Add(new Dictionary<string, object?>
                            {
                                ["user_id"] = memberUin,
                                ["nickname"] = Ob11.Str(m, "nick") ?? "",
                                ["card"] = Ob11.Str(m, "card") ?? "",
                                ["role"] = RoleToString(Ob11.Str(m, "role")),
                                ["shut_up_timestamp"] = Ob11.Long(m, "shutUp", Ob11.Long(m, "shutUpUntilTime")),
                            });
                        }
                    }
                    return ToElement(members.ToArray());
                }

            case "get_group_member_info":
                {
                    var gid = Ob11.Str(j, "group_id") ?? throw new Ob11ParamException("group_id");
                    var uin = Ob11.Str(j, "user_id") ?? throw new Ob11ParamException("user_id");
                    var uid = await ResolveUidAsync(uin);
                    var r = await CallKernel("group.memberInfo", new { groupCode = gid, uid });
                    var m = r.TryGetProperty("member", out var mm) && mm.ValueKind == JsonValueKind.Object ? mm : default;
                    long shutTs = 0;
                    if (m.ValueKind == JsonValueKind.Object)
                    {
                        shutTs = Ob11.Long(m, "shutUpUntilTime", Ob11.Long(m, "shutUp"));
                        LearnMember(gid, uin, Str3(m, "role"), Ob11.Long(m, "level").ToString(), Str3(m, "cardName"));
                    }
                    return ToElement(new Dictionary<string, object?>
                    {
                        ["group_id"] = gid,
                        ["user_id"] = uin,
                        ["nickname"] = Str3(m, "nick"),
                        ["card"] = Str3(m, "cardName"),
                        ["role"] = RoleToString(Str3(m, "role")),
                        ["title"] = Str3(m, "specialTitle") ?? "",
                        ["join_time"] = m.ValueKind == JsonValueKind.Object ? Ob11.Long(m, "joinTime") : 0,
                        ["level"] = m.ValueKind == JsonValueKind.Object ? Ob11.Long(m, "level") : 0,
                        ["sex"] = "unknown",
                        ["age"] = 0,
                        ["shut_up_timestamp"] = shutTs,
                    });
                }

            case "set_group_admin":
                {
                    var gid = Ob11.Str(j, "group_id") ?? throw new Ob11ParamException("group_id");
                    var uid = await ResolveUidAsync(Ob11.Str(j, "user_id") ?? throw new Ob11ParamException("user_id"));
                    // NT GroupMemberRole: 3 = admin, 2 = member
                    await CallKernel("group.setRole", new { groupCode = gid, uid, role = Ob11.Bool(j, "enable", true) ? 3 : 2 });
                    return ToElement(new object());
                }

            case "set_group_kick":
                {
                    var gid = Ob11.Str(j, "group_id") ?? throw new Ob11ParamException("group_id");
                    var uid = await ResolveUidAsync(Ob11.Str(j, "user_id") ?? throw new Ob11ParamException("user_id"));
                    await CallKernel("group.kick", new
                    {
                        groupCode = gid,
                        uids = new[] { uid },
                        refuseForever = Ob11.Bool(j, "reject_add_request"),
                    });
                    return ToElement(new object());
                }

            case "set_group_ban":
                {
                    var gid = Ob11.Str(j, "group_id") ?? throw new Ob11ParamException("group_id");
                    var uin = Ob11.Str(j, "user_id") ?? throw new Ob11ParamException("user_id");
                    var uid = await ResolveUidAsync(uin);
                    var dur = Ob11.Long(j, "duration");
                    await CallKernel("group.banMember", new
                    {
                        groupCode = gid,
                        list = new[] { new { uid, timeStamp = dur } },
                    });
                    return ToElement(new object());
                }

            case "set_group_whole_ban":
                {
                    var gid = Ob11.Str(j, "group_id") ?? throw new Ob11ParamException("group_id");
                    await CallKernel("group.ban", new { groupCode = gid, shutUp = Ob11.Bool(j, "enable", true) });
                    return ToElement(new object());
                }

            case "set_group_card":
                {
                    var gid = Ob11.Str(j, "group_id") ?? throw new Ob11ParamException("group_id");
                    var uid = await ResolveUidAsync(Ob11.Str(j, "user_id") ?? throw new Ob11ParamException("user_id"));
                    await CallKernel("group.setCard", new { groupCode = gid, uid, cardName = Ob11.Str(j, "card") ?? "" });
                    return ToElement(new object());
                }

            case "set_group_special_title":
                {
                    var gid = Ob11.Str(j, "group_id") ?? throw new Ob11ParamException("group_id");
                    var uid = await ResolveUidAsync(Ob11.Str(j, "user_id") ?? throw new Ob11ParamException("user_id"));
                    // must NOT fall back to group.setCard — that changes the group card,
                    // not the special title; the kernel method is feature-detected on
                    // the bridge and errors clearly on builds without it
                    await CallKernel("group.setSpecialTitle", new { groupCode = gid, uid, specialTitle = Ob11.Str(j, "special_title") ?? "" });
                    return ToElement(new object());
                }

            case "set_group_name":
                {
                    var gid = Ob11.Str(j, "group_id") ?? throw new Ob11ParamException("group_id");
                    await CallKernel("group.setName", new { groupCode = gid, groupName = Ob11.Str(j, "group_name") ?? "" });
                    return ToElement(new object());
                }

            case "set_group_leave":
                {
                    var gid = Ob11.Str(j, "group_id") ?? throw new Ob11ParamException("group_id");
                    await CallKernel("group.quit", new { groupCode = gid, needDeleteLocalMsg = false });
                    return ToElement(new object());
                }

            case "set_friend_add_request":
                {
                    var flag = Ob11.Str(j, "flag") ?? throw new Ob11ParamException("flag");
                    var parts = flag.Split('|');
                    await CallKernel("buddy.approveRequest", new
                    {
                        friendUid = parts[0],
                        reqTime = parts.Length > 1 ? parts[1] : "0",
                        accept = Ob11.Bool(j, "approve", true),
                    });
                    return ToElement(new object());
                }

            case "set_group_add_request":
                // group request handling needs operateSysNotify with notify seq — requires bridge support
                throw new Ob11UnsupportedException("set_group_add_request pending bridge support (operateSysNotify)");

            // ---- 标准剩余 API：显式unsupported（known-but-unsupported → retcode 1200，区别于 1404 未知动作） ----
            case "send_like":
                throw new Ob11UnsupportedException("send_like not supported: bridge kernel has no like-send method");
            case "set_group_anonymous_ban":
            case "set_group_anonymous":
                throw new Ob11UnsupportedException("anonymous messaging was removed in NTQQ");
            case "get_group_honor_info":
                throw new Ob11UnsupportedException("get_group_honor_info not supported (requires web credentials)");

            // ---- 凭证类：NT 无 web 凭证通道，按 NapCat 口径返回空值 ----
            case "get_cookies":
                return ToElement(new { cookies = "" });
            case "get_csrf_token":
                return ToElement(new { token = 0 });
            case "get_credentials":
                return ToElement(new { cookies = "", csrf_token = 0 });

            case "clean_cache":
                {
                    // 标准 clean_cache：清运行期索引缓存；SQLite 消息历史保留（非缓存数据）
                    _msgIndex.Clear(); _msgIndexOrder.Clear();
                    _seqIndex.Clear(); _seqIndexOrder.Clear();
                    _mediaIndex.Clear(); _mediaIndexOrder.Clear();
                    _markerToRealId.Clear(); _markerOrder.Clear();
                    _recallEmitted.Clear(); _recallOperator.Clear(); _recentRecallPeer.Clear();
                    _memberCache.Clear();
                    lock (_memberFetchLock) { _memberListFetched.Clear(); }
                    await _store.CleanCacheAsync();
                    return ToElement(new object());
                }

            case "set_restart":
                {
                    if (!_opts.AllowRestart)
                        throw new Ob11UnsupportedException("set_restart disabled (AaR:AllowRestart=false)");
                    var delay = Ob11.Long(j, "delay", 0);
                    _logger.LogWarning("set_restart: process exit in {Ms}ms (service manager should restart)", delay);
                    _ = Task.Run(async () =>
                    {
                        try { await Task.Delay((int)Math.Max(0, delay)); } catch { }
                        Environment.Exit(0);
                    });
                    // 标准要求 status=async（重启会中断当前响应，返回后立刻执行）
                    throw new Ob11AsyncSignal();
                }

            case "get_group_at_all_remain":
                {
                    var gid = Ob11.Str(j, "group_id") ?? throw new Ob11ParamException("group_id");
                    var r = await CallKernel("group.remainAtTimes", new { groupCode = gid });
                    var info = r.TryGetProperty("data", out var dd) && dd.ValueKind == JsonValueKind.Object ? dd : r;
                    var at = info.TryGetProperty("atInfo", out var ai) && ai.ValueKind == JsonValueKind.Object ? ai : default;
                    if (at.ValueKind != JsonValueKind.Object) return r;
                    return ToElement(new Dictionary<string, object?>
                    {
                        ["can_at_all"] = Ob11.Bool(at, "canAtAll"),
                        ["remain_at_all_count_for_uin"] = Ob11.Long(at, "RemainAtAllCountForUin"),
                        ["remain_at_all_count_for_group"] = Ob11.Long(at, "RemainAtAllCountForGroup"),
                        ["at_times_msg"] = at.TryGetProperty("atTimesMsg", out var tm) && tm.ValueKind == JsonValueKind.String ? tm.GetString() : "",
                        ["can_not_at_all_msg"] = at.TryGetProperty("canNotAtAllMsg", out var cn) && cn.ValueKind == JsonValueKind.String ? cn.GetString() : "",
                    });
                }

            case "get_recent_contact":
                return await CallKernel("recent.list", null);

            case "can_send_image":
            case "can_send_record":
                return ToElement(new { yes = true });

            case "get_status":
                return ToElement(new Dictionary<string, object?>
                {
                    ["online"] = _bridge.IsBridgeConnected,
                    ["good"] = _bridge.IsBridgeConnected,
                });

            case "get_version_info":
                return ToElement(new Dictionary<string, object?>
                {
                    ["app_name"] = "AaR",
                    ["app_version"] = ServerVersion,
                    ["protocol_version"] = Ob11.ProtocolVersion,
                    ["coolq_edition"] = "AaR",
                    ["plugin_version"] = ServerVersion,
                });

            case "get_image":
            case "get_record":
            case "get_file":
                {
                    var file = Ob11.Str(j, "file") ?? throw new Ob11ParamException("file");
                    var kind = action == "get_image" ? "image" : action == "get_record" ? "record" : "file";
                    var key = file.ToLowerInvariant();
                    _mediaIndex.TryGetValue(key, out var media);
                    // also accept a bare md5 even when the caller passes an uuid-ish name
                    if (media is null && file.Length == 32 && file.All(c => Uri.IsHexDigit(c)))
                        _mediaIndex.TryGetValue(new string(file.ToLowerInvariant().ToArray()), out media);
                    // SQLite 回查（重启后仍可取 media 引用），命中回填内存 L1
                    media ??= await FindMediaAsync(key);
                    string url = media?.Url ?? "";
                    string? local = media?.LocalPath;
                    if (local is null || !File.Exists(local))
                    {
                        // ask the kernel to (re)download the media element on demand
                        if (media is not null && !string.IsNullOrEmpty(media.ElementId))
                        {
                            try
                            {
                                var dl = await CallKernel("msg.downloadMedia", new
                                {
                                    chatType = media.ChatType,
                                    peerUid = media.PeerUid,
                                    msgId = media.MsgId,
                                    elementId = media.ElementId,
                                });
                                var dlPath = dl.TryGetProperty("filePath", out var fp) && fp.ValueKind == JsonValueKind.String ? fp.GetString() : null;
                                if (!string.IsNullOrEmpty(dlPath) && File.Exists(dlPath)) local = dlPath;
                            }
                            catch { /* fall through with metadata only */ }
                        }
                    }
                    // image url fallback: legacy md5-addressed qpic url
                    if (kind == "image" && string.IsNullOrEmpty(url) && key.Length == 32 && key.All(c => Uri.IsHexDigit(c)))
                        url = "https://gchat.qpic.cn/gchatpic_new/0/0-0-" + key.ToUpperInvariant() + "/0";
                    var data = new Dictionary<string, object?>
                    {
                        ["file"] = local ?? file,
                        ["url"] = url ?? "",
                    };
                    if (kind != "image")
                    {
                        data["name"] = media?.Name ?? file;
                        data["size"] = long.TryParse(media?.Size, out var sz) ? sz : 0;
                        if (kind == "file") data["file_id"] = key;
                        if (media?.LocalPath is { } lp && File.Exists(lp)) data["path"] = lp;
                    }
                    // 标准 get_record 支持 out_format（mp3/amr/…）：ffmpeg 尽力转换，失败返回原文件
                    if (kind == "record" && local is not null &&
                        Ob11.Str(j, "out_format") is { } outFmt && !string.IsNullOrWhiteSpace(outFmt))
                    {
                        var converted = await TryConvertAudioAsync(local, outFmt);
                        if (converted is not null) { data["file"] = converted; data["path"] = converted; }
                    }
                    return ToElement(data);
                }

            case "send_poke":
            case "group_poke":
            case "friend_poke":
                // NapCat's kernel path also does not implement poke sending (requires packet stack).
                // Kept as experimental passthrough to msg.poke — returns the raw kernel result.
                {
                    if (Ob11.Get(j, "group_id").ValueKind is not JsonValueKind.Undefined)
                    {
                        var gid = Ob11.Str(j, "group_id") ?? throw new Ob11ParamException("group_id");
                        var target = Ob11.Str(j, "target_id") ?? Ob11.Str(j, "user_id") ?? throw new Ob11ParamException("target_id");
                        return await CallKernel("msg.poke", new { chatType = 2, peerUid = gid, targetUin = target });
                    }
                    var uidTarget = Ob11.Str(j, "user_id") ?? throw new Ob11ParamException("user_id");
                    var tUid = await ResolveUidAsync(uidTarget);
                    return await CallKernel("msg.poke", new { chatType = 1, peerUid = tUid, targetUid = tUid });
                }

            case "set_msg_emoji_like":
                {
                    var msgId = Ob11.Str(j, "message_id") ?? throw new Ob11ParamException("message_id");
                    var idx = await FindIndexAsync(msgId)
                        ?? throw new Ob11BadException("unknown message_id: " + msgId);
                    var parts = idx.Split('|');
                    return await CallKernel("msg.emojiLike", new
                    {
                        chatType = long.Parse(parts[0]),
                        peerUid = parts[1],
                        msgSeq = parts[2],
                        emojiId = Ob11.Str(j, "emoji_id") ?? throw new Ob11ParamException("emoji_id"),
                        set = Ob11.Bool(j, "set", true),
                    });
                }

            case "get_group_msg_history":
                {
                    var gid = Ob11.Str(j, "group_id") ?? throw new Ob11ParamException("group_id");
                    var seq = Ob11.Str(j, "message_seq") ?? "0";
                    var count = Ob11.Long(j, "count", 20);
                    // message_seq=0 means "latest window"; fetch newest-first
                    var r = await CallKernel("msg.historyBySeq", new { chatType = 2, peerUid = gid, msgSeq = seq, count, desc = true });
                    return await BuildHistoryResponse(r, gid);
                }

            case "get_friend_msg_history":
                {
                    var uin = Ob11.Str(j, "user_id") ?? throw new Ob11ParamException("user_id");
                    var uid = await ResolveUidAsync(uin);
                    var seq = Ob11.Str(j, "message_seq") ?? "0";
                    var count = Ob11.Long(j, "count", 20);
                    var r = await CallKernel("msg.historyBySeq", new { chatType = 1, peerUid = uid, msgSeq = seq, count, desc = true });
                    return await BuildHistoryResponse(r, uin);
                }

            case "get_forward_msg":
                {
                    // 标准参数是 id；NapCat 风格的 message_id/res_id 一并接受。
                    // 容器定位优先查存储（real msgId ↔ resId），回退内存索引。
                    var msgId = Ob11.Str(j, "id") ?? Ob11.Str(j, "message_id") ?? Ob11.Str(j, "res_id")
                        ?? throw new Ob11ParamException("id");
                    long chatType;
                    string peerUid, containerMsgId;
                    var fwd = await _store.GetForwardAsync(msgId) ?? await _store.GetForwardByResIdAsync(msgId);
                    if (fwd is not null)
                    {
                        containerMsgId = fwd.MessageId;
                        chatType = fwd.ChatType;
                        peerUid = fwd.PeerUid;
                    }
                    else
                    {
                        var idx = await FindIndexAsync(msgId)
                            ?? throw new Ob11BadException("forward container not indexed (send/observe it first): " + msgId);
                        var fp = idx.Split('|');
                        chatType = long.Parse(fp[0]);
                        peerUid = fp[1];
                        containerMsgId = msgId;
                    }
                    var r = await CallKernel("msg.getForward", new
                    {
                        chatType,
                        peerUid,
                        rootMsgId = containerMsgId,
                        parentMsgId = containerMsgId,
                    });
                    var msgList = r.TryGetProperty("msgList", out var ml) ? ml : (JsonElement)default;
                    if (msgList.ValueKind == JsonValueKind.Array && msgList.GetArrayLength() > 0)
                    {
                        var items = new List<object?>();
                        foreach (var rec in msgList.EnumerateArray())
                        {
                            var segs = await Ob11Mapper.ElementsToSegmentsAsync(
                                rec.TryGetProperty("elements", out var els) ? els : default,
                                ResolveUinForMapperAsync);
                            items.Add(new Dictionary<string, object?>
                            {
                                ["content"] = segs,
                                ["sender"] = new Dictionary<string, object?>
                                {
                                    ["user_id"] = Ob11.Str(rec, "senderUin") ?? await ResolveUinCachedAsync(Ob11.Str(rec, "senderUid") ?? ""),
                                    ["nickname"] = Ob11.Str(rec, "sendNickName") ?? "",
                                },
                                ["time"] = Ob11.Long(rec, "msgTime"),
                            });
                        }
                        return ToElement(new Dictionary<string, object?> { ["messages"] = items });
                    }
                    throw new Ob11BadException("forward not found: " + msgId);
                }

            case "send_group_forward_msg":
                {
                    var gid = Ob11.Str(j, "group_id") ?? throw new Ob11ParamException("group_id");
                    return await SendForwardAsync(j, 2, gid);
                }

            case "send_private_forward_msg":
                {
                    var uid = Ob11.Str(j, "user_id") ?? throw new Ob11ParamException("user_id");
                    var tuid = await ResolveUidAsync(uid);
                    return await SendForwardAsync(j, 1, tuid);
                }

            case "get_essence_msg_list":
                {
                    var gid = Ob11.Str(j, "group_id") ?? throw new Ob11ParamException("group_id");
                    return await CallKernel("group.essence.list", new { groupCode = gid });
                }

            case "set_essence_msg":
                {
                    var msgId = Ob11.Str(j, "message_id") ?? throw new Ob11ParamException("message_id");
                    var eidx = await FindIndexAsync(msgId)
                        ?? throw new Ob11BadException("unknown message_id: " + msgId);
                    var ep = eidx.Split('|');
                    return await CallKernel("group.essence.add", new { groupCode = ep[1], msgId });
                }

            case "del_essence_msg":
                {
                    var msgId = Ob11.Str(j, "message_id") ?? throw new Ob11ParamException("message_id");
                    var didx = await FindIndexAsync(msgId)
                        ?? throw new Ob11BadException("unknown message_id: " + msgId);
                    var dp = didx.Split('|');
                    return await CallKernel("group.essence.remove", new { groupCode = dp[1], msgId });
                }

            case "get_group_shut_list":
                {
                    var gid = Ob11.Str(j, "group_id") ?? throw new Ob11ParamException("group_id");
                    return await CallKernel("group.shutList", new { groupCode = gid });
                }

            case "send_group_notice":
                {
                    var gid = Ob11.Str(j, "group_id") ?? throw new Ob11ParamException("group_id");
                    var content = Ob11.Str(j, "content") ?? throw new Ob11ParamException("content");
                    return await CallKernel("group.notice.publish", new { groupCode = gid, content, pinned = Ob11.Long(j, "pinned") });
                }

            case "get_group_notice":
                throw new Ob11UnsupportedException("get_group_notice pending v2 (getGroupBulletinList shape)");

            case "download_file":
                {
                    var url = Ob11.Str(j, "url") ?? throw new Ob11ParamException("url");
                    if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                        !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        throw new Ob11ParamException("url (http/https required)");
                    var dir = Path.Combine(Path.GetTempPath(), "aar", "downloads");
                    Directory.CreateDirectory(dir);
                    string ext;
                    try { ext = Path.GetExtension(new Uri(url).AbsolutePath); }
                    catch { throw new Ob11ParamException("url (malformed)"); }
                    if (string.IsNullOrEmpty(ext)) ext = ".bin";
                    var file = Path.Combine(dir, "dl-" + Guid.NewGuid().ToString("N") + ext);
                    // shared client: a per-call new HttpClient() leaks sockets
                    using (var resp2 = await DownloadHttp.GetAsync(url))
                    {
                        resp2.EnsureSuccessStatusCode();
                        await using var fs = File.Create(file);
                        await resp2.Content.CopyToAsync(fs);
                    }
                    return ToElement(new Dictionary<string, object?> { ["file"] = file });
                }

            case "send_input_status":
                {
                    var peerUid = Ob11.Str(j, "peer_uid") ?? throw new Ob11ParamException("peer_uid");
                    return await CallKernel("msg.inputStatus", new { chatType = 1, peerUid, eventType = Ob11.Long(j, "event_type") });
                }

            case "debug_kernel":
                // diagnostics only: gated by config, invokes a registered kernel method raw
                if (!_opts.EnableDebugKernel)
                    throw new Ob11UnsupportedException("debug_kernel disabled (set AaR:EnableDebugKernel=true)");
                {
                    var method = Ob11.Str(j, "method") ?? throw new Ob11ParamException("method");
                    var rpcParams = new Dictionary<string, object?> { ["method"] = method };
                    rpcParams["params"] = Ob11.Get(j, "params").ValueKind == JsonValueKind.Object ? (object)Ob11.Get(j, "params") : new { };
                    return await CallKernel("debug.call", rpcParams);
                }

            default:
                // 标准口径：未知动作 → retcode 1404（HTTP 404 / WS 1404）
                throw new Ob11UnknownActionException("unknown action: " + action);
        }
    }

    private static string BuildRawText(JsonElement j)
    {
        var msg = Ob11.Get(j, "message");
        if (msg.ValueKind == JsonValueKind.String) return msg.GetString() ?? "";
        if (msg.ValueKind == JsonValueKind.Array)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var s in msg.EnumerateArray())
                if (s.TryGetProperty("data", out var d) && d.TryGetProperty("text", out var t))
                    sb.Append(t.GetString());
            return sb.ToString();
        }
        return "";
    }

    private static string? NickFromProfile(JsonElement prof)
    {
        if (prof.ValueKind != JsonValueKind.Object) return null;
        if (prof.TryGetProperty("coreInfo", out var ci) && ci.TryGetProperty("nick", out var n))
            return n.GetString();
        if (prof.TryGetProperty("nick", out var n2)) return n2.GetString();
        return null;
    }

    /// <summary>ffmpeg 尽力转换语音格式（get_record out_format）；失败返回 null（调用方回退原文件）。</summary>
    private static async Task<string?> TryConvertAudioAsync(string src, string outFormat)
    {
        var fmt = outFormat.TrimStart('.').ToLowerInvariant();
        if (fmt is not ("mp3" or "amr" or "wma" or "m4a" or "spx" or "ogg" or "wav" or "flac")) return null;
        var ffmpeg = Ob11Mapper.FindFfmpeg();
        if (ffmpeg is null || !File.Exists(src)) return null;
        var dst = Path.Combine(Path.GetTempPath(), "aar", "audio-" + Guid.NewGuid().ToString("N") + "." + fmt);
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        try
        {
            using var proc = new System.Diagnostics.Process();
            proc.StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = "-y -v error -i \"" + src + "\" \"" + dst + "\"",
                UseShellExecute = false,
                RedirectStandardError = true,
            };
            proc.Start();
            var wait = proc.WaitForExitAsync();
            var done = await Task.WhenAny(wait, Task.Delay(20000));
            if (done != wait)
            {
                try { proc.Kill(true); } catch { }
                return null;
            }
            return wait.IsCompletedSuccessfully && proc.ExitCode == 0 && File.Exists(dst) ? dst : null;
        }
        catch { return null; }
    }

    private static string? Str3(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        return obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    private static string RoleToString(string? role) => role switch
    {
        // NT GroupMemberRole: 4 = owner, 3 = admin, 2 = member
        "4" or "owner" => "owner",
        "3" or "admin" => "admin",
        _ => "member",
    };

    private async Task PrimeUinLookupAsync()
    {
        try
        {
            var b = await CallKernel("buddy.list", null);
            if (b.TryGetProperty("friends", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var f in arr.EnumerateArray())
                    LearnUin(Ob11.Str(f, "uid"), Ob11.Str(f, "uin"));
            var g = await CallKernel("group.list", new { forced = false });
            if (g.TryGetProperty("groups", out var garr) && garr.ValueKind == JsonValueKind.Array)
                foreach (var grp in garr.EnumerateArray())
                {
                    // peer uid for groups is the group code itself
                    var gc = Ob11.Str(grp, "groupCode") ?? "";
                    if (gc.Length > 0) _uidToUin[gc] = gc;
                }
            // keep a snapshot delegate as a fallback layer for LookupUin
            var snap = new Dictionary<string, string>(_uidToUin);
            _primedUinLookup = uid => snap.TryGetValue(uid, out var v) ? v : null;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "prime uin lookup failed"); }
    }

    // ================= event fan-out =================

    public event Func<object, Task>? EventEmitted;

    public async Task BroadcastAsync(Dictionary<string, object?> evt)
    {
        NormalizeIdFields(evt);
        EventsBroadcast++;
        var json = Ob11Json.Serialize(evt);
        _logger.LogInformation("ob11 event broadcast: {Json}", json[..Math.Min(json.Length, 160)]);
        try { EventEmitted?.Invoke(json); }
        catch (Exception ex) { _logger.LogWarning(ex, "event subscriber failed (isolated)"); }

        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        foreach (var kv in _wsClients)
        {
            var ws = kv.Value;
            try
            {
                if (ws.State != System.Net.WebSockets.WebSocketState.Open) { RemoveWsClient(kv.Key); continue; }
                // /api 专用连接不收事件（标准：事件只推 universal/event 连接）
                if (_wsModes.TryGetValue(kv.Key, out var m) && m == WsClientMode.Api) continue;
                // per-connection send lock: parallel WebSocket.SendAsync on one socket
                // throws InvalidOperationException and would drop the client
                await SendWsFrameAsync(kv.Key, ws, bytes);
            }
            catch { RemoveWsClient(kv.Key); }
        }
    }
}

public class Ob11ParamException(string what) : Exception("missing required param: " + what);
