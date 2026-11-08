using HomeCompanion.Extensions;
using HomeCompanion.Integrations.Sonos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HomeCompanion.Integrations.Sonos.Tests;

[TestFixture]
public class SonosExtensionRegistrationTests
{
    [Test]
    public void RegisterServices_RegistersSonosServices()
    {
        var builder = Host.CreateApplicationBuilder();
        var sut = new SonosExtensionRegistration();

        sut.RegisterServices(new TestExtensionRegistrationContext { Builder = builder });

        Assert.Multiple(() =>
        {
            Assert.That(builder.Services.Any(sd => sd.ServiceType == typeof(ISonosTransportService)), Is.True);
            Assert.That(builder.Services.Any(sd => sd.ServiceType == typeof(ISonosDiscoveryService)), Is.True);
            Assert.That(builder.Services.Any(sd => sd.ServiceType == typeof(ISonosSystemService)), Is.True);
            Assert.That(builder.Services.Any(sd => sd.ServiceType == typeof(ISonosTopologyService)), Is.True);
            Assert.That(builder.Services.Any(sd => sd.ServiceType == typeof(ISonosPlaybackService)), Is.True);
            Assert.That(builder.Services.Any(sd => sd.ServiceType == typeof(ISonosQueueService)), Is.True);
            Assert.That(builder.Services.Any(sd => sd.ServiceType == typeof(ISonosFavoritesService)), Is.True);
            Assert.That(builder.Services.Any(sd => sd.ServiceType == typeof(ISonosAlarmService)), Is.True);
            Assert.That(builder.Services.Any(sd => sd.ServiceType == typeof(ISonosStateRestoreService)), Is.True);
        });
    }

    private sealed class TestExtensionRegistrationContext : IExtensionRegistrationContext
    {
        public required IHostApplicationBuilder Builder { get; init; }
    }
}
