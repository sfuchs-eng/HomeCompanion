using HomeCompanion.Base.Model;

namespace HomeCompanion.Logics.Shutters;

/// <summary>
/// Encapsulates the desired target state for a shutter, including the logic to determine the target position based on various inputs such as time of day, weather conditions, and user preferences.
/// Serves as input for shutter actuation, allowing for a last gating possibility to e.g. limit movement rates, or implement safety interlocks at shutter level just prior actuator hardware.
/// </summary>
public class ShutterTarget(ShutterRuntimeContext shutterRuntimeContext, ShutterPosition targetPosition)
{
    public ShutterKey ShutterKey => ShutterRuntimeContext.ShutterKey;
    public ShutterRuntimeContext ShutterRuntimeContext { get; } = shutterRuntimeContext;
    public ShutterPosition TargetPosition { get; } = targetPosition;

    public void Set(double liftPosition, double tiltAngle)
    {
        TargetPosition.LiftPosition = liftPosition;
        TargetPosition.TiltAngle = tiltAngle;
    }

    public bool IsNoOp => TargetPosition.PreventPositionChange && TargetPosition.PreventTiltChange;
}
