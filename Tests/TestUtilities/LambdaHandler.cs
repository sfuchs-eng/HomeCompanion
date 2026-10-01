using HomeCompanion.Events;

namespace HomeCompanion.Tests.TestUtilities;

public sealed class LambdaHandler<T> : IEventHandler<T> where T : IEvent
{
    private Func<T, ValueTask>? func;

    public LambdaHandler(Action<T> action)
    {
        this.func = @event =>
        {
            action(@event);
            return ValueTask.CompletedTask;
        };
    }
    
    public LambdaHandler(Func<T, ValueTask> func)
    {
        this.func = func;
    }

    public ValueTask HandleAsync(T @event, CancellationToken cancellationToken = default)
    {
        return func?.Invoke(@event) ?? throw new InvalidOperationException("Handler function is not set.");
    }
}
