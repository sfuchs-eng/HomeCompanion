namespace HomeCompanion.Logics.Shutters;

public class ShutterAutomationComputationTriggerEvent : HomeCompanionEvent
{
    public required ShutterAutomationComputationTriggerContext Context { get; init; }

    public ShutterAutomationComputationTriggerEvent() : base()
    {
    }

    public ShutterAutomationComputationTriggerEvent(
        ShutterAutomationComputationTriggerContext context
    ) : base()
    {
        Context = context;
        Timestamp = context.Timestamp;
    }

    public override string ToString()
    {
        return $"ShutterAutomationComputationTriggerEvent: Context={Context}";
    }
}
