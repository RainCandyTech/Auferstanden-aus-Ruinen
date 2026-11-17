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
