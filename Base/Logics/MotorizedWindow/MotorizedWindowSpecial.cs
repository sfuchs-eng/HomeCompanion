using HomeCompanion.Base.Model;

namespace HomeCompanion.Base.Logics.MotorizedWindow;

public class CfgMotorizedWindowSpecial : CfgSpecial
{
    public bool HasShutter => !string.IsNullOrEmpty(ShutterPositionReference) && !string.IsNullOrEmpty(ShutterCloseCommandReference) && !string.IsNullOrEmpty(ShutterOpenCommandReference) && !string.IsNullOrEmpty(ShutterCommandAcknowledgmentReference);
    public bool HasWindow => !string.IsNullOrEmpty(WindowPositionReference) && !string.IsNullOrEmpty(WindowCloseCommandReference) && !string.IsNullOrEmpty(WindowOpenCommandReference) && !string.IsNullOrEmpty(WindowCommandAcknowledgmentReference);

    /// <summary>
    /// For other entities which want to control the window position, e.g. for automatic ventilation.
    /// The referenced value should be a KnxValueBool which is true when the window is closed and false when open.
    /// </summary>
    /// <value></value>
    public string? WindowPositionReference { get; set; }

    public string? WindowPositionStatusReference { get; set; }

    /// <summary>
    /// For other entities which want to control the shutter position, e.g. shadowing logic.
    /// The referenced value should be a KnxValueBool which is true when the shutter is closed and false when open.
    /// </summary>
    /// <value></value>
    public string? ShutterPositionReference { get; set; }

    public string? ShutterPositionStatusReference { get; set; }

    public string? WindowCloseCommandReference { get; set; }
    public string? WindowOpenCommandReference { get; set; }
    public string? WindowCommandAcknowledgmentReference { get; set; }

    public string? ShutterCloseCommandReference { get; set; }
    public string? ShutterOpenCommandReference { get; set; }
    public string? ShutterCommandAcknowledgmentReference { get; set; }

    public ThreeWireControlTiming WindowTiming { get; set; } = new ThreeWireControlTiming()
    {
        OpenDuration = TimeSpan.FromSeconds(40),
        CloseDuration = TimeSpan.FromSeconds(40),
        LockDelay = TimeSpan.FromSeconds(5),
        ExcessTime = TimeSpan.FromSeconds(5)
    };
    
    public ThreeWireControlTiming ShutterTiming { get; set; } = new ThreeWireControlTiming()
    {
        OpenDuration = TimeSpan.FromSeconds(33.5),
        CloseDuration = TimeSpan.FromSeconds(33.5),
        LockDelay = TimeSpan.Zero,
        ExcessTime = TimeSpan.FromSeconds(2)
    };
}

public class ThreeWireControlTiming
{
    public TimeSpan OpenDuration { get; set; }
    public TimeSpan CloseDuration { get; set; }
    public TimeSpan LockDelay { get; set; }
    public TimeSpan ExcessTime { get; set; }
}

/// <summary>
/// Represents a motorized window with optional shutter control.
/// E.g. a Velux Windows integrated into the KNX system via the KLF 200 interface.
/// Window open/close via <see cref="CfgMotorizedWindowSpecial.WindowPositionReference"/> has priority over the shuttter control.
/// Prior opening the window, the shutter will be opened if it is not already open.
/// After closing the window, the shutter is brought into the position defined by <see cref="CfgMotorizedWindowSpecial.ShutterPositionReference"/>.
/// </summary>
public class MotorizedWindowSpecial(string name, CfgMotorizedWindowSpecial config)
    : Special(name, config), IConfigBackedModelEntity
{
    public CfgMotorizedWindowSpecial Config { get; set; } = config;

    /// <summary>
    /// Requested window position. True means closed, false means open. This is the main control for the window position, e.g. for automatic ventilation.
    /// </summary>
    /// <value></value>
    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgMotorizedWindowSpecial.WindowPositionReference))]
    public IValue<bool>? WindowPosition { get; set; }

    /// <summary>
    /// Current window position. True means closed, false means open, partly or fully, or in transition. This reflects the actual state of the window.
    /// </summary>
    /// <value></value>
    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgMotorizedWindowSpecial.WindowPositionStatusReference))]
    public IValue<bool>? WindowPositionStatus { get; set; }

    /// <summary>
    /// Requested shutter position. True means closed, false means open. This is the main control for the shutter position, e.g. for shadowing logic.
    /// </summary>
    /// <value></value>
    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgMotorizedWindowSpecial.ShutterPositionReference))]
    public IValue<bool>? ShutterPosition { get; set; }

    /// <summary>
    /// Current shutter position. True means closed, false means open, partly or fully, or in transition. This reflects the actual state of the shutter.
    /// </summary>
    /// <value></value>
    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgMotorizedWindowSpecial.ShutterPositionStatusReference))]
    public IValue<bool>? ShutterPositionStatus { get; set; }

    // below are the inputs and outputs of the KLF 200 integrating e.g. Velux windows

    /// <summary>
    /// KLF200 close command input to the window. True means close, false means no command.
    /// </summary>
    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgMotorizedWindowSpecial.WindowCloseCommandReference))]
    public IValue<bool>? WindowCloseCommand { get; set; }

    /// <summary>
    /// KLF200 open command input to the window. True means open, false means no command.
    /// </summary>
    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgMotorizedWindowSpecial.WindowOpenCommandReference))]
    public IValue<bool>? WindowOpenCommand { get; set; }

    /// <summary>
    /// KLF200 command acknowledgment input from the window. Raises to true after the command applied is fully reached. Means the window is fully closed after a close command, or fully open after an open command. False means no acknowledgment, e.g. the command is still in progress or not applied at all.
    /// </summary>
    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgMotorizedWindowSpecial.WindowCommandAcknowledgmentReference))]
    public IValue<bool>? WindowCommandAcknowledgment { get; set; }

    /// <summary> KLF200 close command input to the shutter. True means close, false means no command. </summary>
    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgMotorizedWindowSpecial.ShutterCloseCommandReference))]
    public IValue<bool>? ShutterCloseCommand { get; set; }

    /// <summary> KLF200 open command input to the shutter. True means open, false means no command. </summary>
    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgMotorizedWindowSpecial.ShutterOpenCommandReference))]
    public IValue<bool>? ShutterOpenCommand { get; set; }

    /// <summary> KLF200 command acknowledgment input from the shutter. Raises to true after the command applied is fully reached. Means the shutter is fully closed after a close command, or fully open after an open command. False means no acknowledgment, e.g. the command is still in progress or not applied at all. </summary>
    [ModelValueBinding(SourceConfigPropertyName = nameof(CfgMotorizedWindowSpecial.ShutterCommandAcknowledgmentReference))]
    public IValue<bool>? ShutterCommandAcknowledgment { get; set; }
}