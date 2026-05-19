using HomeCompanion.Abstractions;
using HomeCompanion.Core;
using HomeCompanion.Core.Events;
using HomeCompanion.Events;
using HomeCompanion.Values;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeCompanion.Tests;

[TestFixture]
public class ValueBaseTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ValueBase<T> CreateValue<T>(TimeProvider? timeProvider = null)
        => new(NullLoggerFactory.Instance.CreateLogger<ValueBase<T>>(), timeProvider);

    private static EventBus CreateBus() => new(NullLogger<EventBus>.Instance);

    private static async Task RunWithBusAsync(EventBus bus, Func<Task> action, int drainMs = 150)
    {
        using var cts = new CancellationTokenSource();
        await bus.StartAsync(cts.Token);
        await action();
        await Task.Delay(drainMs);
        await cts.CancelAsync();
        try { await bus.StopAsync(CancellationToken.None); } catch (OperationCanceledException) { }
    }

    private sealed class LambdaHandler<T>(Action<T> action) : IEventHandler<T> where T : IEvent
    {
        public ValueTask HandleAsync(T @event, CancellationToken cancellationToken = default)
        {
            action(@event);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubValuesManager : IValuesManager
    {
        public void RegisterValue(IValue value) { }
        public void UnregisterValue(IValue value) { }
    }

    private sealed class NullEventPublisher : IEventPublisher
    {
        public ValueTask PublishAsync(IEvent @event, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }

    // ── Tests: Write() ────────────────────────────────────────────────────────

    [Test]
    public async Task Write_PublishesValueWriteRequest()
    {
        var bus = CreateBus();
        var value = CreateValue<bool>();
        value.Initialize(bus, new StubValuesManager());

        ValueWriteRequest? received = null;
        bus.Subscribe(new LambdaHandler<ValueWriteRequest>(e => received = e));

        await RunWithBusAsync(bus, async () =>
        {
            value.Write(true);
            await Task.Delay(50);
        });

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Source, Is.SameAs(value));
        Assert.That(received.NewValue, Is.EqualTo(true));
    }

    [Test]
    public async Task Write_PublishesValueWritten()
    {
        var bus = CreateBus();
        var value = CreateValue<bool>();
        value.Initialize(bus, new StubValuesManager());

        ValueWritten? received = null;
        bus.Subscribe(new LambdaHandler<ValueWritten>(e => received = e));

        await RunWithBusAsync(bus, async () =>
        {
            value.Write(true);
            await Task.Delay(50);
        });

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Source, Is.SameAs(value));
    }

    [Test]
    public void Write_RaisesWrittenEvent()
    {
        var value = CreateValue<bool>();
        value.Initialize(new NullEventPublisher(), new StubValuesManager());

        ValueWrittenEventArgs? args = null;
        value.Written += (_, e) => args = e;

        value.Write(true);

        Assert.That(args, Is.Not.Null);
        Assert.That(args!.NewValue, Is.SameAs(value));
    }

    [Test]
    public void Write_WhenValueChanges_RaisesChangedEvent()
    {
        var value = CreateValue<bool>();
        value.Initialize(new NullEventPublisher(), new StubValuesManager());

        ValueChangedEventArgs? args = null;
        value.Changed += (_, e) => args = e;

        value.Write(true);

        Assert.That(args, Is.Not.Null);
    }

    [Test]
    public void Write_WhenValueUnchanged_DoesNotRaiseChangedEvent()
    {
        var value = CreateValue<bool>();
        value.Initialize(new NullEventPublisher(), new StubValuesManager());
        value.Write(true); // first write — establishes the value

        ValueChangedEventArgs? args = null;
        value.Changed += (_, e) => args = e;

        value.Write(true); // same value again

        Assert.That(args, Is.Null);
    }

    [Test]
    public void Write_UpdatesStoredValueAndSetsStatus()
    {
        var value = CreateValue<int>();
        value.Initialize(new NullEventPublisher(), new StubValuesManager());

        value.Write(42);

        Assert.Multiple(() =>
        {
            Assert.That(value.Value, Is.EqualTo(42));
            Assert.That(value.Status.HasFlag(ValueStatus.Initialized), Is.True);
            Assert.That(value.Status.HasFlag(ValueStatus.Used), Is.True);
        });
    }

    // ── Tests: ReceiveUpdate ─────────────────────────────────────────────────

    [Test]
    public void ReceiveUpdate_UpdatesStoredValueAndStatus()
    {
        var value = CreateValue<int>();
        value.Initialize(new NullEventPublisher(), new StubValuesManager());

        ((IValueEventReceiver)value).ReceiveUpdate(99);

        Assert.Multiple(() =>
        {
            Assert.That(value.Value, Is.EqualTo(99));
            Assert.That(value.Status.HasFlag(ValueStatus.Initialized), Is.True);
            Assert.That(value.Status.HasFlag(ValueStatus.Live), Is.True);
        });
    }

    [Test]
    public void ReceiveUpdate_WhenValueChanges_RaisesChangedEvent()
    {
        var value = CreateValue<int>();
        value.Initialize(new NullEventPublisher(), new StubValuesManager());

        ValueChangedEventArgs? args = null;
        value.Changed += (_, e) => args = e;

        ((IValueEventReceiver)value).ReceiveUpdate(42);

        Assert.That(args, Is.Not.Null);
    }

    [Test]
    public void ReceiveUpdate_OnFirstCall_AlwaysRaisesChangedEvent()
    {
        // Even when updating to the default value (false), the first update raises Changed.
        var value = CreateValue<bool>();
        value.Initialize(new NullEventPublisher(), new StubValuesManager());

        int changedCount = 0;
        value.Changed += (_, _) => changedCount++;

        ((IValueEventReceiver)value).ReceiveUpdate(false); // same as default, but first receive

        Assert.That(changedCount, Is.EqualTo(1));
    }

    [Test]
    public void ReceiveUpdate_SubsequentCall_SameValue_DoesNotRaiseChangedEvent()
    {
        var value = CreateValue<bool>();
        value.Initialize(new NullEventPublisher(), new StubValuesManager());
        ((IValueEventReceiver)value).ReceiveUpdate(false); // first receive

        int changedCount = 0;
        value.Changed += (_, _) => changedCount++;

        ((IValueEventReceiver)value).ReceiveUpdate(false); // same value again

        Assert.That(changedCount, Is.EqualTo(0));
    }

    [Test]
    public void ReceiveUpdate_WithIncorrectType_SetsErrorStatus()
    {
        var value = CreateValue<bool>();
        value.Initialize(new NullEventPublisher(), new StubValuesManager());

        ((IValueEventReceiver)value).ReceiveUpdate("not a bool");

        Assert.That(value.Status.HasFlag(ValueStatus.Error), Is.True);
    }

    [Test]
    public void ReceiveUpdate_WithNull_SetsErrorStatus()
    {
        var value = CreateValue<bool>();
        value.Initialize(new NullEventPublisher(), new StubValuesManager());

        ((IValueEventReceiver)value).ReceiveUpdate(null);

        Assert.That(value.Status.HasFlag(ValueStatus.Error), Is.True);
    }

    // ── Tests: ReceiveWrite ──────────────────────────────────────────────────

    [Test]
    public void ReceiveWrite_UpdatesValueAndSetsLiveStatus()
    {
        var value = CreateValue<int>();
        value.Initialize(new NullEventPublisher(), new StubValuesManager());

        ((IValueEventReceiver)value).ReceiveWrite(55);

        Assert.Multiple(() =>
        {
            Assert.That(value.Value, Is.EqualTo(55));
            Assert.That(value.Status.HasFlag(ValueStatus.Live), Is.True);
        });
    }

    [Test]
    public void ReceiveWrite_WhenValueChanges_RaisesWrittenAndChangedEvents()
    {
        var value = CreateValue<int>();
        value.Initialize(new NullEventPublisher(), new StubValuesManager());

        bool writtenRaised = false;
        bool changedRaised = false;
        value.Written += (_, _) => writtenRaised = true;
        value.Changed += (_, _) => changedRaised = true;

        ((IValueEventReceiver)value).ReceiveWrite(77);

        Assert.Multiple(() =>
        {
            Assert.That(writtenRaised, Is.True);
            Assert.That(changedRaised, Is.True);
        });
    }

    [Test]
    public void ReceiveWrite_WithNull_SetsErrorStatus()
    {
        var value = CreateValue<bool>();
        value.Initialize(new NullEventPublisher(), new StubValuesManager());

        ((IValueEventReceiver)value).ReceiveWrite(null);

        Assert.That(value.Status.HasFlag(ValueStatus.Error), Is.True);
    }

    // ── Tests: InitializeValue ───────────────────────────────────────────────

    [Test]
    public void InitializeValue_AcceptsDirectTypeMatch()
    {
        var value = CreateValue<int>();

        var result = value.InitializeValue(42, AppInitializationStage.InitLoadFromStore);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(value.Value, Is.EqualTo(42));
            Assert.That(value.Status.HasFlag(ValueStatus.Initialized), Is.True);
        });
    }

    [Test]
    public void InitializeValue_AcceptsCompatibleStringConversion()
    {
        var value = CreateValue<bool>();

        var result = value.InitializeValue("True", AppInitializationStage.InitLoadFromStore);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(value.Value, Is.True);
        });
    }

    [Test]
    public void InitializeValue_AcceptsIntStringForInt()
    {
        var value = CreateValue<int>();

        var result = value.InitializeValue("123", AppInitializationStage.InitLoadFromStore);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(value.Value, Is.EqualTo(123));
        });
    }

    [Test]
    public void InitializeValue_RejectsStageDowngrade()
    {
        var value = CreateValue<int>();
        value.InitializeValue(1, AppInitializationStage.InitRetrieveFromEnvironment);

        // Try to initialize with a lower stage — should be rejected
        var result = value.InitializeValue(2, AppInitializationStage.InitLoadFromStore);

        Assert.That(result, Is.False);
        Assert.That(value.Value, Is.EqualTo(1)); // unchanged
    }

    [Test]
    public void InitializeValue_AllowsStageUpgrade()
    {
        var value = CreateValue<int>();
        value.InitializeValue(1, AppInitializationStage.InitLoadFromStore);

        var result = value.InitializeValue(2, AppInitializationStage.InitRetrieveFromEnvironment);

        Assert.That(result, Is.True);
        Assert.That(value.Value, Is.EqualTo(2));
    }

    [Test]
    public void InitializeValue_DoesNotSetLiveOrUsedStatus()
    {
        var value = CreateValue<int>();

        value.InitializeValue(5, AppInitializationStage.InitLoadFromStore);

        Assert.Multiple(() =>
        {
            Assert.That(value.Status.HasFlag(ValueStatus.Initialized), Is.True);
            Assert.That(value.Status.HasFlag(ValueStatus.Live), Is.False);
            Assert.That(value.Status.HasFlag(ValueStatus.Used), Is.False);
        });
    }

    // ── Tests: event handler isolation ───────────────────────────────────────

    [Test]
    public void Written_ExceptionInOneHandler_DoesNotBlockSubsequentHandlers()
    {
        var value = CreateValue<bool>();
        value.Initialize(new NullEventPublisher(), new StubValuesManager());

        bool secondHandlerCalled = false;
        value.Written += (_, _) => throw new InvalidOperationException("boom");
        value.Written += (_, _) => secondHandlerCalled = true;

        Assert.DoesNotThrow(() => value.Write(true));
        Assert.That(secondHandlerCalled, Is.True);
    }

    [Test]
    public void Changed_ExceptionInOneHandler_DoesNotBlockSubsequentHandlers()
    {
        var value = CreateValue<bool>();
        value.Initialize(new NullEventPublisher(), new StubValuesManager());

        bool secondHandlerCalled = false;
        value.Changed += (_, _) => throw new InvalidOperationException("boom");
        value.Changed += (_, _) => secondHandlerCalled = true;

        Assert.DoesNotThrow(() => value.Write(true));
        Assert.That(secondHandlerCalled, Is.True);
    }
}
