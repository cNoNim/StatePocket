using DotNext.Threading;

namespace StatePocket.Hosting;

internal sealed class CallToolExecutionGate : IDisposable, IAsyncDisposable
{
    private readonly AsyncReaderWriterLock _lock = new();

    public ValueTask DisposeAsync()
    {
        return _lock.DisposeAsync();
    }

    public void Dispose()
    {
        _lock.Dispose();
    }

    public async ValueTask<TResult> ExecuteReadAsync<TState, TResult>(
        TState state,
        Func<TState, CancellationToken, ValueTask<TResult>> action,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(action);
        var lockTaken = false;
        try
        {
            await _lock.EnterReadLockAsync(cancellationToken)
                       .ConfigureAwait(false);
            lockTaken = true;
            cancellationToken.ThrowIfCancellationRequested();
            return await action(state, cancellationToken)
               .ConfigureAwait(false);
        }
        finally
        {
            if (lockTaken)
            {
                _lock.Release();
            }
        }
    }

    public async ValueTask<TResult> ExecuteWriteAsync<TState, TResult>(
        TState state,
        Func<TState, CancellationToken, ValueTask<TResult>> action,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(action);
        var lockTaken = false;
        try
        {
            await _lock.EnterWriteLockAsync(cancellationToken)
                       .ConfigureAwait(false);
            lockTaken = true;
            cancellationToken.ThrowIfCancellationRequested();
            return await action(state, cancellationToken)
               .ConfigureAwait(false);
        }
        finally
        {
            if (lockTaken)
            {
                _lock.Release();
            }
        }
    }
}
