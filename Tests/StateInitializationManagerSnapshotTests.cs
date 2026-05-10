using HomeCompanion.Abstractions;
using HomeCompanion.Core.Persistence;
using HomeCompanion.Persistence;
using HomeCompanion.Values;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeCompanion.Tests;

[TestFixture]
public class StateInitializationManagerSnapshotTests
{
    [Test]
    public async Task SaveStateAndInitializeState_roundtrips_value_snapshot()
    {
        var store = new StubStateStore();
        var lifecycle = new StubLifecycleSync();

        var sourceContainer = new RoundtripContainer();
        sourceContainer.Counter.InitializeValue(42, AppInitializationStage.InitBusValueReceived);

        var saveManager = new StateInitializationManager(
            lifecycle,
            store,
            [sourceContainer],
            NullLogger<StateInitializationManager>.Instance,
            TimeProvider.System);

        await saveManager.SaveStateAsync(CancellationToken.None);

        Assert.That(store.Stored, Is.TypeOf<ValueSnapshotSet>());
        var snapshot = (ValueSnapshotSet)store.Stored!;
        Assert.That(snapshot.Values, Is.Not.Empty);

        var targetContainer = new RoundtripContainer();
        var loadManager = new StateInitializationManager(
            lifecycle,
            store,
            [targetContainer],
            NullLogger<StateInitializationManager>.Instance,
            TimeProvider.System);

        await loadManager.InitializeStateAsync(CancellationToken.None);

        Assert.That(targetContainer.Counter.Value, Is.EqualTo(42));
        Assert.That(targetContainer.Counter.Status.HasFlag(ValueStatus.Initialized), Is.True);
    }

    [Test]
    public async Task InitializeState_skips_stale_snapshot()
    {
        var store = new StubStateStore
        {
            Stored = new ValueSnapshotSet
            {
                Values =
                {
                    ["HomeCompanion.Tests.StateInitializationManagerSnapshotTests+RoundtripContainer|Counter"] = new ValueSnapshotEntry
                    {
                        Key = "HomeCompanion.Tests.StateInitializationManagerSnapshotTests+RoundtripContainer|Counter",
                        PayloadJson = "7",
                    },
                },
            },
            ForceIsSuccess = true,
            ForceIsRecent = false,
        };

        var manager = new StateInitializationManager(
            new StubLifecycleSync(),
            store,
            [new RoundtripContainer()],
            NullLogger<StateInitializationManager>.Instance,
            TimeProvider.System);

        var container = new RoundtripContainer();
        manager = new StateInitializationManager(
            new StubLifecycleSync(),
            store,
            [container],
            NullLogger<StateInitializationManager>.Instance,
            TimeProvider.System);

        await manager.InitializeStateAsync(CancellationToken.None);

        Assert.That(container.Counter.Value, Is.EqualTo(0));
        Assert.That(container.Counter.Status.HasFlag(ValueStatus.Initialized), Is.False);
    }

    [Test]
    public async Task InitializeState_falls_back_to_value_name_when_key_changed()
    {
        var store = new StubStateStore();
        var lifecycle = new StubLifecycleSync();

        var source = new SaveNameFallbackContainer();
        source.OldCounter.InitializeValue(99, AppInitializationStage.InitBusValueReceived);

        var saver = new StateInitializationManager(
            lifecycle,
            store,
            [source],
            NullLogger<StateInitializationManager>.Instance,
            TimeProvider.System);

        await saver.SaveStateAsync(CancellationToken.None);

        var target = new LoadNameFallbackContainer();
        var loader = new StateInitializationManager(
            lifecycle,
            store,
            [target],
            NullLogger<StateInitializationManager>.Instance,
            TimeProvider.System);

        await loader.InitializeStateAsync(CancellationToken.None);

        Assert.That(target.RenamedCounter.Value, Is.EqualTo(99));
    }

    [Test]
    public async Task SaveStateAndInitializeState_roundtrips_enum_datetimeoffset_nullable_and_custom_payload()
    {
        var store = new StubStateStore();
        var lifecycle = new StubLifecycleSync();

        var source = new MultiTypeRoundtripContainer();
        var expectedTimestamp = new DateTimeOffset(2026, 5, 9, 13, 45, 0, TimeSpan.FromHours(2));

        source.Mode.InitializeValue(ModeState.Online, AppInitializationStage.InitBusValueReceived);
        source.LastSeen.InitializeValue(expectedTimestamp, AppInitializationStage.InitBusValueReceived);
        source.OptionalLevel.InitializeValue(7, AppInitializationStage.InitBusValueReceived);
        source.SensorPayload.InitializeValue(
            new SensorReading { Value = 21.5, Unit = "C" },
            AppInitializationStage.InitBusValueReceived);

        var saver = new StateInitializationManager(
            lifecycle,
            store,
            [source],
            NullLogger<StateInitializationManager>.Instance,
            TimeProvider.System);

        await saver.SaveStateAsync(CancellationToken.None);

        var target = new MultiTypeRoundtripContainer();
        var loader = new StateInitializationManager(
            lifecycle,
            store,
            [target],
            NullLogger<StateInitializationManager>.Instance,
            TimeProvider.System);

        await loader.InitializeStateAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(target.Mode.Value, Is.EqualTo(ModeState.Online));
            Assert.That(target.LastSeen.Value, Is.EqualTo(expectedTimestamp));
            Assert.That(target.OptionalLevel.Value, Is.EqualTo(7));
            Assert.That(target.SensorPayload.Value, Is.Not.Null);
            Assert.That(target.SensorPayload.Value.Value, Is.EqualTo(21.5).Within(0.000001));
            Assert.That(target.SensorPayload.Value.Unit, Is.EqualTo("C"));
        });
    }

    [Test]
    public async Task SaveStateAndInitializeState_roundtrips_nullable_null_payload()
    {
        var store = new StubStateStore();
        var lifecycle = new StubLifecycleSync();

        var source = new NullableNullRoundtripContainer();
        source.OptionalLevel.InitializeValue((int?)null, AppInitializationStage.InitBusValueReceived);

        var saver = new StateInitializationManager(
            lifecycle,
            store,
            [source],
            NullLogger<StateInitializationManager>.Instance,
            TimeProvider.System);

        await saver.SaveStateAsync(CancellationToken.None);

        Assert.That(store.Stored, Is.TypeOf<ValueSnapshotSet>());
        var snapshot = (ValueSnapshotSet)store.Stored!;
        var key = "HomeCompanion.Tests.StateInitializationManagerSnapshotTests+NullableNullRoundtripContainer|OptionalLevel";
        Assert.That(snapshot.Values, Contains.Key(key));
        Assert.That(snapshot.Values[key].PayloadJson, Is.EqualTo("null"));

        var target = new NullableNullRoundtripContainer();
        var loader = new StateInitializationManager(
            lifecycle,
            store,
            [target],
            NullLogger<StateInitializationManager>.Instance,
            TimeProvider.System);

        await loader.InitializeStateAsync(CancellationToken.None);

        Assert.That(target.OptionalLevel.Value, Is.Null);
        Assert.That(target.OptionalLevel.Status.HasFlag(ValueStatus.Initialized), Is.True);
    }

    [Test]
    public async Task SaveState_serializes_enum_by_name_and_restore_accepts_name_payload()
    {
        var store = new StubStateStore();
        var lifecycle = new StubLifecycleSync();

        var source = new EnumRoundtripContainer();
        source.Mode.InitializeValue(ModeState.Online, AppInitializationStage.InitBusValueReceived);

        var saver = new StateInitializationManager(
            lifecycle,
            store,
            [source],
            NullLogger<StateInitializationManager>.Instance,
            TimeProvider.System);

        await saver.SaveStateAsync(CancellationToken.None);

        Assert.That(store.Stored, Is.TypeOf<ValueSnapshotSet>());
        var snapshot = (ValueSnapshotSet)store.Stored!;
        var key = "HomeCompanion.Tests.StateInitializationManagerSnapshotTests+EnumRoundtripContainer|Mode";
        Assert.That(snapshot.Values, Contains.Key(key));
        Assert.That(snapshot.Values[key].PayloadJson, Is.EqualTo("\"Online\""));

        // Ensure restore works with enum-name payload representation.
        var target = new EnumRoundtripContainer();
        var loader = new StateInitializationManager(
            lifecycle,
            store,
            [target],
            NullLogger<StateInitializationManager>.Instance,
            TimeProvider.System);

        await loader.InitializeStateAsync(CancellationToken.None);

        Assert.That(target.Mode.Value, Is.EqualTo(ModeState.Online));
    }

    private sealed class StubStateStore : IStateStore
    {
        public object? Stored { get; set; }
        public bool ForceIsSuccess { get; set; } = true;
        public bool ForceIsRecent { get; set; } = true;

        public Task<StateLoadingResult<T>> LoadAsync<T>(string stateSetName, TimeSpan maxAge) where T : class, new()
        {
            if (ForceIsSuccess && Stored is T typed)
            {
                return Task.FromResult(new StateLoadingResult<T>
                {
                    IsSuccess = true,
                    IsRecent = ForceIsRecent,
                    StateSet = typed,
                });
            }

            return Task.FromResult(new StateLoadingResult<T>
            {
                IsSuccess = false,
                IsRecent = false,
                StateSet = new T(),
            });
        }

        public Task<StateLoadingResult<T>> LoadAsync<T>(string stateSetName) where T : class, new()
            => LoadAsync<T>(stateSetName, TimeSpan.FromMinutes(30));

        public Task SaveAsync<T>(string stateSetName, T stateSet, CancellationToken cancellation) where T : class, new()
        {
            Stored = stateSet;
            return Task.CompletedTask;
        }

        public Task SaveAsync<T>(string stateSetName, T stateSet, int timeoutSeconds = 30) where T : class, new()
        {
            Stored = stateSet;
            return Task.CompletedTask;
        }
    }

    private sealed class StubLifecycleSync : IHomeCompanionLifeCycleSynchronization
    {
        public Task AwaitBusesConnectedAsync(TimeSpan timeout, CancellationToken token = default) => Task.CompletedTask;

        public Task WaitForInitializationStageCompletedAsync(AppInitializationStage level, TimeSpan timeout, CancellationToken token = default)
            => Task.CompletedTask;

        public Task SignalInitializationStageCompletedAsync(AppInitializationStage level) => Task.CompletedTask;

        public bool IsInitializationStageCompleted(AppInitializationStage level) => false;

        public bool IsAllUpToStageCompleted(AppInitializationStage level) => false;
    }

    private sealed class RoundtripContainer : IValuesContainer
    {
        public ValueBase<int> Counter { get; } = new(NullLogger<ValueBase<int>>.Instance)
        {
            Name = "roundtrip-counter",
        };

        public IEnumerable<IValue> GetValues() => [Counter];
    }

    private sealed class SaveNameFallbackContainer : IValuesContainer
    {
        public ValueBase<int> OldCounter { get; } = new(NullLogger<ValueBase<int>>.Instance)
        {
            Name = "stable-counter-name",
        };

        public IEnumerable<IValue> GetValues() => [OldCounter];
    }

    private sealed class LoadNameFallbackContainer : IValuesContainer
    {
        public ValueBase<int> RenamedCounter { get; } = new(NullLogger<ValueBase<int>>.Instance)
        {
            Name = "stable-counter-name",
        };

        public IEnumerable<IValue> GetValues() => [RenamedCounter];
    }

    private sealed class MultiTypeRoundtripContainer : IValuesContainer
    {
        public ValueBase<ModeState> Mode { get; } = new(NullLogger<ValueBase<ModeState>>.Instance)
        {
            Name = "mode-state",
        };

        public ValueBase<DateTimeOffset> LastSeen { get; } = new(NullLogger<ValueBase<DateTimeOffset>>.Instance)
        {
            Name = "last-seen",
        };

        public ValueBase<int?> OptionalLevel { get; } = new(NullLogger<ValueBase<int?>>.Instance)
        {
            Name = "optional-level",
        };

        public ValueBase<SensorReading> SensorPayload { get; } = new(NullLogger<ValueBase<SensorReading>>.Instance)
        {
            Name = "sensor-payload",
        };

        public IEnumerable<IValue> GetValues() => [Mode, LastSeen, OptionalLevel, SensorPayload];
    }

    private sealed class NullableNullRoundtripContainer : IValuesContainer
    {
        public ValueBase<int?> OptionalLevel { get; } = new(NullLogger<ValueBase<int?>>.Instance)
        {
            Name = "optional-level-null",
        };

        public IEnumerable<IValue> GetValues() => [OptionalLevel];
    }

    private sealed class EnumRoundtripContainer : IValuesContainer
    {
        public ValueBase<ModeState> Mode { get; } = new(NullLogger<ValueBase<ModeState>>.Instance)
        {
            Name = "enum-mode",
        };

        public IEnumerable<IValue> GetValues() => [Mode];
    }

    private enum ModeState
    {
        Offline = 0,
        Online = 1,
    }

    private sealed class SensorReading
    {
        public double Value { get; set; }
        public string Unit { get; set; } = string.Empty;
    }
}
