using HomeCompanion.Base.Model;
using HomeCompanion.Abstractions;
using HomeCompanion.Events;
using HomeCompanion.Values;
using HomeCompanion.Base.Logics.Shutters.AIAttempt;

namespace HomeCompanion.Tests.Shutters;

[TestFixture]
public class ShutterPolicyResolverTests
{
    [Test]
    public void ResolveRoomObjective_UsesExplicitRoomObjective_WhenNotInherited()
    {
        var global = new ShadowingSpecial("Shadowing", new CfgShadowingSpecial
        {
            ThermalControl = ThermalControlMode.CoolingPriority,
        });

        var room = new Room("Living", new CfgRoom
        {
            ObjectiveProfile = RoomObjectiveProfile.DaylightPriority,
        });

        var objective = ShutterPolicyResolver.ResolveRoomObjective(global, room);

        Assert.That(objective, Is.EqualTo(RoomObjectiveProfile.DaylightPriority));
    }

    [Test]
    public void ResolveRoomObjective_UsesThermalControlMapping_WhenRoomObjectiveInherited()
    {
        var global = new ShadowingSpecial("Shadowing", new CfgShadowingSpecial
        {
            ThermalControl = ThermalControlMode.Passive,
        });

        var room = new Room("Living", new CfgRoom
        {
            ObjectiveProfile = RoomObjectiveProfile.InheritFromThermalControl,
        });

        var objective = ShutterPolicyResolver.ResolveRoomObjective(global, room);

        Assert.That(objective, Is.EqualTo(RoomObjectiveProfile.DaylightPriority));
    }

    [Test]
    public void ResolveRoomObjective_UsesInputRule_WhenInputAvailable()
    {
        var global = new ShadowingSpecial("Shadowing", new CfgShadowingSpecial
        {
            ThermalControl = ThermalControlMode.BalancedCooling,
        });

        var room = new Room("Living", new CfgRoom
        {
            ObjectiveProfile = RoomObjectiveProfile.InheritFromThermalControl,
            ObjectiveSelectorInputs =
            {
                ["HotSignal"] = new CfgObjectiveSelectorInput
                {
                    ValueReference = "Values:HotSignal",
                    Threshold = 1.0,
                    ProfileAtOrAboveThreshold = RoomObjectiveProfile.ThermalPriority,
                    ProfileBelowThreshold = RoomObjectiveProfile.BalancedDefault,
                },
            },
        });

        var resolver = new StubValueReferenceProvider(new Dictionary<string, IValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["Values:HotSignal"] = new StubNumericValue(1.0),
        });

        var objective = ShutterPolicyResolver.ResolveRoomObjective(global, room, resolver);

        Assert.That(objective, Is.EqualTo(RoomObjectiveProfile.ThermalPriority));
    }

    [Test]
    public void ResolveThermalControlMode_UsesDynamicValue_WhenAvailable()
    {
        var global = new ShadowingSpecial("Shadowing", new CfgShadowingSpecial
        {
            ThermalControl = ThermalControlMode.Passive,
        })
        {
            ThermalControlMode = new StubByteValue((byte)ThermalControlMode.CoolingPriority),
        };

        var mode = ShutterPolicyResolver.ResolveThermalControlMode(global);

        Assert.That(mode, Is.EqualTo(ThermalControlMode.CoolingPriority));
    }

    [Test]
    public void ShouldApplyUvProtection_ReturnsFalse_WhenManualOverrideExists()
    {
        var allowed = ShutterPolicyResolver.ShouldApplyUvProtection(hasManualOverride: true);

        Assert.That(allowed, Is.False);
    }

    private sealed class StubValueReferenceProvider(Dictionary<string, IValue> byReference) : IValueProvider
    {
        public IValue Resolve(string reference)
            => byReference[reference];

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

    private sealed class StubNumericValue(double numericValue) : IValue
    {
        public Type ValueType => typeof(double);
        public ValueStatus Status => ValueStatus.Initialized;
        public string? Name => "StubNumeric";
        public string? Label => "StubNumeric";
        public object? OValue => numericValue;
#pragma warning disable CS0067
        public event EventHandler<ValueWrittenEventArgs>? Written;
        public event EventHandler<ValueChangedEventArgs>? Changed;
#pragma warning restore CS0067
        public Dictionary<object, IValueBusEndpointMapping> BusMappings { get; init; } = [];
        public void AddBusEndpoint(object busIdentifier, IValueBusEndpointMapping mapping) => BusMappings[busIdentifier] = mapping;
        public string? Format(System.Globalization.CultureInfo? culture = null) => numericValue.ToString(culture);
        public void Initialize(IEventPublisher publisher, IValuesManager manager)
        {
            _ = publisher;
            _ = manager;
        }

        public bool InitializeValue(object value, AppInitializationStage stage)
        {
            _ = value;
            _ = stage;
            return false;
        }

        public bool TryGetBusEndpoint<TBusMapping>(object busIdentifier, out TBusMapping? mapping) where TBusMapping : IValueBusEndpointMapping
        {
            if (BusMappings.TryGetValue(busIdentifier, out var untyped) && untyped is TBusMapping typed)
            {
                mapping = typed;
                return true;
            }

            mapping = default;
            return false;
        }
    }

    private sealed class StubByteValue(byte myByteValue) : IValue<byte>
    {
        public Type ValueType => typeof(byte);
        public ValueStatus Status => ValueStatus.Initialized;
        public string? Name => "StubByte";
        public string? Label => "StubByte";
        public object? OValue => Value;
#pragma warning disable CS0067
        public event EventHandler<ValueWrittenEventArgs>? Written;
        public event EventHandler<ValueChangedEventArgs>? Changed;
#pragma warning restore CS0067
        public Dictionary<object, IValueBusEndpointMapping> BusMappings { get; init; } = [];

        public byte Value { get; private set; } = myByteValue;

        public void AddBusEndpoint(object busIdentifier, IValueBusEndpointMapping mapping) => BusMappings[busIdentifier] = mapping;
        public string? Format(System.Globalization.CultureInfo? culture = null) => Value.ToString(culture);
        public void Initialize(IEventPublisher publisher, IValuesManager manager)
        {
            _ = publisher;
            _ = manager;
        }

        public bool InitializeValue(object value, AppInitializationStage stage)
        {
            _ = value;
            _ = stage;
            return false;
        }

        public bool TryGetBusEndpoint<TBusMapping>(object busIdentifier, out TBusMapping? mapping) where TBusMapping : IValueBusEndpointMapping
        {
            if (BusMappings.TryGetValue(busIdentifier, out var untyped) && untyped is TBusMapping typed)
            {
                mapping = typed;
                return true;
            }

            mapping = default;
            return false;
        }

        public void Write(byte value, object? initiator = null)
        {
            throw new NotImplementedException();
        }

        public void WriteLocked(byte value, object? initiator = null)
        {
            throw new NotImplementedException();
        }

        public bool InitializeValue(byte value, AppInitializationStage stage)
        {
            throw new NotImplementedException();
        }
    }
}