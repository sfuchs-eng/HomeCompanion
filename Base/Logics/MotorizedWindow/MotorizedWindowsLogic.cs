using HomeCompanion.Base.Model;
using HomeCompanion.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HomeCompanion.Base.Logics.MotorizedWindow;

public class MotorizedWindowsLogic(
    IOptions<MotorizedWindowsOptions> options,
    IModelProvider modelProvider,
    IEventPublisher publisher,
    IEventSubscriber subscriber,
    ILoggerFactory loggerFactory,
    ILogger<MotorizedWindowsLogic> logger
) : LogicBase(publisher, subscriber)
{
    private readonly MotorizedWindowsOptions options = options.Value;
    private readonly ILoggerFactory loggerFactory = loggerFactory;
    private readonly ILogger<MotorizedWindowsLogic> logger = logger;

    private Dictionary<string, MotorizedWindow> windows = [];

    protected override async Task InitializeAsyncLatched(CancellationToken cancellationToken = default)
    {
        // we should already be in a life-cycle state that allows to initialize the logic when this here is called.

        // Load all configured windows and their special configurations.
        windows = modelProvider.GetModel().Specials
            .Where(s => s.Value is MotorizedWindowSpecial)
            .Select(s => new { Name = s.Key, Special = s.Value as MotorizedWindowSpecial })
            .Where(s => s.Special != null)
            .ToDictionary(
                s => s.Name,
                s => new MotorizedWindow(
                    s.Special!,
                    loggerFactory
                ));

        foreach (var window in windows.Values)
        {
            await window.StartAsync(cancellationToken);
        }

        await Task.CompletedTask;
    }
}

public class MotorizedWindowsOptions
{
    public static string SectionName => "Logics:MotorizedWindows";
}

// need an extension to register the MotorizedWindowsOptions in the DI container, otherwise the MotorizedWindow logic cannot be initialized due to missing options.
public class MotorizedWindowsExtension : IExtensionRegistration
{
    public void RegisterServices(IExtensionRegistrationContext context)
    {
        context.Builder.Services.AddOptions<MotorizedWindowsOptions>()
            .BindConfiguration(MotorizedWindowsOptions.SectionName).ValidateOnStart();
    }
}
