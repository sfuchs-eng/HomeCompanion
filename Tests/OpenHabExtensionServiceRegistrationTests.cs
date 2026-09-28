using HomeCompanion.Abstractions;
using HomeCompanion.Extensions;
using HomeCompanion.Integrations.OpenHab;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SRF.Network.OpenHab;
using SRF.Network.OpenHab.Client;

namespace HomeCompanion.Tests;

[TestFixture]
public class OpenHabExtensionServiceRegistrationTests
{
    [Test]
    public void RegisterServices_DoesNotRegisterOpenHabServices_WhenDisabled()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OpenHAB:Enable"] = "false",
        });

        var sut = new OpenHabExtensionRegistration(NullLogger<OpenHabExtensionRegistration>.Instance);
        sut.RegisterServices(new TestExtensionRegistrationContext { Builder = builder });

        Assert.Multiple(() =>
        {
            Assert.That(builder.Services.Any(sd => sd.ImplementationType == typeof(OpenHabConnector)), Is.False);
            Assert.That(builder.Services.Any(sd => sd.ImplementationType == typeof(OpenHabConnectivityProvider)), Is.False);
            Assert.That(builder.Services.Any(sd => sd.ImplementationType == typeof(OpenHabExtensionRegistrationBackgroundService)), Is.False);
            Assert.That(builder.Services.Any(sd => sd.ServiceType == typeof(IConnectivityProvider)), Is.False);
            Assert.That(builder.Services.Any(sd => sd.ServiceType == typeof(IEventBusClient)), Is.False);
            Assert.That(builder.Services.Any(sd => sd.ServiceType == typeof(IRestApiClient)), Is.False);
        });
    }

    [Test]
    public void RegisterServices_RegistersOpenHabServices_WhenEnabled()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OpenHAB:Enable"] = "true",
        });

        var sut = new OpenHabExtensionRegistration(NullLogger<OpenHabExtensionRegistration>.Instance);
        sut.RegisterServices(new TestExtensionRegistrationContext { Builder = builder });

        Assert.Multiple(() =>
        {
            Assert.That(builder.Services.Any(sd => sd.ImplementationType == typeof(OpenHabConnector)), Is.True);
            Assert.That(builder.Services.Any(sd => sd.ImplementationType == typeof(OpenHabConnectivityProvider)), Is.True);
            Assert.That(builder.Services.Any(sd => sd.ImplementationType == typeof(OpenHabExtensionRegistrationBackgroundService)), Is.True);
            Assert.That(builder.Services.Any(sd => sd.ServiceType == typeof(IConnectivityProvider)), Is.True);
            Assert.That(builder.Services.Any(sd => sd.ServiceType == typeof(IEventBusClient) && sd.ImplementationType == typeof(EventBusClient)), Is.True);
            Assert.That(builder.Services.Any(sd => sd.ServiceType == typeof(IRestApiClient)), Is.True);
        });
    }

    private sealed class TestExtensionRegistrationContext : IExtensionRegistrationContext
    {
        public required IHostApplicationBuilder Builder { get; init; }
    }
}
