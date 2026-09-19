using System.Collections.Concurrent;
using System.Globalization;
using HomeCompanion;
using HomeCompanion.Abstractions;
using HomeCompanion.Core;
using HomeCompanion.Core.Events;
using HomeCompanion.Events;
using HomeCompanion.Integrations.Knx;
using HomeCompanion.Persistence;
using HomeCompanion.Tests.TestUtilities;
using HomeCompanion.Values;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SRF.Knx.Config;
using SRF.Knx.Config.Domain;
using SRF.Knx.Config.ETS5;
using SRF.Knx.Core;
using SRF.Knx.Core.DPT;
using SRF.Knx.Core.Master;
using SRF.Network.Knx;
using SRF.Network.Knx.Connection;
using SRF.Network.Knx.Dpt;
using SRF.Network.Knx.Messages;
using UnitsNet;

namespace HomeCompanion.Tests.Values;

[TestFixture]
public class KnxDpstScaledNumericIValueTests
{
    private const string ScaledGa = "2/1/1";
    private const string NonScaledGa = "2/1/2";
    private const string ParamGa = "2/1/3";
    private const double Dpst5_1Coefficient = 100.0 / 255.0;

    private static IEnumerable<TestCaseData> NumericCompatibilityCases
    {
        get
        {
            yield return new TestCaseData(new NumericKnxIntegrationCase(
                TestName: "DPST-9-4 / IValue<float>",
                DptId: "DPST-9-4",
                ValueType: typeof(float),
                InboundValue: 350.5f,
                OutboundValue: 711.25f,
                Tolerance: 0.2))
                .SetName("NumericCompatibility_Dpst9_4_IValueFloat");

            yield return new TestCaseData(new NumericKnxIntegrationCase(
                TestName: "DPST-9-1 / IValue<float>",
                DptId: "DPST-9-1",
                ValueType: typeof(float),
                InboundValue: 21.6f,
                OutboundValue: 23.2f,
                Tolerance: 0.05))
                .SetName("NumericCompatibility_Dpst9_1_IValueFloat");

            yield return new TestCaseData(new NumericKnxIntegrationCase(
                TestName: "DPST-9-4 / IValue<Illuminance> (lux)",
                DptId: "DPST-9-4",
                ValueType: typeof(Illuminance),
                InboundValue: 420.0,
                OutboundValue: 815.0,
                Tolerance: 0.2,
                ExpectedUnitToken: "lux"))
                .SetName("NumericCompatibility_Dpst9_4_IValueIlluminance");

            yield return new TestCaseData(new NumericKnxIntegrationCase(
                TestName: "DPST-9-1 / IValue<Temperature> (°C)",
                DptId: "DPST-9-1",
                ValueType: typeof(Temperature),
                InboundValue: 19.75,
                OutboundValue: 24.25,
                Tolerance: 0.05,
                ExpectedUnitToken: "°C"))
                .SetName("NumericCompatibility_Dpst9_1_IValueTemperature");
        }
    }

    [TestCaseSource(nameof(NumericCompatibilityCases))]
    public async Task InboundWrite_NumericCompatibilityCase_UpdatesIValue(NumericKnxIntegrationCase testCase)
    {
        var context = CreateCaseContext(testCase);
        var bus = CreateBus();
        var resolver = CreateResolver(new Dictionary<string, string> { [ParamGa] = testCase.DptId });
        var knxBus = new StubKnxBus();
        var connection = CreateConnection(knxBus, resolver);
        var provider = CreateProvider(connection, bus, context.Container, resolver);
        var dpt = resolver.GetDpt(new GroupAddress(ParamGa));
        var inboundRaw = dpt.ToGroupValue(ConvertToDptApplicationValue(testCase.InboundValue, dpt)).Value;

        await RunWithBusAsync(bus, async () =>
        {
            await provider.StartAsync(CancellationToken.None);

            knxBus.RaiseMessageReceived(new GroupEventArgs
            {
                DestinationAddress = new GroupAddress(ParamGa),
                SourceAddress = new IndividualAddress("1.1.12"),
                EventType = GroupEventType.ValueWrite,
                Value = new GroupValue(inboundRaw),
            });

            await Task.Delay(200);
            await provider.StopAsync(CancellationToken.None);
        });

        Assert.Multiple(() =>
        {
            var actualMagnitude = ReadValueMagnitude(context.Value, testCase);
            var expectedMagnitude = ToExpectedMagnitude(testCase.InboundValue, testCase);
            Assert.That(actualMagnitude, Is.EqualTo(expectedMagnitude).Within(testCase.Tolerance),
                $"Inbound KNX value for {testCase.TestName} should update IValue<{testCase.ValueType.Name}>.");
            Assert.That(context.Value.Status.HasFlag(ValueStatus.Initialized), Is.True);
            Assert.That(context.Value.Status.HasFlag(ValueStatus.Live), Is.True);
        });
    }

    [TestCaseSource(nameof(NumericCompatibilityCases))]
    public async Task ValueWrite_NumericCompatibilityCase_SendsExpectedRawKnxValue(NumericKnxIntegrationCase testCase)
    {
        var context = CreateCaseContext(testCase);
        var bus = CreateBus();
        var resolver = CreateResolver(new Dictionary<string, string> { [ParamGa] = testCase.DptId });
        var knxBus = new StubKnxBus();
        var connection = CreateConnection(knxBus, resolver);
        var provider = CreateProvider(connection, bus, context.Container, resolver);
        var dpt = resolver.GetDpt(new GroupAddress(ParamGa));
        var expectedRaw = dpt.ToGroupValue(ConvertToDptApplicationValue(testCase.OutboundValue, dpt)).Value;
        var outboundTypedValue = ConvertToValueTypeValue(testCase.OutboundValue, testCase);

        await RunWithBusAsync(bus, async () =>
        {
            await provider.StartAsync(CancellationToken.None);
            knxBus.SentMessages.Clear();

            context.Write(outboundTypedValue);

            await Task.Delay(200);
            await provider.StopAsync(CancellationToken.None);
        });

        var writeMessage = knxBus.SentMessages.LastOrDefault(m =>
            m.EventType == GroupEventType.ValueWrite &&
            m.DestinationAddress.ToString() == ParamGa);

        Assert.That(writeMessage, Is.Not.Null,
            $"Expected outbound KNX ValueWrite for group address {ParamGa} in {testCase.TestName}.");
        Assert.That(writeMessage!.Value.Value, Is.EqualTo(expectedRaw),
            $"Outbound KNX raw payload mismatch for {testCase.TestName}.");

        if (!string.IsNullOrWhiteSpace(testCase.ExpectedUnitToken))
        {
            var formatted = dpt.Format(new GroupValue(writeMessage.Value.Value), "en", CultureInfo.InvariantCulture, null);
            Assert.That(formatted, Does.Contain(testCase.ExpectedUnitToken!).IgnoreCase,
                $"Expected formatted DPT value for {testCase.TestName} to contain unit token '{testCase.ExpectedUnitToken}'.");
        }
    }

    [TestCase(255, 100.0)]
    [TestCase(0, 0.0)]
    [TestCase(127, 127.0 * Dpst5_1Coefficient)]
    public async Task InboundWrite_Dpst5_1_UpdatesIValueDoubleUsingCoefficient(byte raw, double expected)
    {
        var bus = CreateBus();
        var resolver = CreateResolver(new Dictionary<string, string> { [ScaledGa] = "DPST-5-1" });
        var knxBus = new StubKnxBus();
        var connection = CreateConnection(knxBus, resolver);
        var container = new DoubleKnxValueContainer(ScaledGa, "DPST-5-1");
        var provider = CreateProvider(connection, bus, container, resolver);

        await RunWithBusAsync(bus, async () =>
        {
            await provider.StartAsync(CancellationToken.None);

            knxBus.RaiseMessageReceived(new GroupEventArgs
            {
                DestinationAddress = new GroupAddress(ScaledGa),
                SourceAddress = new IndividualAddress("1.1.10"),
                EventType = GroupEventType.ValueWrite,
                Value = new GroupValue([raw]),
            });

            await Task.Delay(200);
            await provider.StopAsync(CancellationToken.None);
        });

        Assert.Multiple(() =>
        {
            Assert.That(container.Percent.Value, Is.EqualTo(expected).Within(0.51),
                $"Raw value {raw} for DPST-5-1 should be converted to percentage via coefficient {Dpst5_1Coefficient}.");
            Assert.That(container.Percent.Status.HasFlag(ValueStatus.Initialized), Is.True);
            Assert.That(container.Percent.Status.HasFlag(ValueStatus.Live), Is.True);
        });
    }

    [TestCase(100.0, 255)]
    [TestCase(0.0, 0)]
    [TestCase(49.8, 127)]
    public async Task ValueWrite_Dpst5_1_SendsExpectedRawKnxGroupValue(double valueToWrite, byte expectedRaw)
    {
        var bus = CreateBus();
        var resolver = CreateResolver(new Dictionary<string, string> { [ScaledGa] = "DPST-5-1" });
        var knxBus = new StubKnxBus();
        var connection = CreateConnection(knxBus, resolver);
        var container = new DoubleKnxValueContainer(ScaledGa, "DPST-5-1");
        var provider = CreateProvider(connection, bus, container, resolver);

        await RunWithBusAsync(bus, async () =>
        {
            await provider.StartAsync(CancellationToken.None);
            knxBus.SentMessages.Clear();

            container.Percent.Write(valueToWrite);

            await Task.Delay(200);
            await provider.StopAsync(CancellationToken.None);
        });

        var writeMessage = knxBus.SentMessages.LastOrDefault(m =>
            m.EventType == GroupEventType.ValueWrite &&
            m.DestinationAddress.ToString() == ScaledGa);

        Assert.That(writeMessage, Is.Not.Null,
            $"Expected outbound KNX ValueWrite for group address {ScaledGa}.");
        Assert.That(writeMessage!.Value.Value, Is.EqualTo(new byte[] { expectedRaw }),
            $"Writing {valueToWrite} on DPST-5-1 should encode to raw byte {expectedRaw}.");
    }

    [Test]
    public async Task ValueWrite_TypeMismatch_AddsValueExceptionAndSkipsSend()
    {
        var bus = CreateBus();
        var resolver = CreateResolver(new Dictionary<string, string> { [NonScaledGa] = "DPST-1-1" });
        var knxBus = new StubKnxBus();
        var connection = CreateConnection(knxBus, resolver);
        var container = new DoubleKnxValueContainer(NonScaledGa, "DPST-1-1");
        var provider = CreateProvider(connection, bus, container, resolver);

        await RunWithBusAsync(bus, async () =>
        {
            await provider.StartAsync(CancellationToken.None);
            knxBus.SentMessages.Clear();

            container.Percent.Write(128.0);

            await Task.Delay(200);
            await provider.StopAsync(CancellationToken.None);
        });

        Assert.Multiple(() =>
        {
            Assert.That(knxBus.SentMessages, Has.None.Matches<IKnxMessage>(m =>
                m.EventType == GroupEventType.ValueWrite &&
                m.DestinationAddress.ToString() == NonScaledGa));
            Assert.That(container.Percent.Exceptions.Any(e => e.Message.Contains("KNX write skipped", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    private static EventBus CreateBus() => new(NullLogger<EventBus>.Instance);

    private static async Task RunWithBusAsync(EventBus bus, Func<Task> action, int drainMs = 200)
    {
        using var cts = new CancellationTokenSource();
        await bus.StartAsync(cts.Token);
        await action();
        await Task.Delay(drainMs);
        await cts.CancelAsync();
        try { await bus.StopAsync(CancellationToken.None); } catch (OperationCanceledException) { }
    }

    private static KnxConnection CreateConnection(StubKnxBus knxBus, IDptResolver resolver)
        => new(
            new KnxLibraryInitializationStub(),
            knxBus,
            Options.Create(new KnxConnectionOptions()),
            NullLogger<KnxConnection>.Instance,
            resolver);

    private static KnxConnectivityProvider CreateProvider(
        IKnxConnection connection,
        EventBus bus,
        IValuesContainer container,
        IDptResolver resolver)
    {
        var provider = new KnxConnectivityProvider(
            Options.Create(new KnxIntegrationOptions()),
            new StubKnxSystemConfiguration(resolver),
            [connection],
            bus,
            bus,
            [container],
            new StubLifeCycleManager(false, false),
            new StubStateInitializationManager(),
            resolver,
            NullLogger<KnxConnectivityProvider>.Instance);

        InitializeValues([container], bus, new TestValuesManager(bus));
        return provider;
    }

    private static void InitializeValues(IEnumerable<IValuesContainer> containers, IEventPublisher publisher, IValuesManager manager)
    {
        foreach (var container in containers)
        {
            foreach (var property in container.GetType().GetIValueProperties())
            {
                if (property.GetValue(container) is IValue value)
                    value.Initialize(publisher, manager);
            }
        }
    }

    private static IDptResolver CreateResolver(Dictionary<string, string> dptsByGa)
    {
        var domainConfig = new DomainConfiguration();
        foreach (var (gaText, dptId) in dptsByGa)
        {
            var ga = new GroupAddress(gaText);
            domainConfig.GroupAddresses[ga.Address] = new EtsGroupAddressConfig
            {
                Address = ga,
                Label = $"Fixture-{gaText}",
                DPTs = dptId,
            };
        }

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddKnxCore();
        services.AddSingleton<IKnxMasterDataProvider>(KnxMasterDataProviderStub.Create());
        services.AddSingleton(domainConfig);
        services.TryAddSingleton<IDptResolver, KnxDptResolver>();

        return services.BuildServiceProvider(validateScopes: false).GetRequiredService<IDptResolver>();
    }

    private static CaseContext CreateCaseContext(NumericKnxIntegrationCase testCase)
    {
        var containerType = typeof(NumericKnxValueContainer<>).MakeGenericType(testCase.ValueType);
        var container = (IValuesContainer)(Activator.CreateInstance(containerType, ParamGa, testCase.DptId, TryCreateUnitInfo(testCase))
            ?? throw new InvalidOperationException($"Could not create container for {testCase.TestName}."));

        var valueProperty = containerType.GetProperty("Numeric")
            ?? throw new InvalidOperationException($"Numeric property not found on {containerType.FullName}.");
        var valueObject = valueProperty.GetValue(container)
            ?? throw new InvalidOperationException($"Numeric IValue is null for {testCase.TestName}.");
        var value = (IValue)valueObject;

        var writeMethod = valueObject.GetType()
            .GetMethods()
            .FirstOrDefault(m =>
            {
                if (!string.Equals(m.Name, "Write", StringComparison.Ordinal))
                    return false;

                var parameters = m.GetParameters();
                return parameters.Length >= 1 && parameters[0].ParameterType == testCase.ValueType;
            })
            ?? throw new InvalidOperationException($"Write({testCase.ValueType.Name}) not found for {testCase.TestName}.");

        return new CaseContext(
            Container: container,
            Value: value,
            Write: payload => writeMethod.Invoke(valueObject, [payload, null]));
    }

    private static object ConvertToValueTypeValue(object value, NumericKnxIntegrationCase testCase)
    {
        var valueType = testCase.ValueType;
        if (valueType.IsInstanceOfType(value))
            return value;

        if (typeof(IQuantity).IsAssignableFrom(valueType))
        {
            var magnitude = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            var unit = ResolveCaseQuantityUnit(testCase);
            var quantity = Quantity.From(magnitude, unit);

            if (!valueType.IsInstanceOfType(quantity))
                throw new InvalidOperationException($"Quantity type {quantity.GetType().FullName} does not match expected IValue type {valueType.FullName}.");

            return quantity;
        }

        return Convert.ChangeType(value, valueType, CultureInfo.InvariantCulture);
    }

    private static double ReadValueMagnitude(IValue value, NumericKnxIntegrationCase testCase)
    {
        if (value.OValue is IQuantity quantity)
            return quantity.As(ResolveCaseQuantityUnit(testCase));

        return Convert.ToDouble(value.OValue, CultureInfo.InvariantCulture);
    }

    private static double ToExpectedMagnitude(object value, NumericKnxIntegrationCase testCase)
    {
        if (value is IQuantity quantity)
            return quantity.As(ResolveCaseQuantityUnit(testCase));

        return Convert.ToDouble(value, CultureInfo.InvariantCulture);
    }

    private static Enum ResolveCaseQuantityUnit(NumericKnxIntegrationCase testCase)
    {
        if (testCase.ValueType == typeof(Temperature))
            return UnitsNet.Units.TemperatureUnit.DegreeCelsius;

        if (testCase.ValueType == typeof(Illuminance))
            return UnitsNet.Units.IlluminanceUnit.Lux;

        throw new InvalidOperationException($"No quantity unit mapping configured for case {testCase.TestName} ({testCase.ValueType.FullName}).");
    }

    private static ValueUnitInfo? TryCreateUnitInfo(NumericKnxIntegrationCase testCase)
    {
        if (testCase.ValueType == typeof(Temperature))
            return new ValueUnitInfo("Temperature", "DegreeCelsius", "°C");

        if (testCase.ValueType == typeof(Illuminance))
            return new ValueUnitInfo("Illuminance", "Lux", "lux");

        return null;
    }

    private static object ConvertToDptApplicationValue(object value, DptBase dpt)
    {
        if (dpt.ApplicationType.IsInstanceOfType(value))
            return value;

        if (typeof(IQuantity).IsAssignableFrom(dpt.ApplicationType))
        {
            var knxUnit = ResolveKnxUnit(dpt);
            var magnitude = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            var quantity = Quantity.From(magnitude, knxUnit);

            if (!dpt.ApplicationType.IsInstanceOfType(quantity))
                throw new InvalidOperationException($"Quantity type {quantity.GetType().FullName} does not match DPT application type {dpt.ApplicationType.FullName} for {dpt.Id}.");

            return quantity;
        }

        return Convert.ChangeType(value, dpt.ApplicationType, CultureInfo.InvariantCulture);
    }

    private static Enum ResolveKnxUnit(DptBase dpt)
    {
        var knxUnitProperty = dpt.GetType().GetProperty("KnxUnit");
        if (knxUnitProperty?.GetValue(dpt) is not Enum knxUnit)
            throw new InvalidOperationException($"DPT {dpt.Id} is quantity-based, but KnxUnit could not be resolved from type {dpt.GetType().FullName}.");

        return knxUnit;
    }

    private sealed class DoubleKnxValueContainer : IValuesContainer
    {
        public ValueBase<double> Percent { get; }

        public DoubleKnxValueContainer(string groupAddress, string dpt)
        {
            Percent = new ValueBase<double>(NullLoggerFactory.Instance.CreateLogger<ValueBase<double>>())
            {
                BusMappings = new()
                {
                    [KnxBusEndpointMapping.BusId] = new KnxBusEndpointMapping(groupAddress, dpt)
                    {
                        Communication = BusCommunication.RegularCommunication | BusCommunication.AnswerReadRequests,
                    },
                },
            };
        }

        public IEnumerable<IValue> GetValues() => [Percent];
    }

    private sealed class NumericKnxValueContainer<TNumeric> : IValuesContainer where TNumeric : notnull
    {
        public ValueBase<TNumeric> Numeric { get; }

        public NumericKnxValueContainer(string groupAddress, string dpt, ValueUnitInfo? unitMetadata = null)
        {
            Numeric = new ValueBase<TNumeric>(NullLoggerFactory.Instance.CreateLogger<ValueBase<TNumeric>>())
            {
            Unit = unitMetadata,
                BusMappings = new()
                {
                    [KnxBusEndpointMapping.BusId] = new KnxBusEndpointMapping(groupAddress, dpt)
                    {
                        Communication = BusCommunication.RegularCommunication | BusCommunication.AnswerReadRequests,
                    },
                },
            };
        }

        public IEnumerable<IValue> GetValues() => [Numeric];
    }

    public sealed record NumericKnxIntegrationCase(
        string TestName,
        string DptId,
        Type ValueType,
        object InboundValue,
        object OutboundValue,
        double Tolerance,
        string? ExpectedUnitToken = null);

    private sealed record CaseContext(
        IValuesContainer Container,
        IValue Value,
        Action<object> Write);

    private sealed class StubKnxBus : IKnxBus
    {
        public bool IsConnected { get; private set; }

        public BusConnectionState ConnectionState { get; private set; } = BusConnectionState.Closed;

        public event EventHandler<KnxConnectionEventArgs>? ConnectionStateChanged;

        public event EventHandler<KnxMessageReceivedEventArgs>? MessageReceived;

        public List<IKnxMessage> SentMessages { get; } = [];

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = true;
            ConnectionState = BusConnectionState.Connected;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = false;
            ConnectionState = BusConnectionState.Closed;
            return Task.CompletedTask;
        }

        public Task SendGroupMessageAsync(IKnxMessage message, CancellationToken cancellationToken = default)
        {
            SentMessages.Add(message);
            return Task.CompletedTask;
        }

        public void RaiseMessageReceived(GroupEventArgs args, DateTimeOffset? at = null)
            => MessageReceived?.Invoke(this, new KnxMessageReceivedEventArgs(args, at ?? DateTimeOffset.UtcNow));

        public void RaiseConnectionStatusChanged(bool isConnected)
        {
            IsConnected = isConnected;
            ConnectionState = isConnected ? BusConnectionState.Connected : BusConnectionState.Closed;
            ConnectionStateChanged?.Invoke(this, new KnxConnectionEventArgs());
        }
    }

    private sealed class TestValuesManager : IValuesManager
    {
        private readonly ConcurrentDictionary<IValue, bool> _values = [];

        public TestValuesManager(IEventSubscriber subscriber)
        {
            subscriber.Subscribe(new ValueUpdateHandler(this));
            subscriber.Subscribe(new ValueWriteHandler(this));
        }

        public void RegisterValue(IValue value) => _values.TryAdd(value, true);

        public void UnregisterValue(IValue value) => _values.TryRemove(value, out _);

        private void Route(ValueUpdateReceived @event)
        {
            if (@event.Target is IValueEventReceiver receiver && _values.ContainsKey(@event.Target))
                receiver.ReceiveUpdate(@event.Value);
        }

        private void Route(ValueWriteReceived @event)
        {
            if (@event.Target is IValueEventReceiver receiver && _values.ContainsKey(@event.Target))
                receiver.ReceiveWrite(@event.Value);
        }

        private sealed class ValueUpdateHandler(TestValuesManager owner) : IEventHandler<ValueUpdateReceived>
        {
            public ValueTask HandleAsync(ValueUpdateReceived @event, CancellationToken cancellationToken = default)
            {
                owner.Route(@event);
                return ValueTask.CompletedTask;
            }
        }

        private sealed class ValueWriteHandler(TestValuesManager owner) : IEventHandler<ValueWriteReceived>
        {
            public ValueTask HandleAsync(ValueWriteReceived @event, CancellationToken cancellationToken = default)
            {
                owner.Route(@event);
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class StubKnxSystemConfiguration(IDptResolver resolver) : IKnxSystemConfiguration
    {
        public DptBase GetDpt(GroupAddress groupAddress) => resolver.GetDpt(groupAddress);

        public void ClearCache() { }

        public DptBase GetDptFromId(string dptId) => throw new NotImplementedException();

        public GroupAddressMeta GetGroupAddressMeta(GroupAddress groupAddress) => throw new NotImplementedException();

        public GroupAddressMeta GetGroupAddressMeta(string name) => throw new NotImplementedException();

        public GroupAddressMeta? GetGroupAddressMetaOrNull(GroupAddress groupAddress) => null;

        public GroupAddressMeta? GetGroupAddressMetaOrNull(string name) => null;

        public bool TryGetGroupAddressMeta(GroupAddress ga, out GroupAddressMeta? gaConfig)
        {
            gaConfig = null;
            return false;
        }
    }

    private sealed class StubStateInitializationManager : IStateInitializationRegistrar
    {
        public void RegisterInitialization(AppLifeCycleStage stage, StateInitializationDelegate initialization) { }

        public void RemoveInitialization(AppLifeCycleStage stage, StateInitializationDelegate initialization) { }

        public void RegisterSave(StateInitializationDelegate save) { }

        public void RemoveSave(StateInitializationDelegate save) { }
    }

    private sealed class KnxMasterDataProviderStub(KnxMasterData masterData) : IKnxMasterDataProvider
    {
        public KnxMasterData GetMasterData() => masterData;

        public bool TryGetDptMaster(DataPointTypeId dptId, out DatapointType? dpt, out DatapointSubtype? dptSubtype)
        {
            var dt = masterData.MasterData?.DatapointTypes?.Items.Values
                .FirstOrDefault(x => x.Number == dptId.Main);

            if (dt is null)
            {
                dpt = null;
                dptSubtype = null;
                return false;
            }

            dpt = dt;
            dptSubtype = dptId.Sub == 0
                ? null
                : dt.DatapointSubtypes?.DatapointSubtype.FirstOrDefault(s => s.Number == dptId.Sub);

            return dptId.Sub == 0 || dptSubtype is not null;
        }

        public static KnxMasterDataProviderStub Create()
        {
            var baseDir = Path.GetDirectoryName(typeof(KnxMasterDataProviderStub).Assembly.Location) ?? "";
            var path = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..",
                "SRF.Network", "Subs", "SRF.Knx", "SRF.Knx.Config", "Resources", "knx_master.xml"));

            if (!File.Exists(path))
            {
                Assert.Fail($"knx_master.xml not found at: {path}");
            }

            return new KnxMasterDataProviderStub(KnxMasterDataLoader.LoadFromFile(path));
        }
    }
}
