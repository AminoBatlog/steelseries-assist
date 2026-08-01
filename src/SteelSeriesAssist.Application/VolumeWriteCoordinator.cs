using SteelSeriesAssist.Domain;

namespace SteelSeriesAssist.Application;

public sealed class VolumeWriteCoordinator : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<string, ChannelWriteState> _channels = [];
    private readonly Func<string, float, CancellationToken, Task<VolumeState>> _writeAsync;
    private readonly TimeSpan _minimumInterval;
    private readonly CancellationTokenSource _lifetimeCancellation = new();

    public VolumeWriteCoordinator(
        Func<string, float, CancellationToken, Task<VolumeState>> writeAsync,
        TimeSpan? minimumInterval = null)
    {
        _writeAsync = writeAsync;
        _minimumInterval = minimumInterval ?? TimeSpan.FromMilliseconds(75);
    }

    public event Action<string, VolumeState, bool>? WriteCompleted;

    public event Action<string, Exception>? WriteFailed;

    public void Queue(string channel, float volume, bool isFinal)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            throw new ArgumentException("A channel is required.", nameof(channel));
        }

        if (volume is < 0 or > 1 || float.IsNaN(volume))
        {
            throw new ArgumentOutOfRangeException(nameof(volume));
        }

        lock (_sync)
        {
            if (!_channels.TryGetValue(channel, out var state))
            {
                state = new ChannelWriteState();
                _channels.Add(channel, state);
            }

            state.LatestVolume = volume;
            state.HasPendingValue = true;
            state.HasFinalValue |= isFinal;
            if (!state.IsRunning)
            {
                state.IsRunning = true;
                state.LoopTask = Task.Run(() => RunChannelAsync(channel, state, _lifetimeCancellation.Token));
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetimeCancellation.Cancel();
        Task[] tasks;
        lock (_sync)
        {
            tasks = _channels.Values
                .Select(state => state.LoopTask)
                .Where(task => task is not null)
                .Cast<Task>()
                .ToArray();
        }

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _lifetimeCancellation.Dispose();
    }

    private async Task RunChannelAsync(
        string channel,
        ChannelWriteState state,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            float volume;
            bool isFinal;
            TimeSpan delay;
            lock (_sync)
            {
                if (!state.HasPendingValue)
                {
                    state.IsRunning = false;
                    state.LoopTask = null;
                    return;
                }

                volume = state.LatestVolume;
                isFinal = state.HasFinalValue;
                state.HasPendingValue = false;
                state.HasFinalValue = false;
                var elapsed = DateTimeOffset.UtcNow - state.LastWriteStartedAt;
                delay = isFinal || elapsed >= _minimumInterval
                    ? TimeSpan.Zero
                    : _minimumInterval - elapsed;
            }

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                lock (_sync)
                {
                    if (state.HasPendingValue)
                    {
                        volume = state.LatestVolume;
                        isFinal |= state.HasFinalValue;
                        state.HasPendingValue = false;
                        state.HasFinalValue = false;
                    }
                }
            }

            try
            {
                lock (_sync)
                {
                    state.LastWriteStartedAt = DateTimeOffset.UtcNow;
                }

                var confirmed = await _writeAsync(channel, volume, cancellationToken).ConfigureAwait(false);
                WriteCompleted?.Invoke(channel, confirmed, isFinal);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                WriteFailed?.Invoke(channel, exception);
            }
        }
    }

    private sealed class ChannelWriteState
    {
        public float LatestVolume { get; set; }

        public bool HasPendingValue { get; set; }

        public bool HasFinalValue { get; set; }

        public bool IsRunning { get; set; }

        public DateTimeOffset LastWriteStartedAt { get; set; } = DateTimeOffset.MinValue;

        public Task? LoopTask { get; set; }
    }
}
