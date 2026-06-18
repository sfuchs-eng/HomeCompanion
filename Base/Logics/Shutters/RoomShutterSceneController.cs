using HomeCompanion.Base.Model;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Logics.Shutters;

public class RoomShutterSceneController(
    IValueProvider valuesProvider,
    IEventPublisher eventPublisher,
    IEventSubscriber eventSubscriber,
    TimeProvider timeProvider,
    IModelProvider modelProvider,
    ILoggerFactory loggerFactory,
    ILogger<RoomShutterSceneController> logger
) : LogicBase(eventPublisher, eventSubscriber)
{
    private readonly IValueProvider valuesProvider = valuesProvider;
    private readonly IEventPublisher eventPublisher = eventPublisher;
    private readonly IEventSubscriber eventSubscriber = eventSubscriber;
    private readonly TimeProvider timeProvider = timeProvider;
    private readonly IModelProvider modelProvider = modelProvider;
    private readonly ILoggerFactory loggerFactory = loggerFactory;
    private readonly ILogger<RoomShutterSceneController> logger = logger;

    private readonly Dictionary<RoomKey, RoomRuntime> roomRuntimes = new();

    protected override async Task InitializeAsyncLatched(CancellationToken cancellationToken = default)
    {
    }
}