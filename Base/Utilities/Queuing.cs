namespace HomeCompanion.Base.Utilities;

public interface IQueueFeeder<in TChannelItem>
{
    Task EnqueueAsync(TChannelItem trigger, CancellationToken token);
}