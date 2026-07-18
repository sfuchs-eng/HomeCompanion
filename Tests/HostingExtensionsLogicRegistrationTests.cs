using HomeCompanion.Core;
using HomeCompanion.Diagnostics;
using HomeCompanion.Logics;
using Microsoft.Extensions.DependencyInjection;

namespace HomeCompanion.Tests;

[TestFixture]
public class HostingExtensionsLogicRegistrationTests
{
    private sealed class TestDiagnosableLogic : ILogic, IDiagnosable
    {
        public string Name => nameof(TestDiagnosableLogic);

        public bool IsEnabled { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            IsEnabled = true;
            return Task.CompletedTask;
        }

        public Task EnableAsync(CancellationToken cancellationToken = default)
        {
            IsEnabled = true;
            return Task.CompletedTask;
        }

        public Task DisableAsync(CancellationToken cancellationToken = default)
        {
            IsEnabled = false;
            return Task.CompletedTask;
        }

        public Task<IDiagnosticResultNode> GetDiagnosisAsync(CancellationToken cancellationToken)
            => Task.FromResult<IDiagnosticResultNode>(DiagnosticResultNode.Create(Name));
    }

    [Test]
    public void RegisterLogicType_RegistersDiagnosableTypes_AsConcreteLogicAndDiagnosableService()
    {
        var services = new ServiceCollection();

        HostingExtensions.RegisterLogicType(services, typeof(TestDiagnosableLogic));

        using var provider = services.BuildServiceProvider();

        var concrete = provider.GetRequiredService<TestDiagnosableLogic>();
        var diagnosable = provider.GetRequiredService<IDiagnosable>();

        Assert.Multiple(() =>
        {
            Assert.That(concrete, Is.Not.Null);
            Assert.That(diagnosable, Is.SameAs(concrete));
        });
    }
}