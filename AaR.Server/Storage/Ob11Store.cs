using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AaR.Server.Storage;

/// <summary>
/// EF Core + SQLite durable store behind the OB11 in-memory indexes.
/// Provider "sqlite" persists to a WAL database file; "memory" keeps the same
/// schema in a shared-cache in-memory database (Data Source=file:aar_mem*mode=memory*
/// cache=shared) — identical engine and behavior, nothing touches disk. A single
/// anchor connection is held open for the process lifetime so the shared memory
/// database survives across pooled connections.
/// </summary>
public sealed class Ob11Store
{
    private readonly IDbContextFactory<AarDbContext> _factory;
    private readonly ILogger<Ob11Store> _logger;
    private readonly bool _isMemory;
    // serialize writes: SQLite single-writer; contention here is negligible at OB11 rates
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private SqliteConnection? _anchor;

    public Ob11Store(AaROptions opts, ILogger<Ob11Store> logger)
    {
        _logger = logger;
        _isMemory = !string.Equals(opts.Storage.Provider, "sqlite", StringComparison.OrdinalIgnoreCase);
        var cs = _isMemory
            ? "Data Source=file:aar_mem?mode=memory&cache=shared"
            : "Data Source=" + ResolvePath(opts.Storage.Path);
        _factory = new DbContextFactoryImpl(cs);
    }

    private static string ResolvePath(string p)
    {
        if (System.IO.Path.IsPathRooted(p)) return p;
        var root = AppContext.BaseDirectory; // ContentRoot may not be set under systemd
        return System.IO.Path.Combine(Directory.GetCurrentDirectory(), p);
    }

    /// <summary>Create schema + WAL mode; for the memory provider, open the anchor connection.</summary>
    public async Task InitializeAsync()
    {
        if (_isMemory)
        {
            _anchor = new SqliteConnection("Data Source=file:aar_mem?mode=memory&cache=shared");
            _anchor.Open(); // hold for process lifetime: last-close would drop the db
        }
        await using var db = await _factory.CreateDbContextAsync();
        if (!_isMemory)
        {
            var dir = Path.GetDirectoryName(((SqliteConnection)db.Database.GetDbConnection()).DataSource);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        }
        await db.Database.EnsureCreatedAsync();
        // EnsureCreated 对已存在的库不建新表（1.0.0-preview2 新增 Accounts/ReverseTargets
        // 需在老库上补齐）——幂等 DDL，新库上为空操作
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "Accounts" (
                "Uin" TEXT NOT NULL CONSTRAINT "PK_Accounts" PRIMARY KEY,
                "Nickname" TEXT NULL,
                "Bridge" TEXT NOT NULL,
                "Version" TEXT NOT NULL,
                "FirstSeenAt" TEXT NOT NULL,
                "LastSeenAt" TEXT NOT NULL,
                "LastOfflineAt" TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_Accounts_LastSeenAt" ON "Accounts" ("LastSeenAt");
            CREATE TABLE IF NOT EXISTS "ReverseTargets" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_ReverseTargets" PRIMARY KEY AUTOINCREMENT,
                "Kind" TEXT NOT NULL,
                "Url" TEXT NOT NULL,
                "AccessToken" TEXT NULL,
                "SelfId" TEXT NULL,
                "ReconnectMs" INTEGER NOT NULL,
                "Enabled" INTEGER NOT NULL,
                "CreatedAt" TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_ReverseTargets_Kind" ON "ReverseTargets" ("Kind");
            """);
        _logger.LogInformation("ob11 store ready: provider={Provider} source={Source}",
            _isMemory ? "memory(shared-cache)" : "sqlite",
            _isMemory ? ":memory:" : ((SqliteConnection)db.Database.GetDbConnection()).DataSource);
    }

    // ---------- messages ----------

    public async Task SaveMessageAsync(StoredMessage m)
    {
        await _writeLock.WaitAsync();
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            var existing = await db.Messages.FindAsync(m.MessageId);
            if (existing is null) db.Messages.Add(m);
            else
            {
                existing.Seq = m.Seq; existing.PeerUid = m.PeerUid; existing.ChatType = m.ChatType;
                existing.SenderUid = m.SenderUid; existing.SenderUin = m.SenderUin;
                existing.MsgTime = m.MsgTime; existing.IsSelfSent = m.IsSelfSent;
            }
            await db.SaveChangesAsync();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "store save message failed"); }
        finally { _writeLock.Release(); }
    }

    public async Task<StoredMessage?> GetMessageAsync(string messageId)
    {
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.Messages.FindAsync(messageId);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "store get message failed"); return null; }
    }

    public async Task<StoredMessage?> GetBySeqAsync(long chatType, string peerUid, string seq)
    {
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.Messages.FirstOrDefaultAsync(m =>
                m.ChatType == chatType && m.PeerUid == peerUid && m.Seq == seq);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "store get by seq failed"); return null; }
    }

    // ---------- media ----------

    public async Task SaveMediaAsync(StoredMedia m)
    {
        await _writeLock.WaitAsync();
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            var existing = await db.Media.FindAsync(m.Key);
            if (existing is null) db.Media.Add(m);
            else db.Entry(existing).CurrentValues.SetValues(m);
            await db.SaveChangesAsync();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "store save media failed"); }
        finally { _writeLock.Release(); }
    }

    public async Task<StoredMedia?> GetMediaAsync(string key)
    {
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.Media.FindAsync(key);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "store get media failed"); return null; }
    }

    // ---------- forwards ----------

    public async Task SaveForwardAsync(StoredForward f)
    {
        await _writeLock.WaitAsync();
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            var existing = await db.Forwards.FindAsync(f.MessageId);
            if (existing is null) db.Forwards.Add(f);
            else db.Entry(existing).CurrentValues.SetValues(f);
            await db.SaveChangesAsync();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "store save forward failed"); }
        finally { _writeLock.Release(); }
    }

    public async Task<StoredForward?> GetForwardAsync(string messageId)
    {
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.Forwards.FindAsync(messageId);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "store get forward failed"); return null; }
    }

    public async Task<StoredForward?> GetForwardByResIdAsync(string resId)
    {
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.Forwards.FirstOrDefaultAsync(f => f.ResId == resId);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "store get forward by resid failed"); return null; }
    }

    // ---------- uid → uin ----------

    public async Task LearnUinAsync(string uid, string uin)
    {
        if (uid.Length == 0 || uin.Length == 0) return;
        await _writeLock.WaitAsync();
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            var existing = await db.UidUins.FindAsync(uid);
            if (existing is null)
                db.UidUins.Add(new UidUin { Uid = uid, Uin = uin });
            else if (existing.Uin != uin) { existing.Uin = uin; existing.UpdatedAt = DateTime.UtcNow; }
            else return;
            await db.SaveChangesAsync();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "store learn uin failed"); }
        finally { _writeLock.Release(); }
    }

    public async Task<string?> GetUinAsync(string uid)
    {
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            return (await db.UidUins.FindAsync(uid))?.Uin;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "store get uin failed"); return null; }
    }

    // ---------- send markers ----------

    public async Task SaveMarkerAsync(string marker, string realMsgId)
    {
        if (marker.Length == 0 || realMsgId.Length == 0) return;
        await _writeLock.WaitAsync();
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            var existing = await db.Markers.FindAsync(marker);
            if (existing is null) db.Markers.Add(new StoredMarker { Marker = marker, RealMsgId = realMsgId });
            else existing.RealMsgId = realMsgId;
            await db.SaveChangesAsync();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "store save marker failed"); }
        finally { _writeLock.Release(); }
    }

    public async Task<string?> GetMarkerAsync(string marker)
    {
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            return (await db.Markers.FindAsync(marker))?.RealMsgId;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "store get marker failed"); return null; }
    }

    // ---------- accounts（1.0.0-preview2 多账号存库） ----------

    /// <summary>hello 绑定即 upsert（首见插、再见刷 LastSeen/桥型/版本/昵称）。</summary>
    public async Task TouchAccountAsync(string uin, string? nickname, string bridge, string version)
    {
        if (uin.Length == 0) return;
        await _writeLock.WaitAsync();
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            var existing = await db.Accounts.FindAsync(uin);
            if (existing is null)
            {
                db.Accounts.Add(new AccountRecord
                {
                    Uin = uin,
                    Nickname = nickname,
                    Bridge = bridge,
                    Version = version,
                });
            }
            else
            {
                existing.LastSeenAt = DateTime.UtcNow;
                existing.Bridge = bridge.Length > 0 ? bridge : existing.Bridge;
                existing.Version = version.Length > 0 ? version : existing.Version;
                if (!string.IsNullOrEmpty(nickname)) existing.Nickname = nickname;
            }
            await db.SaveChangesAsync();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "store touch account failed"); }
        finally { _writeLock.Release(); }
    }

    /// <summary>昵称 best-effort 刷新（hello 后自询 user.self_info）。</summary>
    public async Task SetAccountNicknameAsync(string uin, string? nickname)
    {
        if (uin.Length == 0 || string.IsNullOrEmpty(nickname)) return;
        await _writeLock.WaitAsync();
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            var existing = await db.Accounts.FindAsync(uin);
            if (existing is null)
                db.Accounts.Add(new AccountRecord { Uin = uin, Nickname = nickname });
            else existing.Nickname = nickname;
            await db.SaveChangesAsync();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "store set account nickname failed"); }
        finally { _writeLock.Release(); }
    }

    /// <summary>槽位断连记档（LastOfflineAt；账号仍留在已知清单）。</summary>
    public async Task TouchAccountOfflineAsync(string uin)
    {
        if (uin.Length == 0) return;
        await _writeLock.WaitAsync();
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            var existing = await db.Accounts.FindAsync(uin);
            if (existing is not null)
            {
                existing.LastOfflineAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "store touch account offline failed"); }
        finally { _writeLock.Release(); }
    }

    public async Task<List<AccountRecord>> ListAccountsAsync()
    {
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.Accounts.OrderByDescending(a => a.LastSeenAt).ToListAsync();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "store list accounts failed"); return new(); }
    }

    // ---------- reverse targets（1.0.0-preview2 反向端点存库 + 动态增删） ----------

    public async Task<List<ReverseTargetRecord>> ListReverseTargetsAsync()
    {
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.ReverseTargets.OrderBy(t => t.Id).ToListAsync();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "store list reverse targets failed"); return new(); }
    }

    public async Task<ReverseTargetRecord?> GetReverseTargetAsync(long id)
    {
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.ReverseTargets.FindAsync(id);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "store get reverse target failed"); return null; }
    }

    public async Task<ReverseTargetRecord?> AddReverseTargetAsync(ReverseTargetRecord t)
    {
        await _writeLock.WaitAsync();
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            db.ReverseTargets.Add(t);
            await db.SaveChangesAsync();
            return t;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "store add reverse target failed"); return null; }
        finally { _writeLock.Release(); }
    }

    public async Task<bool> UpdateReverseTargetAsync(ReverseTargetRecord t)
    {
        await _writeLock.WaitAsync();
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            var existing = await db.ReverseTargets.FindAsync(t.Id);
            if (existing is null) return false;
            existing.Kind = t.Kind;
            existing.Url = t.Url;
            existing.AccessToken = t.AccessToken;
            existing.SelfId = string.IsNullOrEmpty(t.SelfId) ? null : t.SelfId;
            existing.ReconnectMs = t.ReconnectMs > 0 ? t.ReconnectMs : existing.ReconnectMs;
            existing.Enabled = t.Enabled;
            await db.SaveChangesAsync();
            return true;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "store update reverse target failed"); return false; }
        finally { _writeLock.Release(); }
    }

    public async Task<bool> DeleteReverseTargetAsync(long id)
    {
        await _writeLock.WaitAsync();
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            var existing = await db.ReverseTargets.FindAsync(id);
            if (existing is null) return false;
            db.ReverseTargets.Remove(existing);
            await db.SaveChangesAsync();
            return true;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "store delete reverse target failed"); return false; }
        finally { _writeLock.Release(); }
    }

    /// <summary>seed：表空且 appsettings 配置非空 → 导入（一次性；之后表为唯一权威）。</summary>
    public async Task<int> SeedReverseTargetsAsync(System.Collections.Generic.List<ReverseTargetRecord> records)
    {
        var added = 0;
        await _writeLock.WaitAsync();
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            if (await db.ReverseTargets.AnyAsync()) return 0;
            foreach (var r in records)
            {
                db.ReverseTargets.Add(r);
                added++;
            }
            if (added > 0) await db.SaveChangesAsync();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "store seed reverse targets failed"); }
        finally { _writeLock.Release(); }
        return added;
    }

    // ---------- clean_cache ----------

    /// <summary>Drop volatile indexes (media refs / markers / uid map); message history stays.</summary>
    public async Task CleanCacheAsync()
    {
        await _writeLock.WaitAsync();
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            await db.Media.ExecuteDeleteAsync();
            await db.Markers.ExecuteDeleteAsync();
            await db.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE);");
        }
        catch (Exception ex) { _logger.LogWarning(ex, "store clean failed"); }
        finally { _writeLock.Release(); }
    }

    public async Task<(int Messages, int Media, int Uins)> StatsAsync()
    {
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            return (await db.Messages.CountAsync(), await db.Media.CountAsync(), await db.UidUins.CountAsync());
        }
        catch { return (0, 0, 0); }
    }

    /// <summary>Minimal IDbContextFactory over a fixed connection string.</summary>
    private sealed class DbContextFactoryImpl : IDbContextFactory<AarDbContext>
    {
        private readonly string _cs;
        public DbContextFactoryImpl(string cs) => _cs = cs;
        public AarDbContext CreateDbContext()
        {
            var builder = new DbContextOptionsBuilder<AarDbContext>();
            builder.UseSqlite(_cs);
            return new AarDbContext(builder.Options);
        }
        public async Task<AarDbContext> CreateDbContextAsync(CancellationToken ct = default)
            => CreateDbContext();
    }
}
