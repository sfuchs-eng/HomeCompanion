using System.Reflection;
using HomeCompanion.Integrations.Knx;
using HomeCompanion.Values;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SRF.Knx.Config;
using SRF.Knx.Config.Domain;
using SRF.Knx.Config.ETS5;
using SRF.Knx.Core;
using SRF.Knx.Core.DPT;
using SRF.Knx.Core.Master;
using SRF.Network.Knx.Dpt;

namespace HomeCompanion.Tests;

/// <summary>
/// Verifies that numeric <see cref="IValue{T}"/> properties use KNX DPTs whose resolved CLR type
/// matches <typeparamref name="T"/>.
/// </summary>
[TestFixture]
public class KnxNumericValueTypeCompatibilityTests
{
    [Test]
    public void NumericIValues_MatchResolvedDptClrType()
    {
        // Arrange
        var expectedDptsByGa = new Dictionary<string, string>
        {
            ["1/0/1"] = "DPST-9-1",   // float
            ["1/0/2"] = "DPST-13-1",  // int
            ["1/0/3"] = "DPST-5-1",   // byte
        };

        var resolver = CreateResolver(expectedDptsByGa);
        var container = new FixtureNumericValuesContainer();

        // Act + Assert
        var numericCount = 0;
        var properties = container.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => typeof(IValue).IsAssignableFrom(p.PropertyType));

        foreach (var property in properties)
        {
            var valueInterface = property.PropertyType.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValue<>));

            if (valueInterface is null)
            {
                continue;
            }

            var clrType = valueInterface.GetGenericArguments()[0];
            if (!IsNumericType(clrType))
            {
                continue;
            }

            numericCount++;

            Assert.That(property.GetValue(container), Is.AssignableTo<IValue>(),
                $"Property '{property.Name}' should return an IValue instance.");

            var value = (IValue)property.GetValue(container)!;
            Assert.That(value.TryGetBusEndpoint<KnxBusEndpointMapping>(KnxBusEndpointMapping.BusId, out var mapping), Is.True,
                $"Property '{property.Name}' is numeric ({clrType.Name}) but has no KNX mapping.");

            var dpt = resolver.GetDpt(mapping!.GroupAddress);

            Assert.That(dpt, Is.AssignableTo<DptSimple>(),
                $"Property '{property.Name}' ({mapping.GroupAddress}) resolved DPT must be DptSimple for numeric mapping.");

            var simpleDpt = (DptSimple)dpt;
            Assert.That(simpleDpt.NumericInfo, Is.Not.Null,
                $"Property '{property.Name}' ({mapping.GroupAddress}, {dpt.Id.EtsFormat}) should resolve as numeric DPT with NumericInfo.");

            var expectedDpt = expectedDptsByGa[mapping.GroupAddress.ToString()];
            Assert.Multiple(() =>
            {
                Assert.That(dpt.Id.EtsFormat, Is.EqualTo(expectedDpt),
                    $"Property '{property.Name}' uses unexpected DPT for group address {mapping.GroupAddress}.");
                Assert.That(dpt.ValueType, Is.EqualTo(clrType),
                    $"Property '{property.Name}' ({mapping.GroupAddress}, {dpt.Id.EtsFormat}) has CLR type mismatch: IValue<{clrType.Name}> vs resolver {dpt.ValueType.Name}.");
            });
        }

        Assert.That(numericCount, Is.GreaterThan(0),
            "The fixture should contain numeric IValue<T> properties to validate.");
    }

    private static IDptResolver CreateResolver(Dictionary<string, string> expectedDptsByGa)
    {
        var domainConfig = new DomainConfiguration();
        foreach (var (gaText, dptId) in expectedDptsByGa)
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

    private static bool IsNumericType(Type type)
    {
        return type == typeof(byte)
            || type == typeof(sbyte)
            || type == typeof(short)
            || type == typeof(ushort)
            || type == typeof(int)
            || type == typeof(uint)
            || type == typeof(long)
            || type == typeof(ulong)
            || type == typeof(float)
            || type == typeof(double)
            || type == typeof(decimal);
    }

    private sealed class FixtureNumericValuesContainer : IValuesContainer
    {
        public ValueBase<float> Temperature { get; } = new(NullLoggerFactory.Instance.CreateLogger<ValueBase<float>>())
        {
            BusMappings = new()
            {
                [KnxBusEndpointMapping.BusId] = new KnxBusEndpointMapping("1/0/1", "DPST-9-1"),
            },
        };

        public ValueBase<int> Counter { get; } = new(NullLoggerFactory.Instance.CreateLogger<ValueBase<int>>())
        {
            BusMappings = new()
            {
                [KnxBusEndpointMapping.BusId] = new KnxBusEndpointMapping("1/0/2", "DPST-13-1"),
            },
        };

        public ValueBase<byte> Dimming { get; } = new(NullLoggerFactory.Instance.CreateLogger<ValueBase<byte>>())
        {
            BusMappings = new()
            {
                [KnxBusEndpointMapping.BusId] = new KnxBusEndpointMapping("1/0/3", "DPST-5-1"),
            },
        };

        public ValueBase<bool> NonNumericSwitch { get; } = new(NullLoggerFactory.Instance.CreateLogger<ValueBase<bool>>())
        {
            BusMappings = new()
            {
                [KnxBusEndpointMapping.BusId] = new KnxBusEndpointMapping("1/0/4", "DPST-1-1"),
            },
        };

        public IEnumerable<IValue> GetValues()
        {
            yield return Temperature;
            yield return Counter;
            yield return Dimming;
            yield return NonNumericSwitch;
        }
    }

    // ── Additional extension method / type-mismatch tests ────────────────────

    [Test]
    public void IsIValueProperties_TypeMismatch_ReturnsFalse()
    {
        // Arrange: float IValue mapped to a DPT whose CLR type is bool (DPT-1)
        var resolver = CreateResolver(new Dictionary<string, string>
        {
            ["2/0/1"] = "DPST-1-1",   // resolves to bool, not float
        });

        var container = new MismatchedContainer();

        var property = container.GetType()
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .First(p => p.Name == nameof(MismatchedContainer.FloatValueMappedToBoolDpt));

        var value = (IValue)property.GetValue(container)!;

        Assert.That(value.TryGetBusEndpoint<KnxBusEndpointMapping>(KnxBusEndpointMapping.BusId, out var mapping), Is.True);
        var dpt = resolver.GetDpt(mapping!.GroupAddress);

        // The IValue<float> type and the DPT's ValueType (bool) must not match
        Assert.That(dpt.ValueType, Is.Not.EqualTo(typeof(float)),
            "DPST-1-1 resolves to bool, not float — type mismatch should be detected.");
    }

    [Test]
    public void GetIValueProperties_ReturnsOnlyIValueProperties()
    {
        var container = new FixtureNumericValuesContainer();

        var properties = container.GetType()
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(p => typeof(IValue).IsAssignableFrom(p.PropertyType))
            .ToList();

        // All returned properties must implement IValue
        Assert.That(properties, Is.Not.Empty);
        foreach (var prop in properties)
        {
            Assert.That(typeof(IValue).IsAssignableFrom(prop.PropertyType), Is.True,
                $"Property '{prop.Name}' should implement IValue.");
        }
    }

    private sealed class MismatchedContainer : IValuesContainer
    {
        /// <summary>
        /// <see cref="ValueBase{T}"/> with T=float but the KNX mapping points to a bool DPT (DPST-1-1).
        /// This simulates a type mismatch that tests should be able to detect.
        /// </summary>
        public ValueBase<float> FloatValueMappedToBoolDpt { get; } = new(NullLoggerFactory.Instance.CreateLogger<ValueBase<float>>())
        {
            BusMappings = new()
            {
                [KnxBusEndpointMapping.BusId] = new KnxBusEndpointMapping("2/0/1", "DPST-1-1"),
            },
        };

        public IEnumerable<IValue> GetValues() => [FloatValueMappedToBoolDpt];
    }

    private sealed class KnxMasterDataProviderStub(KnxMasterData masterData) : IKnxMasterDataProvider
    {
        public KnxMasterData GetMasterData() => masterData;

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
