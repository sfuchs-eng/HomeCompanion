using HomeCompanion.Base.Model;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Logics.Shutters;

public class ShutterController(
    IValueProvider valuesProvider,
    IEventPublisher eventPublisher,
    IEventSubscriber eventSubscriber,
    TimeProvider timeProvider,
    IModelProvider modelProvider,
    ILogger<ShutterController> logger
) : LogicBase(eventPublisher, eventSubscriber)
{
    private readonly IValueProvider valuesProvider = valuesProvider;
    private readonly IEventPublisher eventPublisher = eventPublisher;
    private readonly IEventSubscriber eventSubscriber = eventSubscriber;
    private readonly TimeProvider timeProvider = timeProvider;
    private readonly IModelProvider modelProvider = modelProvider;
    private readonly ILogger<ShutterController> logger = logger;

    protected override Task InitializeAsyncLatched(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
