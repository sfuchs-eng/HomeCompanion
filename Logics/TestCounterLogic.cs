using HomeCompanion;
using HomeCompanion.Events;
using HomeCompanion.Values;
using HomeCompanion.Integrations.Knx;
using Microsoft.Extensions.Logging;
using HomeCompanion.Logics;

namespace HomeCompanion.Logics;

/// <summary>
/// Minimal test logic for full stack integration testing of KNX → <see cref="IValue.Changed"/> → logic reaction.:
/// The logic tracks on-duration of a boolean switch, counts off-transitions, and writes results to dedicated values.
/// The switch and result data points reside in the actual KNX system with real group addresses, and are accessed via the generated <see cref="KnxValues"/> class.
/// This allows testing the full path from KNX message reception to <see cref="IValue.Changed"/> event triggering and logic reaction.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><description>Rising edge (false → true): records the current time.</description></item>
///   <item><description>Falling edge (true → false): computes on-duration, writes it in seconds to
///   <see cref="KnxValues"/>, and increments <see cref="KnxValues.TestCount"/> by 1.</description></item>
/// </list>
/// <para>Serves as an end-to-end proof that the path KNX → <see cref="IValue.Changed"/> → logic reaction works.</para>
/// </remarks>
/// <remarks>
/// Initializes a new <see cref="TestCounterLogic"/>.
/// </remarks>
public class TestCounterLogic(
    KnxValues values,
    IEventPublisher publisher,
    IEventSubscriber subscriber,
    TimeProvider timeProvider,
    ILogger<TestCounterLogic> logger) : LogicBase(publisher, subscriber)
{
    private readonly KnxValues _values = values;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<TestCounterLogic> _logger = logger;
    private DateTimeOffset? _switchOnAt;

    /// <inheritdoc/>
    protected override Task InitializeAsyncLatched(CancellationToken cancellationToken = default)
    {
        _values.TestSwitch.Changed += OnTestSwitchChanged;
        return Task.CompletedTask;
    }

    private void OnTestSwitchChanged(object? sender, ValueChangedEventArgs e)
    {
        _logger.LogTrace("Received TestSwitch change event. New value: {NewValue}, Status: {Status}", _values.TestSwitch.Value, _values.TestSwitch.Status);
        if (_values.TestSwitch.Value && _values.TestSwitch.Status.HasFlag(ValueStatus.Initialized))
        {
            _logger.LogInformation("Test switch turned ON. Recording start time.");
            // Rising edge: record start time
            _switchOnAt = _timeProvider.GetUtcNow();
        }
        else
        {
            // Falling edge: compute duration, write results
            if (_switchOnAt.HasValue)
            {
                _logger.LogInformation("Test switch turned OFF. Computing duration and updating values.");
                float duration = (float)(_timeProvider.GetUtcNow() - _switchOnAt.Value).TotalSeconds;
                _switchOnAt = null;
                _values.TestCounter.Write((byte)(_values.TestCounter.Value + 1), this);
                _values.TestValueFloat.Write(duration, this);
                _values.TestValueInt32.Write(_values.TestValueInt32.Value + 1, this);
            }
        }
    }
}
