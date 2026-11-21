using System.Collections.Concurrent;

namespace AaR.Server.Services;

/// <summary>OB11 调用审计环形缓冲（WebUI 总览页「最近调用」数据源）。
/// 记录动作名/retcode/耗时/时间戳，环形保留 N 条。1.0.0-preview1。</summary>
public sealed class Ob11Audit
{
    public sealed record Call(string Action, int Retcode, int Ms, DateTime At, string? Target = null);

    private readonly ConcurrentQueue<Call> _ring = new();
    private long _total, _fail;
    private readonly int _capacity;

    public Ob11Audit(int capacity = 300) => _capacity = capacity;

    public void Record(string action, int retcode, int ms, string? target = null)
    {
        _ring.Enqueue(new Call(action, retcode, ms, DateTime.Now, target));
        while (_ring.Count > _capacity && _ring.TryDequeue(out _)) { }
        Interlocked.Increment(ref _total);
        if (retcode != 0) Interlocked.Increment(ref _fail);
    }

    public IReadOnlyList<Call> Recent(int take = 40) => _ring.ToArray()[^Math.Min(take, _ring.Count)..];
    public long Total => _total;
    public long Fail => _fail;
}
