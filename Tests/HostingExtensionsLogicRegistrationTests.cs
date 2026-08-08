using HomeCompanion.Core;
using HomeCompanion.Diagnostics;
using HomeCompanion.Logics;
using Microsoft.Extensions.DependencyInjection;

namespace HomeCompanion.Tests;

[TestFixture]
public class HostingExtensionsLogicRegistrationTests
{
    private abstract class TestLogicBase : ILogic
    {
        public string Name => GetType().Name;
        public bool IsEnabled { get; private set; }
        public bool IsActivated => IsEnabled;
        public Exception? ActivationException => null;

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

        public Task TerminateAsync(CancellationToken cancellationToken = default)
        {
            IsEnabled = false;
            return Task.CompletedTask;
        }
    }

    private sealed class UnrestrictedLogic : TestLogicBase;

    [LoadInEnvironments("Development", "Offline")]
    private sealed class AttributeRestrictedLogic : TestLogicBase;

    [LoadInEnvironments("Production")]
    private sealed class MergeRestrictedLogic : TestLogicBase;

    private sealed class FullNameLookupLogic : TestLogicBase;

    private sealed class TestDiagnosableLogic : ILogic, IDiagnosable
    {
        public string Name => nameof(TestDiagnosableLogic);

        public bool IsEnabled { get; private set; }
        public bool IsActivated => IsEnabled;
        public Exception? ActivationException => null;

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

        public Task TerminateAsync(CancellationToken cancellationToken = default)
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

    [Test]
    public void ShouldRegisterLogicType_NoRules_ReturnsTrue()
    {
        var result = HostingExtensions.ShouldRegisterLogicType(
            typeof(UnrestrictedLogic),
            "Development",
            configuredEnvironmentRules: null,
            out var reason);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(reason, Does.Contain("unrestricted"));
        });
    }

    [Test]
    public void ShouldRegisterLogicType_AttributeAllowsEnvironment_ReturnsTrue()
    {
        var result = HostingExtensions.ShouldRegisterLogicType(
            typeof(AttributeRestrictedLogic),
            "development",
            configuredEnvironmentRules: null,
            out _);

        Assert.That(result, Is.True);
    }

    [Test]
    public void ShouldRegisterLogicType_AttributeExcludesEnvironment_ReturnsFalse()
    {
        var result = HostingExtensions.ShouldRegisterLogicType(
            typeof(AttributeRestrictedLogic),
            "Production",
            configuredEnvironmentRules: null,
            out _);

        Assert.That(result, Is.False);
    }

    [Test]
    public void ShouldRegisterLogicType_ConfigOnlyAllowsEnvironment_ReturnsTrue()
    {
        var rules = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(UnrestrictedLogic)] = ["Offline", "Production"],
        };

        var result = HostingExtensions.ShouldRegisterLogicType(
            typeof(UnrestrictedLogic),
            "offline",
            rules,
            out _);

        Assert.That(result, Is.True);
    }

    [Test]
    public void ShouldRegisterLogicType_ConfigOnlyExcludesEnvironment_ReturnsFalse()
    {
        var rules = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(UnrestrictedLogic)] = ["Production"],
        };

        var result = HostingExtensions.ShouldRegisterLogicType(
            typeof(UnrestrictedLogic),
            "Development",
            rules,
            out _);

        Assert.That(result, Is.False);
    }

    [Test]
    public void ShouldRegisterLogicType_AttributeAndConfig_UsesUnion()
    {
        var rules = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(MergeRestrictedLogic)] = ["Development"],
        };

        var result = HostingExtensions.ShouldRegisterLogicType(
            typeof(MergeRestrictedLogic),
            "Development",
            rules,
            out var reason);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(reason, Does.Contain("attribute + configuration"));
            Assert.That(reason, Does.Contain("Development"));
            Assert.That(reason, Does.Contain("Production"));
        });
    }

    [Test]
    public void ShouldRegisterLogicType_AttributeAndConfigUnionExcludesEnvironment_ReturnsFalse()
    {
        var rules = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(MergeRestrictedLogic)] = ["Development"],
        };

        var result = HostingExtensions.ShouldRegisterLogicType(
            typeof(MergeRestrictedLogic),
            "Offline",
            rules,
            out _);

        Assert.That(result, Is.False);
    }

    [Test]
    public void ShouldRegisterLogicType_FullNameKey_PrecedesTypeNameKey()
    {
        var rules = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(FullNameLookupLogic)] = ["Offline"],
            [typeof(FullNameLookupLogic).FullName!] = ["Production"],
        };

        var result = HostingExtensions.ShouldRegisterLogicType(
            typeof(FullNameLookupLogic),
            "Production",
            rules,
            out var reason);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(reason, Does.Contain(typeof(FullNameLookupLogic).FullName!));
            Assert.That(reason, Does.Not.Contain("Offline"));
        });
    }

    [Test]
    public void ShouldRegisterLogicType_WhitespaceEntries_AreIgnored()
    {
        var rules = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(UnrestrictedLogic)] = [" ", "\t", "Offline"],
        };

        var result = HostingExtensions.ShouldRegisterLogicType(
            typeof(UnrestrictedLogic),
            "Offline",
            rules,
            out var reason);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(reason, Does.Contain("Offline"));
        });
    }

    [Test]
    public void ShouldRegisterLogicType_ConfigEntryWithoutValidEnvironments_TreatedAsUnconfigured()
    {
        var rules = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(UnrestrictedLogic)] = ["", " ", "\t"],
        };

        var result = HostingExtensions.ShouldRegisterLogicType(
            typeof(UnrestrictedLogic),
            "Development",
            rules,
            out var reason);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(reason, Does.Contain("unrestricted"));
        });
    }
}