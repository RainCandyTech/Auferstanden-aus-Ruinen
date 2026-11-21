using System.Collections.Concurrent;
using System.Text;

namespace AaR.Server.Services;

/// <summary>运行日志环形缓冲（WebUI 实时日志页的数据源）。
/// 自定义 ILoggerProvider 捕获全部宿主日志（含桥上报行），环形保留 N 条，
/// 级别/模块筛选在读取侧做。1.0.0-preview1。</summary>
public sealed class LogRing
{
    public sealed record Entry(long Id, DateTime At, string Level, string Category, string Message);

    private readonly ConcurrentQueue<Entry> _ring = new();
    private long _seq;
    private readonly int _capacity;

    public LogRing(int capacity = 3000) => _capacity = capacity;

    public void Push(string level, string category, string message)
    {
        var e = new Entry(Interlocked.Increment(ref _seq), DateTime.Now, level, category, message);
        _ring.Enqueue(e);
        while (_ring.Count > _capacity && _ring.TryDequeue(out _)) { }
    }

    /// <summary>读取（自 cursor 之后的新条目；cursor=0 取最近 take 条）。</summary>
    public (IReadOnlyList<Entry> Entries, long Cursor) Read(long cursor, int take = 500)
    {
        var all = _ring.ToArray();
        if (cursor > 0)
        {
            var fresh = all.Where(e => e.Id > cursor).Take(take).ToArray();
            return (fresh, fresh.Length > 0 ? fresh[^1].Id : cursor);
        }
        var tail = all.Length > take ? all[^take..] : all;
        return (tail, tail.Length > 0 ? tail[^1].Id : 0);
    }
}

/// <summary>把 LogRing 挂进 ILoggerFactory 的 Provider（级别落 Ring，不改变既有日志行为）。</summary>
public sealed class LogRingLoggerProvider : ILoggerProvider
{
    private readonly LogRing _ring;
    public LogRingLoggerProvider(LogRing ring) => _ring = ring;

    public ILogger CreateLogger(string categoryName) => new RingLogger(_ring, categoryName);
    public void Dispose() { }

    private sealed class RingLogger : ILogger
    {
        private readonly LogRing _ring;
        private readonly string _cat;
        public RingLogger(LogRing ring, string cat) { _ring = ring; _cat = cat; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var lv = logLevel switch
            {
                LogLevel.Warning => "WARN",
                LogLevel.Error => "ERROR",
                LogLevel.Critical => "ERROR",
                _ => "INFO",
            };
            var msg = formatter(state, exception);
            if (exception is not null) msg += " :: " + exception.Message;
            _ring.Push(lv, ShortCat(_cat), msg);
        }

        private static string ShortCat(string cat)
        {
            // AaR.Server.Services.BridgeRegistry → BridgeRegistry
            var idx = cat.LastIndexOf('.');
            return idx > 0 ? cat[(idx + 1)..] : cat;
        }
    }
}
