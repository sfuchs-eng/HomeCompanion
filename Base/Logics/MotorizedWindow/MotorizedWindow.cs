using Microsoft.Extensions.Logging;

namespace HomeCompanion.Base.Logics.MotorizedWindow;

internal class MotorizedWindow
{
    public ThreeWireControl? WindowControl { get; set; }
    public ThreeWireControl? ShutterControl { get; set; }

    ILogger<MotorizedWindow> Logger { get; }

    public MotorizedWindow(MotorizedWindowSpecial model, ILoggerFactory loggerFactory)
    {
        Logger = loggerFactory.CreateLogger<MotorizedWindow>();

        if ( !model.Config.Enable )
        {
            Logger.LogInformation("Motorized window logic is disabled via configuration, skipping initialization.");
            return;
        }

        WindowControl = new ThreeWireControl(
            new ThreeWireControlContext()
            {
                Name = $"{model.Name}_Window",
                PositionRequest = model.WindowPosition ?? throw new ArgumentException("WindowPosition is required for the window control"),
                PositionStatus = model.WindowPositionStatus ?? throw new ArgumentException("WindowPositionStatus is required for the window control"),
                CloseCommand = model.WindowCloseCommand ?? throw new ArgumentException("WindowCloseCommand is required for the window control"),
                OpenCommand = model.WindowOpenCommand ?? throw new ArgumentException("WindowOpenCommand is required for the window control"),
                CommandAcknowledgment = model.WindowCommandAcknowledgment ?? throw new ArgumentException("WindowCommandAcknowledgment is required for the window control"),
                Timing = model.Config.WindowTiming
            },
            // we will get the logger from DI when we initialize the logic, but we need to pass something here in the constructor, so we create a logger factory and a logger here. This is not ideal, but it works for now.
            loggerFactory.CreateLogger<ThreeWireControl>()
        );

        ShutterControl = new ThreeWireControl(
            new ThreeWireControlContext()
            {
                Name = $"{model.Name}_Shutter",
                PositionRequest = model.ShutterPosition ?? throw new ArgumentException("ShutterPosition is required for the shutter control"),
                PositionStatus = model.ShutterPositionStatus ?? throw new ArgumentException("ShutterPositionStatus is required for the shutter control"),
                CloseCommand = model.ShutterCloseCommand ?? throw new ArgumentException("ShutterCloseCommand is required for the shutter control"),
                OpenCommand = model.ShutterOpenCommand ?? throw new ArgumentException("ShutterOpenCommand is required for the shutter control"),
                CommandAcknowledgment = model.ShutterCommandAcknowledgment ?? throw new ArgumentException("ShutterCommandAcknowledgment is required for the shutter control"),
                Timing = model.Config.ShutterTiming
            },
            loggerFactory.CreateLogger<ThreeWireControl>()
        );

        WindowControl.SetRequireOpenPriorToOpening(ShutterControl);
    }

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        if ( WindowControl == null || ShutterControl == null )
        {
            return;
        }
        await WindowControl.StartAsync(cancellationToken);
        await ShutterControl.StartAsync(cancellationToken);
    }
}
