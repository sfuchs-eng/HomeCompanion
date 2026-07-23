using HomeCompanion.Base.Model;
using HomeCompanion.Diagnostics;
using HomeCompanion.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HomeCompanion.Logics.MotorizedWindow;

/// <summary>
/// Operates motorized windows with or without integrated shutter based on each 2 control wires (up/down) and 1 feedback wire (command acknowledgement/position reached) per window/shutter.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The logic is configured via the <see cref="MotorizedWindowsOptions"/> class, which is bound to the "Logics:MotorizedWindows" section of the configuration.</item>
/// <item>This logic does not automate window/shutter operation. See <see cref="Shutters.ShuttersLogic"/> for shutter automation.</item>
/// </list>
/// </remarks>
/// <typeparam name="MotorizedWindowsOptions"></typeparam>
public class MotorizedWindowsLogic(
    IOptions<MotorizedWindowsOptions> options,
    IModelProvider modelProvider,
    ILoggerFactory loggerFactory,
    ILogger<MotorizedWindowsLogic> logger
) : LogicBase(logger), IDiagnosable
{
    private readonly MotorizedWindowsOptions options = options.Value;
    private readonly ILoggerFactory loggerFactory = loggerFactory;
    private readonly ILogger<MotorizedWindowsLogic> logger = logger;

    private Dictionary<string, MotorizedWindow> windows = [];

    protected override async Task InitializeAsyncLatched(CancellationToken cancellationToken = default)
    {
        // we should already be in a life-cycle state that allows to initialize the logic when this here is called.

        // Load all configured windows and their special configurations.
        windows = modelProvider.GetModel().GetAllSpecialsByName<MotorizedWindowSpecial>()
            .Select(s => new { Name = s.Key, Special = s.Value })
            .ToDictionary(
                s => s.Name,
                s => new MotorizedWindow(
                    s.Special,
                    loggerFactory
                ));

        foreach (var window in windows.Values)
        {
            await window.StartAsync(cancellationToken);
        }

        await Task.CompletedTask;
    }

    public override async Task<IDiagnosticResultNode> GetDiagnosisAsync(CancellationToken cancellationToken)
    {
        var rootNode = DiagnosticResultNode.Create("MotorizedWindowsLogic");

        var model = modelProvider.GetModel();


        if (windows.Count == 0)
        {
            rootNode.AddRecord("No motorized windows configured.");
            return rootNode;
        }
        
        foreach (var window in windows)
        {
            rootNode.Children.Add(await window.Value.GetDiagnosisAsync(cancellationToken));
        }
        return rootNode;
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
