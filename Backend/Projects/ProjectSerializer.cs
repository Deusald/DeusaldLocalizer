using System.Collections.Concurrent;

namespace DeusaldLocalizerBackend;

/// <summary>
/// Serializes work per project: one request at a time for a given project id, but different
/// projects run concurrently. Required both by the product rule and for git work-tree safety.
/// </summary>
public sealed class ProjectSerializer
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _Locks = new();

    /// <summary>Acquires the project lock; dispose the returned handle to release it.</summary>
    public async Task<IDisposable> AcquireAsync(Guid projectId, CancellationToken ct)
    {
        SemaphoreSlim gate = _Locks.GetOrAdd(projectId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        return new Releaser(gate);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _Gate;
        private          int           _Disposed;

        public Releaser(SemaphoreSlim gate) => _Gate = gate;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _Disposed, 1) == 0) _Gate.Release();
        }
    }
}
