using HomeCompanion.Extensions;
using HomeCompanion.Integrations.Sonos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.IO;

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

    [Test]
    public void SonosPage_UsesInteractiveServerRenderMode()
    {
        var pagePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Integrations.Sonos/Pages/SonosPage.razor"));
        var source = File.ReadAllText(pagePath);

        Assert.That(source, Does.Contain("@rendermode InteractiveServer"));
    }

    [Test]
    public void HostApp_ExposesSonosRoute()
    {
        var pagePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../Server/Components/Pages/Sonos.razor"));
        var source = File.ReadAllText(pagePath);

        Assert.That(source, Does.Contain("@page \"/sonos\""));
        Assert.That(source, Does.Contain("<SonosPage />"));
    }

    private sealed class TestExtensionRegistrationContext : IExtensionRegistrationContext
    {
        public required IHostApplicationBuilder Builder { get; init; }
    }
}
