using SteelSeriesAssist.Application;

internal static class UpdateInboxTests
{
    public static Task BurstPreservesLatestPartialState()
    {
        var queue = new Queue<Action>();
        SonarUpdateBatch? result = null;
        using var inbox = new SonarUpdateInbox(queue.Enqueue, batch => result = batch);
        inbox.SetActive(true);
        for (var i = 0; i < 50000; i++)
        {
            inbox.PublishVolumes([new("game", i / 50000f, null)]);
            inbox.PublishChatMix();
            inbox.PublishWriteCompleted("game", new(i / 50000f, false), isFinal: true);
        }
        inbox.PublishVolumes([new("game", null, true), new("unknown", 1, false)]);
        inbox.PublishConnection(false); inbox.PublishConnection(true);
        if (queue.Count != 1) throw new Exception("Event burst expanded the UI queue.");
        queue.Dequeue()();
        var game = result!.Volumes.Single();
        if (game.Channel != "game" || game.Volume != 49999 / 50000f || game.Muted != true ||
            result.Connected != true || !result.ChatMixChanged || result.WriteNotice?.Channel != "game")
            throw new Exception("Coalescing lost the latest partial state.");
        return Task.CompletedTask;
    }

    public static Task HiddenAndDisposedDoNotPost()
    {
        var queue = new Queue<Action>();
        var applied = 0;
        var inbox = new SonarUpdateInbox(queue.Enqueue, _ => applied++);
        for (var i = 0; i < 10000; i++) inbox.PublishVolumes([new("game", i / 10000f, false)]);
        if (queue.Count != 0) throw new Exception("Hidden window received UI work.");
        inbox.SetActive(true);
        if (queue.Count != 1) throw new Exception("Showing window did not flush latest state.");
        inbox.Dispose();
        queue.Dequeue()();
        inbox.PublishChatMix();
        if (applied != 0 || queue.Count != 0) throw new Exception("Disposed inbox applied or queued work.");
        return Task.CompletedTask;
    }
}
