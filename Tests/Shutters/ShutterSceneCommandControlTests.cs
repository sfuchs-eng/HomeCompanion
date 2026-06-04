using HomeCompanion.Base.Logics.Shutters;
using HomeCompanion.Base.Model;
using HomeCompanion.Events;
using HomeCompanion.Persistence;
using HomeCompanion.Values;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeCompanion.Tests.Shutters;

[TestFixture]
public class ShutterSceneCommandControlTests
{
    [Test]
    public async Task ManualSceneController_ExecutesCommands_AndSetsManualOverride()
    {
        var fixture = TestFixtureRuntime.Create();

        await fixture.Logic.InitializeAsync();
        fixture.RoomScene.Write((byte)20, this);
        await fixture.WaitUntilAsync(() => fixture.TargetPosition.Value == 77);

        Assert.That(fixture.TargetPosition.Value, Is.EqualTo(77));
        Assert.That(fixture.StateStore.Stored?.RoomOverrides.ContainsKey(fixture.RoomKey), Is.True);
    }

    [Test]
    public async Task ResumeAutomationScene_ClearsManualOverride()
    {
        var fixture = TestFixtureRuntime.Create();

        await fixture.Logic.InitializeAsync();
        fixture.RoomScene.Write((byte)2, this);
        await fixture.WaitUntilAsync(() => fixture.StateStore.Stored?.RoomOverrides.ContainsKey(fixture.RoomKey) == true);

        fixture.RoomScene.Write((byte)50, this);
        await fixture.WaitUntilAsync(() => fixture.StateStore.Stored?.RoomOverrides.ContainsKey(fixture.RoomKey) == false);

        Assert.That(fixture.StateStore.Stored?.RoomOverrides.ContainsKey(fixture.RoomKey), Is.False);
    }

    [Test]
    public async Task ConfiguredResumeAutomationScene_ClearsManualOverride()
    {
        var fixture = TestFixtureRuntime.Create(resumeAutomationScenes: [60]);

        await fixture.Logic.InitializeAsync();
        fixture.RoomScene.Write((byte)2, this);
        await fixture.WaitUntilAsync(() => fixture.StateStore.Stored?.RoomOverrides.ContainsKey(fixture.RoomKey) == true);

        fixture.RoomScene.Write((byte)60, this);
        await fixture.WaitUntilAsync(() => fixture.StateStore.Stored?.RoomOverrides.ContainsKey(fixture.RoomKey) == false);

        Assert.That(fixture.StateStore.Stored?.RoomOverrides.ContainsKey(fixture.RoomKey), Is.False);
    }

    [Test]
    public async Task ScheduleDue_ExecutesCommands_OnlyWhenInAutomationScene()
    {
        var fixture = TestFixtureRuntime.Create();

        await fixture.Logic.InitializeAsync();

        fixture.RoomScene.Write((byte)2, this);
        await fixture.Subscriber.PublishAsync(new RoomSceneWriteRequestedEvent
        {
            RoomKey = fixture.RoomKey,
            ScheduleKey = "NightClose",
            Scene = 20,
            TriggerLocalTime = new DateTime(2026, 6, 3, 22, 0, 0),
            Timestamp = DateTimeOffset.UtcNow,
        });

        await Task.Delay(50);
        Assert.That(fixture.TargetPosition.Value, Is.EqualTo(0));

        fixture.RoomScene.Write((byte)52, this);
        await fixture.Subscriber.PublishAsync(new RoomSceneWriteRequestedEvent
        {
            RoomKey = fixture.RoomKey,
            ScheduleKey = "NightClose",
            Scene = 20,
            TriggerLocalTime = new DateTime(2026, 6, 3, 22, 5, 0),
            Timestamp = DateTimeOffset.UtcNow,
        });

        await fixture.WaitUntilAsync(() => fixture.TargetPosition.Value == 77);
        Assert.That(fixture.TargetPosition.Value, Is.EqualTo(77));
        Assert.That(fixture.RoomScene.Value, Is.EqualTo((byte)20));
    }

    [Test]
    public async Task ScheduleDue_AutoResumeAfter_ResumesWhenSunExposed()
    {
        var fixture = TestFixtureRuntime.Create();

        await fixture.Logic.InitializeAsync();
        fixture.RoomScene.Write((byte)52, this);

        await fixture.Subscriber.PublishAsync(new RoomSceneWriteRequestedEvent
        {
            RoomKey = fixture.RoomKey,
            ScheduleKey = "Shadowing",
            Scene = 20,
            ResumeAutomationAfter = TimeSpan.FromMilliseconds(100),
            ResumeAutomationScene = 52,
            TriggerLocalTime = new DateTime(2026, 6, 3, 15, 0, 0),
            Timestamp = DateTimeOffset.UtcNow,
        });

        await fixture.WaitUntilAsync(() => fixture.RoomScene.Value == 52, timeoutMs: 2500);
        await fixture.WaitUntilAsync(() => fixture.StateStore.Stored?.RoomOverrides.ContainsKey(fixture.RoomKey) == false, timeoutMs: 2500);

        Assert.That(fixture.RoomScene.Value, Is.EqualTo((byte)52));
    }

    [Test]
    public async Task ScheduleDue_AutoResumeAfter_DoesNotResume_WhenSunNotExposed()
    {
        var fixture = TestFixtureRuntime.Create();

        await fixture.Logic.InitializeAsync();
        fixture.RoomScene.Write((byte)52, this);

        // No sun exposure (below minimum elevation) must keep manual scene active.
        fixture.SunElevation.Write(0f, this);

        await fixture.Subscriber.PublishAsync(new RoomSceneWriteRequestedEvent
        {
            RoomKey = fixture.RoomKey,
            ScheduleKey = "NightClose",
            Scene = 20,
            ResumeAutomationAfter = TimeSpan.FromMilliseconds(100),
            ResumeAutomationScene = 52,
            TriggerLocalTime = new DateTime(2026, 6, 3, 22, 0, 0),
            Timestamp = DateTimeOffset.UtcNow,
        });

        await Task.Delay(300);

        Assert.That(fixture.RoomScene.Value, Is.EqualTo((byte)20));
        Assert.That(fixture.StateStore.Stored?.RoomOverrides.ContainsKey(fixture.RoomKey), Is.True);
    }

    [Test]
    public async Task ManualSceneController_VenetianBlind_WritesPositionAndAngle()
    {
        var fixture = TestFixtureRuntime.Create(ShutterType.VenetianBlind, includeAngleTarget: true);

        await fixture.Logic.InitializeAsync();
        fixture.RoomScene.Write((byte)20, this);

        await fixture.WaitUntilAsync(() => fixture.TargetPosition.Value == 77);
        await fixture.WaitUntilAsync(() => fixture.TargetAngle!.Value == 45);

        Assert.That(fixture.TargetPosition.Value, Is.EqualTo(77));
        Assert.That(fixture.TargetAngle!.Value, Is.EqualTo(45));
    }

    [Test]
    public async Task ManualSceneController_OpenCloseShutter_WritesClosedState()
    {
        var fixture = TestFixtureRuntime.Create(ShutterType.OpenClose, includeOpenCloseTarget: true);

        await fixture.Logic.InitializeAsync();
        fixture.RoomScene.Write((byte)20, this);

        await fixture.WaitUntilAsync(() => fixture.TargetOpenClose!.Value);

        Assert.That(fixture.TargetOpenClose!.Value, Is.True);
    }

    [Test]
    public async Task ScheduleDue_ManualScene_RespectsRoomCutoverOverride_PerShutter()
    {
        var fixture = TestFixtureRuntime.Create();
        fixture.RoomConfig.FacadeSunCutoverAngleOverride = 75;

        await fixture.Logic.InitializeAsync();
        fixture.RoomScene.Write((byte)52, this);

        // Scheduled transitions are now gated per shutter, so a strict room cutover override blocks the command.
        fixture.SunAzimuth.Write(189f, this);
        fixture.SunElevation.Write(20f, this);

        await fixture.Subscriber.PublishAsync(new RoomSceneWriteRequestedEvent
        {
            RoomKey = fixture.RoomKey,
            ScheduleKey = "Shadowing",
            Scene = 20,
            TriggerLocalTime = new DateTime(2026, 6, 3, 15, 0, 0),
            Timestamp = DateTimeOffset.UtcNow,
        });

        await Task.Delay(250);
        Assert.That(fixture.TargetPosition.Value, Is.EqualTo(0));
    }

    [Test]
    public async Task ScheduleDue_ManualScene_IgnoresDynamicCutoverRule_ByThermalModeAndTemperature()
    {
        var fixture = TestFixtureRuntime.Create();
        fixture.ShadowingConfig.DynamicFacadeSunCutoverRules =
        [
            new CfgDynamicCutoverAngleRule
            {
                ThermalControlMode = ThermalControlMode.CoolingPriority,
                OutdoorTemperatureMin = 28,
                CutoverAngle = 70,
            },
        ];

        await fixture.Logic.InitializeAsync();
        fixture.RoomScene.Write((byte)52, this);

        fixture.ThermalMode.Write(2f, this); // CoolingPriority in 0-based encoding
        fixture.OutdoorTemperature.Write(30f, this);

        // Scheduled transitions now apply room scene/manual semantics, so command execution is not
        // gated by dynamic cutover checks in this path.
        fixture.SunAzimuth.Write(189f, this);
        fixture.SunElevation.Write(20f, this);

        await fixture.Subscriber.PublishAsync(new RoomSceneWriteRequestedEvent
        {
            RoomKey = fixture.RoomKey,
            ScheduleKey = "Shadowing",
            Scene = 20,
            TriggerLocalTime = new DateTime(2026, 6, 3, 15, 0, 0),
            Timestamp = DateTimeOffset.UtcNow,
        });

        await fixture.WaitUntilAsync(() => fixture.TargetPosition.Value == 77);
        Assert.That(fixture.TargetPosition.Value, Is.EqualTo(77));
    }

    [Test]
    public async Task ScheduleDue_InvalidTarget_DoesNotBlockValidCommands()
    {
        var fixture = TestFixtureRuntime.Create();

        fixture.ShadowingConfig.SpecialScenes["Manual20"].Commands["InvalidTarget"] = new CfgShadowingSceneCommand
        {
            TargetValueReference = "DoesNotExist",
            Value = 12,
        };

        await fixture.Logic.InitializeAsync();
        fixture.RoomScene.Write((byte)52, this);

        await fixture.Subscriber.PublishAsync(new RoomSceneWriteRequestedEvent
        {
            RoomKey = fixture.RoomKey,
            ScheduleKey = "MixedTargets",
            Scene = 20,
            TriggerLocalTime = new DateTime(2026, 6, 4, 14, 0, 0),
            Timestamp = DateTimeOffset.UtcNow,
        });

        await fixture.WaitUntilAsync(() => fixture.TargetPosition.Value == 77);
        Assert.That(fixture.TargetPosition.Value, Is.EqualTo(77));
        Assert.That(fixture.RoomScene.Value, Is.EqualTo((byte)20));
    }

    [Test]
    public async Task Startup_RestoresPersistedOverrides_PrunesExpired_NoCatchUpMovement()
    {
        var now = DateTimeOffset.UtcNow;
        var preloaded = new ShutterManualOverrideStateSet
        {
            RoomOverrides = new Dictionary<string, ShutterManualOverrideEntry>(StringComparer.OrdinalIgnoreCase)
            {
                ["Main/Ground/Living"] = new()
                {
                    CreatedAtUtc = now.AddMinutes(-5),
                    ExpiresAtUtc = now.AddHours(1),
                },
                ["Main/Ground/Expired"] = new()
                {
                    CreatedAtUtc = now.AddHours(-2),
                    ExpiresAtUtc = now.AddMinutes(-1),
                },
            },
        };

        var fixture = TestFixtureRuntime.Create(preloadedState: preloaded);

        await fixture.Logic.InitializeAsync();
        await fixture.WaitUntilAsync(() => fixture.StateStore.Stored is not null);

        Assert.That(fixture.StateStore.Stored!.RoomOverrides.ContainsKey(fixture.RoomKey), Is.True);
        Assert.That(fixture.StateStore.Stored.RoomOverrides.ContainsKey("Main/Ground/Expired"), Is.False);
        Assert.That(fixture.TargetPosition.Value, Is.EqualTo(0));
    }

    [Test]
    public async Task SceneWriteRace_UserActionsOrdered_DeterministicFinalState()
    {
        var fixture = TestFixtureRuntime.Create();

        await fixture.Logic.InitializeAsync();

        fixture.RoomScene.Write((byte)2, this);
        await fixture.WaitUntilAsync(() => fixture.StateStore.Stored?.RoomOverrides.ContainsKey(fixture.RoomKey) == true);

        fixture.RoomScene.Write((byte)52, this);
        await fixture.WaitUntilAsync(() => fixture.StateStore.Stored?.RoomOverrides.ContainsKey(fixture.RoomKey) == false);

        Assert.That(fixture.RoomScene.Value, Is.EqualTo((byte)52));
        Assert.That(fixture.StateStore.Stored?.RoomOverrides.ContainsKey(fixture.RoomKey), Is.False);
    }

    private sealed class TestFixtureRuntime
    {
        public required ShutterControl OverrideOwner { get; init; }
        public required ShutterSceneCommandControl Logic { get; init; }
        public required ValueBase<byte> RoomScene { get; init; }
        public required ValueBase<byte> TargetPosition { get; init; }
        public ValueBase<byte>? TargetAngle { get; init; }
        public ValueBase<bool>? TargetOpenClose { get; init; }
        public required ValueBase<float> SunAzimuth { get; init; }
        public required ValueBase<float> SunElevation { get; init; }
        public required ValueBase<float> OutdoorTemperature { get; init; }
        public required ValueBase<float> ThermalMode { get; init; }
        public required StubStateStore StateStore { get; init; }
        public required StubSubscriber Subscriber { get; init; }
        public required CfgRoom RoomConfig { get; init; }
        public required CfgShadowingSpecial ShadowingConfig { get; init; }
        public required string RoomKey { get; init; }

        public static TestFixtureRuntime Create(
            ShutterType shutterType = ShutterType.Positional,
            bool includeAngleTarget = false,
            bool includeOpenCloseTarget = false,
            IEnumerable<int>? resumeAutomationScenes = null,
            ShutterManualOverrideStateSet? preloadedState = null)
        {
            var roomScene = new ValueBase<byte>(NullLogger<ValueBase<byte>>.Instance) { Name = "RoomScene" };
            var targetPosition = new ValueBase<byte>(NullLogger<ValueBase<byte>>.Instance) { Name = "TargetPosition" };
            var targetAngle = includeAngleTarget ? new ValueBase<byte>(NullLogger<ValueBase<byte>>.Instance) { Name = "TargetAngle" } : null;
            var targetOpenClose = includeOpenCloseTarget ? new ValueBase<bool>(NullLogger<ValueBase<bool>>.Instance) { Name = "TargetOpenClose" } : null;
            var sunAzimuth = new ValueBase<float>(NullLogger<ValueBase<float>>.Instance) { Name = "SunAzimuth" };
            var sunElevation = new ValueBase<float>(NullLogger<ValueBase<float>>.Instance) { Name = "SunElevation" };
            var outdoorTemperature = new ValueBase<float>(NullLogger<ValueBase<float>>.Instance) { Name = "OutdoorTemperature" };
            var thermalMode = new ValueBase<float>(NullLogger<ValueBase<float>>.Instance) { Name = "ThermalMode" };

            var seedSource = new object();
            sunAzimuth.Write(129f, seedSource);
            sunElevation.Write(25f, seedSource);
            outdoorTemperature.Write(24f, seedSource);
            thermalMode.Write(1f, seedSource);

            var roomConfig = new CfgRoom
            {
                Shutters =
                {
                    ["MainShutter"] = new CfgShutter
                    {
                        Type = shutterType,
                        FacadeReference = "SE",
                        PositionValueReference = "TargetPosition",
                        AngleValueReference = includeAngleTarget ? "TargetAngle" : null,
                        OpenCloseReference = includeOpenCloseTarget ? "TargetOpenClose" : null,
                    },
                },
            };

            var room = new Room("Living", roomConfig)
            {
                ShutterScene = roomScene,
                Shutters = new Dictionary<string, Shutter>
                {
                    ["MainShutter"] = new("MainShutter", roomConfig.Shutters["MainShutter"]),
                },
            };

            var floor = new Floor
            {
                Name = "Ground",
                Rooms = new Dictionary<string, Room>
                {
                    [room.Name] = room,
                },
            };

            var shadowCfg = new CfgShadowingSpecial
            {
                ResumeAutomationScenes = resumeAutomationScenes?.ToList() ?? [50, 52],
                SpecialScenes =
                {
                    ["Manual20"] = new CfgShadowingSceneController
                    {
                        RoomReference = "Main/Ground/Living",
                        Number = 20,
                        Commands =
                        {
                            ["SetPosition"] = new CfgShadowingSceneCommand
                            {
                                TargetValueReference = "TargetPosition",
                                Value = 77,
                            },
                        },
                    },
                },
            };

            var building = new Building
            {
                Name = "Main",
                Facades = new Dictionary<string, Facade>
                {
                    ["SE"] = new("SE", new CfgFacade { Azimuth = 129, Elevation = 0 }),
                    ["NW"] = new("NW", new CfgFacade { Azimuth = 309, Elevation = 0 }),
                },
                Floors = new Dictionary<string, Floor>
                {
                    [floor.Name] = floor,
                },
                Specials = new Dictionary<string, Special>
                {
                    ["Shadowing"] = new ShadowingSpecial("Shadowing", shadowCfg)
                    {
                        SunPositionAzimuth = sunAzimuth,
                        SunPositionElevation = sunElevation,
                        OutdoorTemperature = outdoorTemperature,
                        ThermalControlMode = thermalMode,
                    },
                },
            };

            var model = new HomeCompanion.Base.Model.Model
            {
                Buildings = new Dictionary<string, Building>
                {
                    [building.Name] = building,
                },
            };

            var valueProvider = new StubValueReferenceProvider(new Dictionary<string, IValue>(StringComparer.OrdinalIgnoreCase)
            {
                ["TargetPosition"] = targetPosition,
            });

            if (targetAngle is not null)
                valueProvider.Add("TargetAngle", targetAngle);

            if (targetOpenClose is not null)
                valueProvider.Add("TargetOpenClose", targetOpenClose);

            var publisher = new StubPublisher();
            var subscriber = new StubSubscriber();
            var stateStore = new StubStateStore(preloadedState);
            var modelProvider = new StubModelProvider(model);

            var overrideOwner = new ShutterControl(
                modelProvider,
                valueProvider,
                stateStore,
                TimeProvider.System,
                NullLogger<ShutterControl>.Instance,
                publisher,
                subscriber);

            var logic = new ShutterSceneCommandControl(
                overrideOwner,
                modelProvider,
                valueProvider,
                TimeProvider.System,
                NullLogger<ShutterSceneCommandControl>.Instance,
                publisher,
                subscriber);

            return new TestFixtureRuntime
            {
                OverrideOwner = overrideOwner,
                Logic = logic,
                RoomScene = roomScene,
                TargetPosition = targetPosition,
                TargetAngle = targetAngle,
                TargetOpenClose = targetOpenClose,
                SunAzimuth = sunAzimuth,
                SunElevation = sunElevation,
                OutdoorTemperature = outdoorTemperature,
                ThermalMode = thermalMode,
                StateStore = stateStore,
                Subscriber = subscriber,
                RoomConfig = roomConfig,
                ShadowingConfig = shadowCfg,
                RoomKey = "Main/Ground/Living",
            };
        }

        public async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 1500)
        {
            var start = Environment.TickCount64;
            while (!condition())
            {
                if (Environment.TickCount64 - start > timeoutMs)
                    Assert.Fail("Condition was not reached in time.");
                await Task.Delay(10);
            }
        }
    }

    private sealed class StubModelProvider(HomeCompanion.Base.Model.Model model) : IModelProvider
    {
        public HomeCompanion.Base.Model.Model GetModel() => model;

        public bool IsInitialized => true;
    }

    private sealed class StubValueReferenceProvider(Dictionary<string, IValue> byReference) : IValueReferenceProvider
    {
        public void Add(string reference, IValue value) => byReference[reference] = value;

        public IValue Resolve(string reference) => byReference[reference];

        public bool TryResolve(string reference, out IValue? value)
            => byReference.TryGetValue(reference, out value);

        public bool TryResolve<T>(string reference, out IValue<T>? value)
        {
            if (byReference.TryGetValue(reference, out var untyped) && untyped is IValue<T> typed)
            {
                value = typed;
                return true;
            }

            value = null;
            return false;
        }
    }

    private sealed class StubStateStore(ShutterManualOverrideStateSet? preloadedState = null) : IStateStore
    {
        private readonly ShutterManualOverrideStateSet? _preloadedState = preloadedState;
        public ShutterManualOverrideStateSet? Stored { get; private set; }

        public Task<StateLoadingResult<T>> LoadAsync<T>(string stateSetName, TimeSpan maxAge) where T : class, new()
        {
            T state;
            if (typeof(T) == typeof(ShutterManualOverrideStateSet) && _preloadedState is not null)
                state = (T)(object)_preloadedState;
            else
                state = new T();

            return Task.FromResult(new StateLoadingResult<T>
            {
                IsSuccess = true,
                IsRecent = true,
                StateSet = state,
            });
        }

        public Task<StateLoadingResult<T>> LoadAsync<T>(string stateSetName) where T : class, new()
            => LoadAsync<T>(stateSetName, TimeSpan.FromMinutes(30));

        public Task SaveAsync<T>(string stateSetName, T stateSet, CancellationToken cancellation) where T : class, new()
        {
            if (stateSet is ShutterManualOverrideStateSet typed)
                Stored = typed;
            return Task.CompletedTask;
        }

        public Task SaveAsync<T>(string stateSetName, T stateSet, int timeoutSeconds = 30) where T : class, new()
        {
            if (stateSet is ShutterManualOverrideStateSet typed)
                Stored = typed;
            return Task.CompletedTask;
        }
    }

    private sealed class StubPublisher : IEventPublisher
    {
        public ValueTask PublishAsync(IEvent @event, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class StubSubscriber : IEventSubscriber
    {
        private readonly Dictionary<Type, List<object>> _handlersByType = [];

        public void Subscribe<T>(IEventHandler<T> handler) where T : IEvent
        {
            if (!_handlersByType.TryGetValue(typeof(T), out var handlers))
            {
                handlers = [];
                _handlersByType[typeof(T)] = handlers;
            }

            handlers.Add(handler);
        }

        public async Task PublishAsync<T>(T @event) where T : IEvent
        {
            if (!_handlersByType.TryGetValue(typeof(T), out var handlers))
                return;

            foreach (var handler in handlers.Cast<IEventHandler<T>>())
                await handler.HandleAsync(@event);
        }
    }
}
