using HomeCompanion.Events;

namespace HomeCompanion.Values;

public interface IValueInitializationEvent : IEvent
{
    /// <summary>
    /// The stage of initialization to execute.
    /// </summary>
    ValuesInitializationStage Stage { get; }
}
