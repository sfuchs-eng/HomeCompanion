namespace HomeCompanion.Logics.Shutters;

/// <summary>
/// Represents the position of a shutter, including its current state and any relevant metadata.
/// As venetian blinds are the most complex type of shutter implemented, the targets for other types are derived from the same model, e.g. roller shutters are either fully open or fully closed, but the logic can still use the same target position model and just limit the possible target positions accordingly.
/// </summary>
/// <param name="liftPosition">Lift position of the shutter, where 0.0 represents fully closed and 1.0 represents fully open. For roller shutters, this value is either 0.0 or 1.0, while for venetian blinds it can take any value in between to represent partial opening.</param>
/// <param name="tiltAngle">Tilt angle of the shutter slats in p.u., where 0 degrees represents fully open = horizontal slats, 1.0 represents slats fully closed = vertical</param>
public class ShutterPosition(double liftPosition, double tiltAngle)
{
    public static ShutterPosition NoOp => new(-1.0, -1.0);

    public static ShutterPosition Open => new(0.0, -1.0);

    public static ShutterPosition FullyClosed => new(1.0, 1.0);

    public bool IsNoOp => PreventPositionChange && PreventTiltChange;

    /// <summary>
    /// Lift position of the shutter, where 0.0 represents fully closed and 1.0 represents fully open. For roller shutters, this value is either 0.0 or 1.0, while for venetian blinds it can take any value in between to represent partial opening.
    /// </summary>
    public double LiftPosition { get; set; } = liftPosition;

    public bool PreventPositionChange => LiftPosition < 0.0;

    /// <summary>
    /// Tilt angle of the shutter slats in p.u., where 0 degrees represents fully open = horizontal slats, 1.0 represents slats fully closed = vertical.
    /// </summary>
    public double TiltAngle { get; set; } = tiltAngle;

    public bool PreventTiltChange => TiltAngle < 0.0;

    public override string ToString()
    {
        return $"{{ Lift: {(PreventPositionChange ? "NoOp" : LiftPosition.ToString("0.00"))}, Tilt: {(PreventTiltChange ? "NoOp" : TiltAngle.ToString("0.00"))} }}";
    }
}