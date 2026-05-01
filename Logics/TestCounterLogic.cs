using HomeCompanion.Abstractions;
using HomeCompanion.Base;
using HomeCompanion.Base.Values;
using HomeCompanion.Knx;

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
public class TestCounterLogic : LogicBase
{
    private readonly KnxValues _values;
    private readonly TimeProvider _timeProvider;
    private DateTimeOffset? _switchOnAt;

    /// <summary>
    /// Initializes a new <see cref="TestCounterLogic"/>.
    /// </summary>
    public TestCounterLogic(
        KnxValues values,
        IEventPublisher publisher,
        IEventSubscriber subscriber,
        TimeProvider timeProvider)
        : base(publisher, subscriber)
    {
        _values = values;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    protected override Task InitializeAsyncLatched(CancellationToken cancellationToken = default)
    {
        //_values.TestSwitch.Changed += OnTestSwitchChanged;
        return Task.CompletedTask;
    }

    private void OnTestSwitchChanged(object? sender, ValueChangedEventArgs e)
    {
        _values.WantToSeeTestCounterHereWithCodeCompletion
        /*
        if (_values.TestSwitch.Value)
        {
            // Rising edge: record start time
            _switchOnAt = _timeProvider.GetUtcNow();
        }*/
        //else
        {
            // Falling edge: compute duration, write results
            if (_switchOnAt.HasValue)
            {
                var duration = (_timeProvider.GetUtcNow() - _switchOnAt.Value).TotalSeconds;
                _switchOnAt = null;
            //    _values.TestDuration.Write(duration);
            //    _values.TestCount.Write(_values.TestCount.Value + 1);
            }
        }
    }
}
