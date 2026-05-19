using HomeCompanion.Integrations.Knx;
using HomeCompanion.Integrations.OpenHab;
using HomeCompanion.Values;
using Microsoft.Extensions.Logging.Abstractions;
using SRF.Knx.Config;
using SRF.Knx.Config.Domain;
using SRF.Knx.Config.ETS5;
using SRF.Knx.Core;
using SRF.Knx.Core.DPT;
using SRF.Knx.Core.Master;
using SRF.Network.Knx.Dpt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Tests;

/// <summary>
/// Tests for <see cref="OpenHabStateConverter"/> using real KNX master data.
/// </summary>
[TestFixture]
public class OpenHabStateConverterTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private OpenHabStateConverter CreateConverter(Dictionary<string, string> dptsByGa)
    {
        var resolver = CreateResolver(dptsByGa);
        var knxConfig = new StubKnxSystemConfiguration(resolver);

        return new OpenHabStateConverter(
            knxConfig,
            KnxMasterDataProviderStub.Create(),
            NullLogger<OpenHabStateConverter>.Instance);
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
                Label = $"Test-{gaText}",
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

    private static ValueBase<T> ValueWithKnxMapping<T>(string groupAddress, string dptId)
        => new(NullLoggerFactory.Instance.CreateLogger<ValueBase<T>>())
        {
            BusMappings = new() { [KnxBusEndpointMapping.BusId] = new KnxBusEndpointMapping(groupAddress, dptId) },
        };

    private static ValueBase<T> ValueWithoutKnxMapping<T>()
        => new(NullLoggerFactory.Instance.CreateLogger<ValueBase<T>>());

    // ── Tests: BitFormat (boolean, DPT-1) ────────────────────────────────────

    [Test]
    public void TryConvertValue_BitFormat_ON_ReturnsTrue_WithBooleanTrue()
    {
        var converter = CreateConverter(new() { ["0/0/1"] = "DPST-1-1" });
        var value = ValueWithKnxMapping<bool>("0/0/1", "DPST-1-1");

        var result = converter.TryConvertValue("ON", value, out var converted);

        Assert.That(result, Is.True);
        Assert.That(converted, Is.EqualTo(true));
    }

    [Test]
    public void TryConvertValue_BitFormat_OFF_ReturnsTrue_WithBooleanFalse()
    {
        var converter = CreateConverter(new() { ["0/0/1"] = "DPST-1-1" });
        var value = ValueWithKnxMapping<bool>("0/0/1", "DPST-1-1");

        var result = converter.TryConvertValue("OFF", value, out var converted);

        Assert.That(result, Is.True);
        Assert.That(converted, Is.EqualTo(false));
    }

    [Test]
    public void TryConvertValue_BitFormat_on_lowercase_ReturnsTrue_WithBooleanTrue()
    {
        var converter = CreateConverter(new() { ["0/0/1"] = "DPST-1-1" });
        var value = ValueWithKnxMapping<bool>("0/0/1", "DPST-1-1");

        var result = converter.TryConvertValue("on", value, out var converted);

        Assert.That(result, Is.True);
        Assert.That(converted, Is.EqualTo(true));
    }

    // ── Tests: FloatFormat (temperature, DPT-9-1) ────────────────────────────

    [Test]
    public void TryConvertValue_FloatFormat_ValidPositiveNumber_ReturnsFloat()
    {
        var converter = CreateConverter(new() { ["0/0/2"] = "DPST-9-1" });
        var value = ValueWithKnxMapping<float>("0/0/2", "DPST-9-1");

        var result = converter.TryConvertValue("21.5", value, out var converted);

        Assert.That(result, Is.True);
        Assert.That(converted, Is.EqualTo(21.5f).Within(0.001f));
    }

    [Test]
    public void TryConvertValue_FloatFormat_NegativeNumber_ReturnsFloat()
    {
        var converter = CreateConverter(new() { ["0/0/2"] = "DPST-9-1" });
        var value = ValueWithKnxMapping<float>("0/0/2", "DPST-9-1");

        var result = converter.TryConvertValue("-5.3", value, out var converted);

        Assert.That(result, Is.True);
        Assert.That(converted, Is.EqualTo(-5.3f).Within(0.01f));
    }

    [Test]
    public void TryConvertValue_FloatFormat_InvalidString_ReturnsFalse()
    {
        var converter = CreateConverter(new() { ["0/0/2"] = "DPST-9-1" });
        var value = ValueWithKnxMapping<float>("0/0/2", "DPST-9-1");

        var result = converter.TryConvertValue("not-a-number", value, out var converted);

        Assert.That(result, Is.False);
        Assert.That(converted, Is.Null);
    }

    // ── Tests: UnsignedIntegerFormat (DPT-5-1, byte/percentage) ─────────────

    [Test]
    public void TryConvertValue_NumericFormat_ValidInteger_ReturnsByte()
    {
        var converter = CreateConverter(new() { ["0/0/3"] = "DPST-5-1" });
        var value = ValueWithKnxMapping<byte>("0/0/3", "DPST-5-1");

        var result = converter.TryConvertValue("100", value, out var converted);

        Assert.That(result, Is.True);
        Assert.That(converted, Is.TypeOf<byte>());
        Assert.That(converted, Is.EqualTo((byte)39));
    }

    // ── Tests: no KNX mapping ─────────────────────────────────────────────────

    [Test]
    public void TryConvertValue_NoKnxMapping_ReturnsFalse()
    {
        var converter = CreateConverter(new());
        var value = ValueWithoutKnxMapping<bool>();

        var result = converter.TryConvertValue("ON", value, out var converted);

        Assert.That(result, Is.False);
        Assert.That(converted, Is.Null);
    }

    // ── Tests: unknown group address ──────────────────────────────────────────

    [Test]
    public void TryConvertValue_UnknownGroupAddress_ReturnsFalse()
    {
        // Converter has no entry for 9/9/9 — DPT resolution will fail
        var converter = CreateConverter(new());
        var value = ValueWithKnxMapping<bool>("9/9/9", "DPST-1-1");

        var result = converter.TryConvertValue("ON", value, out var converted);

        // GetDpt for an unmapped GA throws or returns an error DPT — converter handles gracefully
        Assert.That(result, Is.False);
        Assert.That(converted, Is.Null);
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
}
