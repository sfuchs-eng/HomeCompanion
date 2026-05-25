using HomeCompanion.Alerting;
using HomeCompanion.Extensions;
using HomeCompanion.Integrations.Alerting.Named;
using HomeCompanion.Integrations.Alerting.Providers;
using HomeCompanion.Integrations.Alerting.Values;
using HomeCompanion.Values;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HomeCompanion.Integrations.Alerting;

/// <summary>
/// Registers services for the alerting extension.
/// </summary>
public sealed class AlertingExtensionRegistration : IExtensionRegistration
{
    /// <inheritdoc/>
    public void RegisterServices(IExtensionRegistrationContext context)
    {
        var services = context.Builder.Services;

        services.AddOptions<AlertingIntegrationOptions>()
            .BindConfiguration(AlertingIntegrationOptions.SectionName);

        services.AddSingleton<NamedAlertStateMachine>();
        services.AddSingleton<NamedAlertPersistenceAdapter>();

        services.AddSingleton<AlertingValues>();
        services.AddSingleton<IValuesContainer>(sp => sp.GetRequiredService<AlertingValues>());

        services.AddSingleton<IAlertChannelProvider, PushMessageAlertChannelProvider>();
        services.AddSingleton<IAlertChannelProvider, EmailAlertChannelProvider>();

        services.AddSingleton<IAlertingService, AlertingService>();

        services.AddHostedService<NamedAlertStateInitializationHostedService>();
    }
}
