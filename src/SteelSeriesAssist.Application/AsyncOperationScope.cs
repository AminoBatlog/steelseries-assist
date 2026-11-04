namespace SteelSeriesAssist.Application;

/// <summary>Owns admitted work until cancellation and all continuations have completed.</summary>
public sealed class AsyncOperationScope : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly HashSet<Task> _tasks = [];
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _stopping;

    public AsyncOperationScope() => Token = _cancellation.Token;
    public CancellationToken Token { get; }

    public Task RunAsync(Func<CancellationToken, Task> operation)
    {
        lock (_sync)
        {
            if (_stopping) return Task.CompletedTask;
            var task = operation(Token);
            _tasks.Add(task);
            _ = task.ContinueWith(completed =>
            {
                lock (_sync) _tasks.Remove(completed);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return task;
        }
    }

    public ValueTask DisposeAsync()
    {
        Task[] tasks;
        lock (_sync)
        {
            if (_stopping) return new(_stopped.Task);
            _stopping = true;
            tasks = _tasks.ToArray();
        }
        _ = StopAsync(tasks);
        return new(_stopped.Task);
    }

    private async Task StopAsync(Task[] tasks)
    {
        Exception? failure = null;
        try
        {
            _cancellation.Cancel();
            try { await Task.WhenAll(tasks).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        catch (Exception exception) { failure = exception; }
        finally { _cancellation.Dispose(); }
        if (failure is null) _stopped.TrySetResult();
        else _stopped.TrySetException(failure);
    }
}
