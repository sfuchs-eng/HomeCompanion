using System.Threading.Channels;

namespace HomeCompanion.Base.Utilities;

public class BackgroundRunner<TChannelItem>(Func<System.Threading.Channels.Channel<TChannelItem>, CancellationToken, Task> backgroundTaskFunc) : IQueueFeeder<TChannelItem>
{
    private readonly Func<System.Threading.Channels.Channel<TChannelItem>, CancellationToken, Task> backgroundTaskFunc = backgroundTaskFunc;
    private CancellationTokenSource? cts;
    private Task? backgroundTask;

    private Channel<TChannelItem> Channel { get; } = System.Threading.Channels.Channel.CreateUnbounded<TChannelItem>();

    public void Start(CancellationToken cancellationToken = default)
    {
        if (backgroundTask != null && !backgroundTask.IsCompleted)
            throw new InvalidOperationException("Background task is already running");

        cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        backgroundTask = Task.Run(() => backgroundTaskFunc(Channel, cts.Token), cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (backgroundTask == null || backgroundTask.IsCompleted)
            return;

        cts!.Cancel();
        try
        {
            // await it unless it's already completed or cancellationToken is getting cancelled, in which case just return immediately to avoid waiting for the task to complete when we are already shutting down
            var completedTask = await Task.WhenAny(backgroundTask, Task.Delay(Timeout.Infinite, cancellationToken));
            if (completedTask == backgroundTask)
                await backgroundTask; // observe any exceptions
        }
        catch (OperationCanceledException)
        {
            // expected when the task is cancelled, just ignore
        }
    }

    public async Task EnqueueAsync(TChannelItem trigger, CancellationToken token)
    {
        await Channel.Writer.WriteAsync(trigger, token);
    }

    public void Enqueue(TChannelItem trigger)
    {
        if ( !Channel.Writer.TryWrite(trigger) )
            throw new InvalidOperationException("Failed to enqueue item, channel may be closed");
    }
}