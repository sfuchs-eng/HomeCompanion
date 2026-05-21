using HomeCompanion.Core;
using Microsoft.Extensions.Configuration;
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
/// Integration tests that verify the DI chain required for <see cref="KnxDptResolver"/> to work.
/// Covers the concern from TODO 1.3: is <see cref="DomainConfiguration"/> correctly wired into
/// <see cref="IDptResolver"/> via the HomeCompanion hosting extensions?
/// </summary>
[TestFixture]
public class KnxDptResolverIntegrationTests
{
    // ── Test 1: KnxDptResolver correctly resolves DPT from an inline DomainConfiguration ────────

    /// <summary>
    /// Verifies that <see cref="KnxDptResolver.GetDpt"/> returns the DPT matching the ETS entry
    /// for the given group address. DomainConfiguration is constructed inline — no file I/O needed.
    /// </summary>
    [Test]
    public void GetDpt_WithInlineDomainConfiguration_ReturnsDptMatchingEtsEntry()
    {
        // Arrange — build DomainConfiguration directly from known data
        var ga = new GroupAddress("0/0/1");
        var etsCfg = new EtsGroupAddressConfig
        {
            Address = new GroupAddress("0/0/1"),
            Label = "Aussentemperatur",
            DPTs = "DPST-9-1",   // 2-byte float (temperature)
        };
        var domainConfig = new DomainConfiguration
        {
            GroupAddresses = { [ga.Address] = etsCfg },
        };

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddKnxCore();
        services.AddSingleton<IKnxMasterDataProvider>(KnxMasterDataProviderStub.Create());
        services.AddSingleton(domainConfig);
        services.TryAddSingleton<IDptResolver, KnxDptResolver>();
        var sp = services.BuildServiceProvider(validateScopes: false);

        var resolver = sp.GetRequiredService<IDptResolver>();

        // Act
        var dpt = resolver.GetDpt(ga);

        // Assert
        Assert.That(dpt, Is.Not.Null);
        Assert.That(dpt.Id.Main, Is.EqualTo(9));
        Assert.That(dpt.Id.Sub, Is.EqualTo(1));
    }

    // ── Test 2: AddKnxConfig registers DomainConfiguration ───────────────────────────────────────

    /// <summary>
    /// Verifies that <see cref="ExtensionsHosting.AddKnxConfig"/> registers
    /// <see cref="DomainConfiguration"/> as a singleton in the DI container, and that when the
    /// configured ETS file does not exist, the factory returns a blank (non-null) configuration
    /// rather than throwing.
    /// </summary>
    [Test]
    public void AddKnxConfig_RegistersDomainConfigurationSingleton()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([new("Knx:System:EtsGAExportFile", "/nonexistent/GroupAddressExport.xml")])
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(TimeProvider.System);
        services.AddKnxConfig();

        var sp = services.BuildServiceProvider(validateScopes: false);

        // DomainConfiguration must resolve — file-not-found returns blank (non-null) config.
        var domainConfig = sp.GetRequiredService<DomainConfiguration>();

        Assert.That(domainConfig, Is.Not.Null);
        Assert.That(domainConfig.GroupAddresses, Is.Empty,
            "A missing ETS file must yield an empty DomainConfiguration, not throw");
    }

    // ── Test 3: AddKnxConnections wires DomainConfiguration and IDptResolver into DI ─────────────

    /// <summary>
    /// Verifies the full HomeCompanion hosting path: <c>AddKnxConnections</c> →
    /// <c>AddKnxIpRouting</c> → <c>AddKnxConfig</c> registers both
    /// <see cref="DomainConfiguration"/> and <see cref="IDptResolver"/> in the DI container,
    /// including <see cref="IKnxMasterDataProvider"/> → <see cref="SRF.Knx.Config.KnxMasterDataProvider"/>.
    /// Also verifies that <see cref="IDptResolver"/> resolves to the same singleton that
    /// implements <see cref="IKnxSystemConfiguration"/>.
    /// </summary>
    [Test]
    public void AddKnxConnections_RegistersDomainConfigurationAndDptResolver()
    {
        var baseDir = Path.GetDirectoryName(typeof(KnxDptResolverIntegrationTests).Assembly.Location) ?? "";
        var knxMasterFolder = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..",
            "SRF.Network", "Subs", "SRF.Knx", "SRF.Knx.Config", "Resources"));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([
                new("Knx:System:EtsGAExportFile", "/nonexistent/GroupAddressExport.xml"),
                new("Knx:System:KnxMasterFolder", knxMasterFolder),
                new("Knx:Connections:default:MulticastAddress", "224.0.23.12"),
                new("Knx:Connections:default:Port", "3671"),
            ])
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        Integrations.Knx.KnxExtensionRegistration.AddKnxConnections(services, config);

        var sp = services.BuildServiceProvider(validateScopes: false);

        var domainConfig = sp.GetRequiredService<DomainConfiguration>();
        Assert.That(domainConfig, Is.Not.Null,
            "DomainConfiguration should be registered by AddKnxConnections → AddKnxIpRouting → AddKnxConfig");

        var dptResolver = sp.GetRequiredService<IDptResolver>();
        var systemConfig = sp.GetRequiredService<IKnxSystemConfiguration>();

        Assert.That(dptResolver, Is.AssignableTo<IKnxSystemConfiguration>(),
            "IDptResolver should resolve via IKnxSystemConfiguration through the full DI chain");
        Assert.That(dptResolver, Is.SameAs(systemConfig),
            "IDptResolver and IKnxSystemConfiguration should be the same singleton instance");
    }

    // ── Stub ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads <c>knx_master.xml</c> from the <c>SRF.Knx.Config/Resources/</c> folder, navigating
    /// relative to the test assembly output directory (same strategy as
    /// <c>SRF.Knx.Test/Core/DptFactoryTests.cs</c>).
    /// </summary>
    private sealed class KnxMasterDataProviderStub(KnxMasterData masterData) : IKnxMasterDataProvider
    {
        public KnxMasterData GetMasterData() => masterData;

        public static KnxMasterDataProviderStub Create()
        {
            var baseDir = Path.GetDirectoryName(typeof(KnxMasterDataProviderStub).Assembly.Location) ?? "";
            var path = Path.GetFullPath(
                Path.Combine(baseDir, "..", "..", "..", "..",
                    "SRF.Network", "Subs", "SRF.Knx", "SRF.Knx.Config", "Resources", "knx_master.xml"));

            if (!File.Exists(path))
                Assert.Fail($"knx_master.xml not found at: {path}");

            return new KnxMasterDataProviderStub(KnxMasterDataLoader.LoadFromFile(path));
        }
    }
}
