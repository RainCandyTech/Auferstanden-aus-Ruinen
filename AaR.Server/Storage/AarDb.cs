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

public class AarDbContext : DbContext
{
    public AarDbContext(DbContextOptions<AarDbContext> options) : base(options) { }

    public DbSet<StoredMessage> Messages => Set<StoredMessage>();
    public DbSet<StoredMedia> Media => Set<StoredMedia>();
    public DbSet<StoredForward> Forwards => Set<StoredForward>();
    public DbSet<UidUin> UidUins => Set<UidUin>();
    public DbSet<StoredMarker> Markers => Set<StoredMarker>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<StoredMessage>(e =>
        {
            e.HasKey(x => x.MessageId);
            // reply 引用按 (chatType, peerUid, seq) 反查 message_id
            e.HasIndex(x => new { x.ChatType, x.PeerUid, x.Seq }).IsUnique();
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
    }
}
