using Microsoft.EntityFrameworkCore;

namespace AaR.Server.Storage;

/// <summary>Indexed message: the durable replacement for the in-memory msgId/seq maps.</summary>
public class StoredMessage
{
    /// <summary>NT 雪花 msgId（字符串，>2^53 不可作数字）。</summary>
    public string MessageId { get; set; } = "";
    public long ChatType { get; set; }
    /// <summary>群 = 群号字符串；私聊 = 对方 uid。</summary>
    public string PeerUid { get; set; } = "";
    public string Seq { get; set; } = "";
    public string SenderUid { get; set; } = "";
    public string SenderUin { get; set; } = "";
    public long MsgTime { get; set; }
    public bool IsSelfSent { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Media reference for get_image / get_record / get_file (key = md5 或 fileUuid，小写).</summary>
public class StoredMedia
{
    public string Key { get; set; } = "";
    /// <summary>image / record / video / file</summary>
    public string Kind { get; set; } = "";
    public string ElementId { get; set; } = "";
    public string MsgId { get; set; } = "";
    public int ChatType { get; set; }
    public string PeerUid { get; set; } = "";
    public string? LocalPath { get; set; }
    public string? Url { get; set; }
    public string? Name { get; set; }
    public string? Size { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Merged-forward container: real msgId ↔ resId for get_forward_msg.</summary>
public class StoredForward
{
    /// <summary>转发容器的真实 NT msgId（kernel getForward 用）。</summary>
    public string MessageId { get; set; } = "";
    /// <summary>multimsg ark 的 resId（forward 段 id）。</summary>
    public string ResId { get; set; } = "";
    public long ChatType { get; set; }
    public string PeerUid { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Persistent uid → uin map (learned from records/lists/kernel calls).</summary>
public class UidUin
{
    public string Uid { get; set; } = "";
    public string Uin { get; set; } = "";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>send 返回的 uniqueId 标记 → 真实 msgId（onMsgInfoListUpdate 学到）。</summary>
public class StoredMarker
{
    public string Marker { get; set; } = "";
    public string RealMsgId { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>已知账号档案（1.0.0-preview2）：hello 绑定即落库，跨重启存活。
/// 正向 WS /ws/{uin} 路由与 WebUI 账号面的持久数据源。</summary>
public class AccountRecord
{
    /// <summary>账号 uin（QQ 号，字符串）。</summary>
    public string Uin { get; set; } = "";
    /// <summary>最近一次学到的昵称（hello 后经 user.self_info 自询，best-effort）。</summary>
    public string? Nickname { get; set; }
    /// <summary>hello 申报的桥型（js / headless …）。</summary>
    public string Bridge { get; set; } = "";
    /// <summary>桥申报版本。</summary>
    public string Version { get; set; } = "";
    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
    /// <summary>最近一次 hello 绑定时间。</summary>
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    /// <summary>最近一次槽位离线时间（断连记档）。</summary>
    public DateTime? LastOfflineAt { get; set; }
}

/// <summary>反向推送端点（1.0.0-preview2）：ws/http 目标存库 + 动态增删。
/// 首次启动把 appsettings AaR:Reverse 的既有配置 seed 进表，之后表为唯一权威。</summary>
public class ReverseTargetRecord
{
    public long Id { get; set; }
    /// <summary>"ws"（反向 WebSocket）| "http"（事件 POST）。</summary>
    public string Kind { get; set; } = "ws";
    public string Url { get; set; } = "";
    public string? AccessToken { get; set; }
    /// <summary>绑定账号 uin（null/空 = 全量推送，0.8.1 语义）。</summary>
    public string? SelfId { get; set; }
    public int ReconnectMs { get; set; } = 5000;
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AarDbContext : DbContext
{
    public AarDbContext(DbContextOptions<AarDbContext> options) : base(options) { }

    public DbSet<StoredMessage> Messages => Set<StoredMessage>();
    public DbSet<StoredMedia> Media => Set<StoredMedia>();
    public DbSet<StoredForward> Forwards => Set<StoredForward>();
    public DbSet<UidUin> UidUins => Set<UidUin>();
    public DbSet<StoredMarker> Markers => Set<StoredMarker>();
    public DbSet<AccountRecord> Accounts => Set<AccountRecord>();
    public DbSet<ReverseTargetRecord> ReverseTargets => Set<ReverseTargetRecord>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<StoredMessage>(e =>
        {
            e.HasKey(x => x.MessageId);
            // seq 是回查辅助键：私聊 clientSeq 可重复（NT 语义），转发容器占位 seq="0"，
            // 故只能普通索引——唯一性由 MessageId 主键承担
            e.HasIndex(x => new { x.ChatType, x.PeerUid, x.Seq });
        });
        mb.Entity<StoredMedia>(e =>
        {
            e.HasKey(x => x.Key);
            e.HasIndex(x => x.MsgId);
        });
        mb.Entity<StoredForward>(e =>
        {
            e.HasKey(x => x.MessageId);
            e.HasIndex(x => x.ResId).IsUnique();
        });
        mb.Entity<UidUin>(e => e.HasKey(x => x.Uid));
        mb.Entity<StoredMarker>(e => e.HasKey(x => x.Marker));
        mb.Entity<AccountRecord>(e =>
        {
            e.HasKey(x => x.Uin);
            e.HasIndex(x => x.LastSeenAt);
        });
        mb.Entity<ReverseTargetRecord>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Kind);
        });
    }
}
