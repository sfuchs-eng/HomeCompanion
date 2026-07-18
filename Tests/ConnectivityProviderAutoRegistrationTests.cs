using HomeCompanion.Core;
using HomeCompanion.Integrations.OpenHab;
using Microsoft.Extensions.DependencyInjection;

namespace HomeCompanion.Tests;

[TestFixture]
public class ConnectivityProviderAutoRegistrationTests
{
    [Test]
    public void AddConnectivityProviders_DoesNotAutoRegister_ManualProviders()
    {
        var services = new ServiceCollection();

        services.AddConnectivityProviders();

        Assert.That(
            services.Any(sd => sd.ServiceType == typeof(OpenHabConnectivityProvider)),
            Is.False,
            "OpenHabConnectivityProvider must be registered only through OpenHabExtensionRegistration.");
    }
}
