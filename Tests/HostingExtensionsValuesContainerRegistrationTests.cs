using HomeCompanion.Core;
using HomeCompanion.Values;
using Microsoft.Extensions.DependencyInjection;

namespace HomeCompanion.Tests;

[TestFixture]
public class HostingExtensionsValuesContainerRegistrationTests
{
    [Test]
    public void AddValuesContainers_RegistersPublicContainers_AndSkipsNonPublicContainers()
    {
        var services = new ServiceCollection();

        services.AddValuesContainers();

        Assert.Multiple(() =>
        {
            Assert.That(
                services.Any(d => d.ServiceType == typeof(PublicTestValuesContainer)),
                Is.True,
                "Public IValuesContainer implementations should be discovered via exported-type scanning.");

            Assert.That(
                services.Any(d => d.ServiceType == typeof(InternalTestValuesContainer)),
                Is.False,
                "Non-public IValuesContainer implementations must not be discovered via exported-type scanning.");
        });
    }

    public sealed class PublicTestValuesContainer : IValuesContainer
    {
        public IEnumerable<IValue> GetValues() => [];
    }

    private sealed class InternalTestValuesContainer : IValuesContainer
    {
        public IEnumerable<IValue> GetValues() => [];
    }
}
