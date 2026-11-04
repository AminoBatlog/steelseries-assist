using SteelSeriesAssist.Application;
using SteelSeriesAssist.Domain;

internal static class OperationScopeTests
{
    public static async Task StopWaitsForContinuations()
    {
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scope = new AsyncOperationScope();
        var work = scope.RunAsync(async token =>
        {
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { canceled.SetResult(); await cleanup.Task; }
        });
        var stop = scope.DisposeAsync().AsTask();
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        if (stop.IsCompleted) throw new Exception("Scope released before async cleanup finished.");
        var admitted = false;
        await scope.RunAsync(_ => { admitted = true; return Task.CompletedTask; });
        if (admitted) throw new Exception("Scope admitted work during stop.");
        cleanup.SetResult();
        await stop.WaitAsync(TimeSpan.FromSeconds(2));
        await scope.DisposeAsync();
        try { await work; } catch (OperationCanceledException) { return; }
        throw new Exception("Scope did not cancel its operation.");
    }

    public static async Task ScopeWaitsForEveryOperation()
    {
        var gates = Enumerable.Range(0, 100).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var scope = new AsyncOperationScope();
        foreach (var gate in gates) _ = scope.RunAsync(_ => gate.Task);
        var stop = scope.DisposeAsync().AsTask();
        foreach (var gate in gates.SkipLast(1)) gate.SetResult();
        if (stop.IsCompleted) throw new Exception("Scope forgot an in-flight operation.");
        gates[^1].SetResult();
        await stop.WaitAsync(TimeSpan.FromSeconds(2));
    }

    public static async Task WriterStopsAdmissionAndWaits()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = new VolumeWriteCoordinator(async (_, value, token) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return new VolumeState(value, false);
        });
        writer.Queue("game", 0.5f, false);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.WhenAll(writer.DisposeAsync().AsTask(), writer.DisposeAsync().AsTask());
        try { writer.Queue("game", 0.8f, true); }
        catch (ObjectDisposedException) { return; }
        throw new Exception("Disposed writer admitted another background task.");
    }
}
