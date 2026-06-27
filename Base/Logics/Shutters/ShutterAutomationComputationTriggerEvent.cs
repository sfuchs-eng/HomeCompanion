namespace HomeCompanion.Logics.Shutters;

public class ShutterAutomationComputationTriggerEvent : HomeCompanionEvent
{
    public required ShutterAutomationComputationTriggerContext Context { get; init; }
}
