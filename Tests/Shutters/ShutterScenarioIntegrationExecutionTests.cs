using HomeCompanion.Base.Logics.Shutters;
using HomeCompanion.Base.Model;
using HomeCompanion.Events;
using HomeCompanion.Persistence;
using HomeCompanion.Values;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeCompanion.Tests.Shutters;

[TestFixture]
public class ShutterScenarioIntegrationExecutionTests
{
    [Test]
    public async Task Scenario11_SleepInMorning_NoAutoMovementUntilExplicitResume()
    {
        var fixture = ScenarioFixtureRuntime.CreateSingleFacade();

        await fixture.Logic.InitializeAsync();
        fixture.RoomScene.Write((byte)52, this);

        await fixture.Subscriber.PublishAsync(new RoomSceneWriteRequestedEvent
        {
            RoomKey = fixture.RoomKey,
            ScheduleKey = "NightClose",
            Scene = 20,
            TriggerLocalTime = new DateTime(2026, 6, 3, 22, 0, 0),
            Timestamp = DateTimeOffset.UtcNow,
        });

        await fixture.WaitUntilAsync(() => fixture.RoomScene.Value == 20);
        await Task.Delay(250);

        Assert.That(fixture.RoomScene.Value, Is.EqualTo((byte)20));
        Assert.That(fixture.StateStore.Stored?.RoomOverrides.ContainsKey(fixture.RoomKey), Is.True);
    }

    [Test]
    public async Task Scenario12_TimedResume_NoExposure_SkipsResumeWrite()
    {
        var fixture = ScenarioFixtureRuntime.CreateSingleFacade();

        await fixture.Logic.InitializeAsync();
        fixture.RoomScene.Write((byte)52, this);

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

        await fixture.WaitUntilAsync(() => fixture.RoomScene.Value == 20);
        await Task.Delay(350);

        Assert.That(fixture.RoomScene.Value, Is.EqualTo((byte)20));
        Assert.That(fixture.StateStore.Stored?.RoomOverrides.ContainsKey(fixture.RoomKey), Is.True);
    }

    [Test]
    public async Task Scenario13_TimedResume_WithExposure_ResumesAutomation()
    {
        var fixture = ScenarioFixtureRuntime.CreateSingleFacade();

        await fixture.Logic.InitializeAsync();
        fixture.RoomScene.Write((byte)52, this);

        await fixture.Subscriber.PublishAsync(new RoomSceneWriteRequestedEvent
        {
            RoomKey = fixture.RoomKey,
            ScheduleKey = "MorningResume",
            Scene = 20,
            ResumeAutomationAfter = TimeSpan.FromMilliseconds(100),
            ResumeAutomationScene = 52,
            TriggerLocalTime = new DateTime(2026, 6, 4, 6, 30, 0),
            Timestamp = DateTimeOffset.UtcNow,
        });

        await fixture.WaitUntilAsync(() => fixture.RoomScene.Value == 52, timeoutMs: 2500);
        await fixture.WaitUntilAsync(() => fixture.StateStore.Stored?.RoomOverrides.ContainsKey(fixture.RoomKey) == false, timeoutMs: 2500);

        Assert.That(fixture.RoomScene.Value, Is.EqualTo((byte)52));
    }

    [Test]
    public async Task Scenario17_FacadeSplitRoom_ScheduledScene_ActsPerSunExposedShutter()
    {
        var fixture = ScenarioFixtureRuntime.CreateFacadeSplitRoom();

        await fixture.Logic.InitializeAsync();
        fixture.RoomScene.Write((byte)52, this);

        fixture.SunAzimuth.Write(90f, this);
        fixture.SunElevation.Write(25f, this);

        await fixture.Subscriber.PublishAsync(new RoomSceneWriteRequestedEvent
        {
            RoomKey = fixture.RoomKey,
            ScheduleKey = "MorningShade",
            Scene = 20,
            TriggerLocalTime = new DateTime(2026, 6, 4, 8, 0, 0),
            Timestamp = DateTimeOffset.UtcNow,
        });

        await fixture.WaitUntilAsync(() => fixture.TargetEast.Value == 80);
        await Task.Delay(250);

        Assert.That(fixture.TargetEast.Value, Is.EqualTo((byte)80));
        Assert.That(fixture.TargetWest.Value, Is.EqualTo((byte)0));
        Assert.That(fixture.RoomScene.Value, Is.EqualTo((byte)20));
    }

    [Test]
    public async Task Scenario14_LateNightManualOpen_RemainsUntilExplicitResume()
    {
        var fixture = ScenarioFixtureRuntime.CreateSingleFacade();

        await fixture.Logic.InitializeAsync();
        fixture.RoomScene.Write((byte)52, this);

        await fixture.Subscriber.PublishAsync(new RoomSceneWriteRequestedEvent
        {
            RoomKey = fixture.RoomKey,
            ScheduleKey = "NightClose",
            Scene = 20,
            TriggerLocalTime = new DateTime(2026, 6, 3, 22, 0, 0),
            Timestamp = DateTimeOffset.UtcNow,
        });
        await fixture.WaitUntilAsync(() => fixture.RoomScene.Value == 20);

        // Manual user interaction after close.
        fixture.RoomScene.Write((byte)2, this);
        await fixture.WaitUntilAsync(() => fixture.RoomScene.Value == 2);

        // Next schedule due must not apply while room is still in manual scene.
        await fixture.Subscriber.PublishAsync(new RoomSceneWriteRequestedEvent
        {
            RoomKey = fixture.RoomKey,
            ScheduleKey = "NightCloseSecondPass",
            Scene = 20,
            TriggerLocalTime = new DateTime(2026, 6, 3, 22, 30, 0),
            Timestamp = DateTimeOffset.UtcNow,
        });

        await Task.Delay(150);
        Assert.That(fixture.RoomScene.Value, Is.EqualTo((byte)2));
    }

    [Test]
    public async Task Scenario15_DifferentMorningHabitConfigs_ApplyPerRoom()
    {
        var fixture = ScenarioFixtureRuntime.CreateDualRoomHabitSetup();

        await fixture.Logic.InitializeAsync();
        fixture.RoomScene.Write((byte)52, this);
        fixture.SecondRoomScene!.Write((byte)52, this);

        await fixture.Subscriber.PublishAsync(new RoomSceneWriteRequestedEvent
        {
            RoomKey = fixture.RoomKey,
            ScheduleKey = "ChildNightClose",
            Scene = 20,
            ResumeAutomationAfter = TimeSpan.FromMilliseconds(100),
            ResumeAutomationScene = 52,
            TriggerLocalTime = new DateTime(2026, 6, 4, 6, 45, 0),
            Timestamp = DateTimeOffset.UtcNow,
        });

        await fixture.Subscriber.PublishAsync(new RoomSceneWriteRequestedEvent
        {
            RoomKey = fixture.SecondRoomKey!,
            ScheduleKey = "LivingNightClose",
            Scene = 20,
            TriggerLocalTime = new DateTime(2026, 6, 4, 6, 45, 0),
            Timestamp = DateTimeOffset.UtcNow,
        });

        await fixture.WaitUntilAsync(() => fixture.RoomScene.Value == 52, timeoutMs: 2500);
        await Task.Delay(250);

        Assert.That(fixture.RoomScene.Value, Is.EqualTo((byte)52), "Child room should auto-resume.");
        Assert.That(fixture.SecondRoomScene!.Value, Is.EqualTo((byte)20), "Living room should stay in manual scene until explicit resume.");
    }

    [Test]
    public async Task Scenario18_TransientClouds_NoSceneFlappingWithoutResumeEvent()
    {
        var fixture = ScenarioFixtureRuntime.CreateSingleFacade();

        await fixture.Logic.InitializeAsync();
        fixture.RoomScene.Write((byte)52, this);

        await fixture.Subscriber.PublishAsync(new RoomSceneWriteRequestedEvent
        {
            RoomKey = fixture.RoomKey,
            ScheduleKey = "CloudTestStart",
            Scene = 20,
            TriggerLocalTime = new DateTime(2026, 6, 4, 10, 0, 0),
            Timestamp = DateTimeOffset.UtcNow,
        });
        await fixture.WaitUntilAsync(() => fixture.RoomScene.Value == 20);

        // Simulate short weather fluctuations. This component must not flap scene state by itself.
        for (var i = 0; i < 4; i++)
        {
            fixture.SunElevation.Write(i % 2 == 0 ? 5f : 35f, this);
            await Task.Delay(50);
        }

        Assert.That(fixture.RoomScene.Value, Is.EqualTo((byte)20));
    }

    [Test]
    public async Task Scenario19_CleaningWindow_ManualOpenHeldThenPolicyResume()
    {
        var fixture = ScenarioFixtureRuntime.CreateSingleFacade();

        await fixture.Logic.InitializeAsync();
        fixture.RoomScene.Write((byte)52, this);

        // Policy closes first.
        await fixture.Subscriber.PublishAsync(new RoomSceneWriteRequestedEvent
        {
            RoomKey = fixture.RoomKey,
            ScheduleKey = "PolicyClose",
            Scene = 20,
            TriggerLocalTime = new DateTime(2026, 6, 4, 13, 0, 0),
            Timestamp = DateTimeOffset.UtcNow,
        });
        await fixture.WaitUntilAsync(() => fixture.RoomScene.Value == 20);

        // User opens manually for cleaning.
        fixture.RoomScene.Write((byte)2, this);
        await fixture.WaitUntilAsync(() => fixture.RoomScene.Value == 2);

        // Repeated automation cycles must not override manual state.
        for (var i = 0; i < 3; i++)
        {
            await fixture.Subscriber.PublishAsync(new RoomSceneWriteRequestedEvent
            {
                RoomKey = fixture.RoomKey,
                ScheduleKey = $"PolicyClose-{i}",
                Scene = 20,
                TriggerLocalTime = new DateTime(2026, 6, 4, 13, 5 + i, 0),
                Timestamp = DateTimeOffset.UtcNow,
            });
        }

        await Task.Delay(150);
        Assert.That(fixture.RoomScene.Value, Is.EqualTo((byte)2));

        // User resumes automation explicitly, then policy can close again.
        fixture.RoomScene.Write((byte)52, this);
        await fixture.WaitUntilAsync(() => fixture.RoomScene.Value == 52);

        await fixture.Subscriber.PublishAsync(new RoomSceneWriteRequestedEvent
        {
            RoomKey = fixture.RoomKey,
            ScheduleKey = "PolicyClose-AfterResume",
            Scene = 20,
            TriggerLocalTime = new DateTime(2026, 6, 4, 13, 20, 0),
            Timestamp = DateTimeOffset.UtcNow,
        });
        await fixture.WaitUntilAsync(() => fixture.RoomScene.Value == 20);
    }

    [Test]
    public async Task Scenario20_AwayModeVsManualOverride_RespectsConfiguredPriority()
    {
        var fixture = ScenarioFixtureRuntime.CreateSingleFacade();

        await fixture.Logic.InitializeAsync();
        fixture.RoomScene.Write((byte)52, this);

        // User creates manual override.
        fixture.RoomScene.Write((byte)2, this);
        await fixture.WaitUntilAsync(() => fixture.StateStore.Stored?.RoomOverrides.ContainsKey(fixture.RoomKey) == true);

        // Simulate away-mode related ambient changes; manual override should still dominate
        // this component's schedule application until explicit resume.
        fixture.OutdoorTemperature.Write(35f, this);
        fixture.SunElevation.Write(30f, this);

        await fixture.Subscriber.PublishAsync(new RoomSceneWriteRequestedEvent
        {
            RoomKey = fixture.RoomKey,
            ScheduleKey = "AwayClose",
            Scene = 20,
            TriggerLocalTime = new DateTime(2026, 6, 4, 21, 0, 0),
            Timestamp = DateTimeOffset.UtcNow,
        });

        await Task.Delay(150);
        Assert.That(fixture.RoomScene.Value, Is.EqualTo((byte)2));

        // Explicit resume allows schedule application again.
        fixture.RoomScene.Write((byte)52, this);
        await fixture.WaitUntilAsync(() => fixture.RoomScene.Value == 52);

        await fixture.Subscriber.PublishAsync(new RoomSceneWriteRequestedEvent
        {
            RoomKey = fixture.RoomKey,
            ScheduleKey = "AwayClose-AfterResume",
            Scene = 20,
            TriggerLocalTime = new DateTime(2026, 6, 4, 21, 5, 0),
            Timestamp = DateTimeOffset.UtcNow,
        });
        await fixture.WaitUntilAsync(() => fixture.RoomScene.Value == 20);
    }

    [Test]
    public async Task Scenario23_SecurityHold_ClearsOnGlobalMorningResume()
    {
        var fixture = ScenarioFixtureRuntime.CreateSingleFacade();

        await fixture.Logic.InitializeAsync();
        fixture.RoomScene.Write((byte)52, this);

        // Overnight security-like schedule close.
        await fixture.Subscriber.PublishAsync(new RoomSceneWriteRequestedEvent
        {
            RoomKey = fixture.RoomKey,
            ScheduleKey = "SecurityClose",
            Scene = 20,
            TriggerLocalTime = new DateTime(2026, 6, 4, 22, 0, 0),
            Timestamp = DateTimeOffset.UtcNow,
        });
        await fixture.WaitUntilAsync(() => fixture.RoomScene.Value == 20);

        // Household explicit morning resume.
        fixture.RoomScene.Write((byte)52, this);
        await fixture.WaitUntilAsync(() => fixture.RoomScene.Value == 52);

        // Subsequent schedule actions can apply again after resume.
        await fixture.Subscriber.PublishAsync(new RoomSceneWriteRequestedEvent
        {
            RoomKey = fixture.RoomKey,
            ScheduleKey = "SecurityCloseAgain",
            Scene = 20,
            TriggerLocalTime = new DateTime(2026, 6, 5, 22, 0, 0),
            Timestamp = DateTimeOffset.UtcNow,
        });
        await fixture.WaitUntilAsync(() => fixture.RoomScene.Value == 20);
    }

    [Test]
    public async Task Scenario25_SeasonalSunGeometry_DifferentOutcomeAtSameIntensity()
    {
        var fixture = ScenarioFixtureRuntime.CreateSingleFacade();

        await fixture.Logic.InitializeAsync();
        fixture.RoomScene.Write((byte)52, this);

        // Winter-like geometry: low sun elevation keeps resume blocked.
        fixture.SunAzimuth.Write(90f, this);
        fixture.SunElevation.Write(1f, this);

        await fixture.Subscriber.PublishAsync(new RoomSceneWriteRequestedEvent
        {
            RoomKey = fixture.RoomKey,
            ScheduleKey = "WinterClose",
            Scene = 20,
            ResumeAutomationAfter = TimeSpan.FromMilliseconds(100),
            ResumeAutomationScene = 52,
            TriggerLocalTime = new DateTime(2026, 12, 21, 8, 0, 0),
            Timestamp = DateTimeOffset.UtcNow,
        });
        await fixture.WaitUntilAsync(() => fixture.RoomScene.Value == 20);
        await Task.Delay(250);
        Assert.That(fixture.RoomScene.Value, Is.EqualTo((byte)20));

        // Summer-like geometry: higher elevation allows resume.
        fixture.RoomScene.Write((byte)52, this);
        await fixture.WaitUntilAsync(() => fixture.RoomScene.Value == 52);
        fixture.SunAzimuth.Write(90f, this);
        fixture.SunElevation.Write(35f, this);

        await fixture.Subscriber.PublishAsync(new RoomSceneWriteRequestedEvent
        {
            RoomKey = fixture.RoomKey,
            ScheduleKey = "SummerClose",
            Scene = 20,
            ResumeAutomationAfter = TimeSpan.FromMilliseconds(100),
            ResumeAutomationScene = 52,
            TriggerLocalTime = new DateTime(2026, 6, 21, 8, 0, 0),
            Timestamp = DateTimeOffset.UtcNow,
        });
        await fixture.WaitUntilAsync(() => fixture.RoomScene.Value == 52, timeoutMs: 2500);
    }

    private sealed class ScenarioFixtureRuntime
    {
        public required ShutterControl OverrideOwner { get; init; }
        public required ShutterSceneCommandControl Logic { get; init; }
        public required ValueBase<byte> RoomScene { get; init; }
        public required ValueBase<byte> TargetEast { get; init; }
        public required ValueBase<byte> TargetWest { get; init; }
        public ValueBase<byte>? SecondRoomTarget { get; init; }
        public required ValueBase<float> SunAzimuth { get; init; }
        public required ValueBase<float> SunElevation { get; init; }
        public required ValueBase<float> OutdoorTemperature { get; init; }
        public required ValueBase<float> ThermalMode { get; init; }
        public required StubStateStore StateStore { get; init; }
        public required StubSubscriber Subscriber { get; init; }
        public required string RoomKey { get; init; }
        public string? SecondRoomKey { get; init; }
        public ValueBase<byte>? SecondRoomScene { get; init; }

        public static ScenarioFixtureRuntime CreateSingleFacade()
            => CreateInternal(splitFacade: false);

        public static ScenarioFixtureRuntime CreateFacadeSplitRoom()
            => CreateInternal(splitFacade: true);

        public static ScenarioFixtureRuntime CreateDualRoomHabitSetup()
            => CreateInternal(splitFacade: false, withSecondRoom: true);

        private static ScenarioFixtureRuntime CreateInternal(bool splitFacade, bool withSecondRoom = false)
        {
            var roomScene = new ValueBase<byte>(NullLogger<ValueBase<byte>>.Instance) { Name = "RoomScene" };
            var secondRoomScene = withSecondRoom
                ? new ValueBase<byte>(NullLogger<ValueBase<byte>>.Instance) { Name = "SecondRoomScene" }
                : null;
            var targetEast = new ValueBase<byte>(NullLogger<ValueBase<byte>>.Instance) { Name = "TargetEast" };
            var targetWest = new ValueBase<byte>(NullLogger<ValueBase<byte>>.Instance) { Name = "TargetWest" };
            var secondRoomTarget = withSecondRoom
                ? new ValueBase<byte>(NullLogger<ValueBase<byte>>.Instance) { Name = "SecondRoomTarget" }
                : null;
            var sunAzimuth = new ValueBase<float>(NullLogger<ValueBase<float>>.Instance) { Name = "SunAzimuth" };
            var sunElevation = new ValueBase<float>(NullLogger<ValueBase<float>>.Instance) { Name = "SunElevation" };
            var outdoorTemperature = new ValueBase<float>(NullLogger<ValueBase<float>>.Instance) { Name = "OutdoorTemperature" };
            var thermalMode = new ValueBase<float>(NullLogger<ValueBase<float>>.Instance) { Name = "ThermalMode" };

            var seedSource = new object();
            sunAzimuth.Write(129f, seedSource);
            sunElevation.Write(25f, seedSource);
            outdoorTemperature.Write(24f, seedSource);
            thermalMode.Write(1f, seedSource);

            var roomConfig = new CfgRoom();
            roomConfig.Shutters["EastShutter"] = new CfgShutter
            {
                FacadeReference = "E",
                PositionValueReference = "TargetEast",
            };

            if (splitFacade)
            {
                roomConfig.Shutters["WestShutter"] = new CfgShutter
                {
                    FacadeReference = "W",
                    PositionValueReference = "TargetWest",
                };
            }

            var room = new Room("Living", roomConfig)
            {
                ShutterScene = roomScene,
                Shutters = roomConfig.Shutters.ToDictionary(
                    kv => kv.Key,
                    kv => new Shutter(kv.Key, kv.Value)),
            };

            var floor = new Floor
            {
                Name = "Ground",
                Rooms = new Dictionary<string, Room>
                {
                    [room.Name] = room,
                },
            };

            if (withSecondRoom && secondRoomScene is not null)
            {
                var secondRoomConfig = new CfgRoom();
                secondRoomConfig.Shutters["LivingShutter"] = new CfgShutter
                {
                    FacadeReference = "E",
                    PositionValueReference = "SecondRoomTarget",
                };

                var secondRoom = new Room("Living2", secondRoomConfig)
                {
                    ShutterScene = secondRoomScene,
                    Shutters = secondRoomConfig.Shutters.ToDictionary(
                        kv => kv.Key,
                        kv => new Shutter(kv.Key, kv.Value)),
                };

                floor.Rooms[secondRoom.Name] = secondRoom;
            }

            var controllerCommands = new Dictionary<string, CfgShadowingSceneCommand>
            {
                ["SetEast"] = new()
                {
                    TargetValueReference = "TargetEast",
                    Value = 80,
                },
            };

            if (splitFacade)
            {
                controllerCommands["SetWest"] = new CfgShadowingSceneCommand
                {
                    TargetValueReference = "TargetWest",
                    Value = 30,
                };
            }

            var shadowCfg = new CfgShadowingSpecial
            {
                ResumeAutomationScenes = [50, 52],
                SpecialScenes =
                {
                    ["Manual20"] = new CfgShadowingSceneController
                    {
                        RoomReference = "Main/Ground/Living",
                        Number = 20,
                        Commands = controllerCommands,
                    },
                },
            };

            if (withSecondRoom)
            {
                shadowCfg.SpecialScenes["Living2Manual20"] = new CfgShadowingSceneController
                {
                    RoomReference = "Main/Ground/Living2",
                    Number = 20,
                    Commands =
                    {
                        ["SetSecond"] = new CfgShadowingSceneCommand
                        {
                            TargetValueReference = "SecondRoomTarget",
                            Value = 40,
                        },
                    },
                };
            }

            var building = new Building
            {
                Name = "Main",
                Facades = new Dictionary<string, Facade>
                {
                    ["E"] = new("E", new CfgFacade { Azimuth = 90, Elevation = 0 }),
                    ["W"] = new("W", new CfgFacade { Azimuth = 270, Elevation = 0 }),
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

            var byReference = new Dictionary<string, IValue>(StringComparer.OrdinalIgnoreCase)
            {
                ["TargetEast"] = targetEast,
                ["TargetWest"] = targetWest,
            };

            if (withSecondRoom && secondRoomTarget is not null)
                byReference["SecondRoomTarget"] = secondRoomTarget;

            var stateStore = new StubStateStore();
            var subscriber = new StubSubscriber();
            var publisher = new StubPublisher();
            var modelProvider = new StubModelProvider(model);
            var valueProvider = new StubValueReferenceProvider(byReference);
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

            return new ScenarioFixtureRuntime
            {
                OverrideOwner = overrideOwner,
                Logic = logic,
                RoomScene = roomScene,
                SecondRoomScene = secondRoomScene,
                TargetEast = targetEast,
                TargetWest = targetWest,
                SecondRoomTarget = secondRoomTarget,
                SunAzimuth = sunAzimuth,
                SunElevation = sunElevation,
                OutdoorTemperature = outdoorTemperature,
                ThermalMode = thermalMode,
                StateStore = stateStore,
                Subscriber = subscriber,
                RoomKey = "Main/Ground/Living",
                SecondRoomKey = withSecondRoom ? "Main/Ground/Living2" : null,
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

    private sealed class StubStateStore : IStateStore
    {
        public ShutterManualOverrideStateSet? Stored { get; private set; }

        public Task<StateLoadingResult<T>> LoadAsync<T>(string stateSetName, TimeSpan maxAge) where T : class, new()
            => Task.FromResult(new StateLoadingResult<T>
            {
                IsSuccess = true,
                IsRecent = true,
                StateSet = new T(),
            });

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
