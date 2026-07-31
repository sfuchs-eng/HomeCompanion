using HomeCompanion.Core;
using HomeCompanion.Diagnostics;
using HomeCompanion.Values;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HomeCompanion.Tests;

[TestFixture]
public class HostingExtensionsCoreDiagnosticsTests
{
    [Test]
    public void AddHomeCompanionCore_RegistersCoreDiagnosables()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddHomeCompanionCore();

        var diagnosableDescriptors = builder.Services
            .Where(d => d.ServiceType == typeof(IDiagnosable))
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(builder.Services.Any(d => d.ServiceType == typeof(ValuesManager)), Is.True);
            Assert.That(builder.Services.Any(d => d.ServiceType == typeof(HomeCompanionLifeCycleSynchronization)), Is.True);
            Assert.That(diagnosableDescriptors.Length, Is.GreaterThanOrEqualTo(2));
            Assert.That(diagnosableDescriptors.All(d => d.ImplementationFactory is not null || d.ImplementationType is not null), Is.True);
        });
    }
}