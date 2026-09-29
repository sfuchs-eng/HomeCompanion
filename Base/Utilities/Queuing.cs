using HomeCompanion.Events;

namespace HomeCompanion.Base.Utilities;

public interface IQueueFeeder<in TChannelItem>
{
    Task EnqueueAsync(TChannelItem trigger, CancellationToken token);
    void Enqueue(TChannelItem trigger);
}

public sealed class EventBusQueueFeeder<TChannelItem>(IEventPublisher eventPublisher, Func<TChannelItem, IEvent> eventFactory) : IQueueFeeder<TChannelItem>
{
    private readonly IEventPublisher eventPublisher = eventPublisher;
    private readonly Func<TChannelItem, IEvent> eventFactory = eventFactory;

    public void Enqueue(TChannelItem trigger)
    {
        eventPublisher.Publish(eventFactory(trigger));
    }

    public async Task EnqueueAsync(TChannelItem trigger, CancellationToken token)
    {
        await eventPublisher.PublishAsync(eventFactory(trigger), token).ConfigureAwait(false);
    }
}