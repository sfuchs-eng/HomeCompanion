using HomeCompanion.Extensions;
using HomeCompanion.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HomeCompanion.Integrations.Influx;

/// <summary>
/// Registers internal-signal Influx integration services.
/// </summary>
public sealed class InfluxSignalStoreExtensionRegistration : IExtensionRegistration
{
    /// <inheritdoc />
    public void RegisterServices(IExtensionRegistrationContext context)
    {
        context.Builder.Services
            .AddOptions<InfluxIntegrationOptions>()
            .BindConfiguration(InfluxIntegrationOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(o => !string.IsNullOrWhiteSpace(o.Url), "Influx:Url is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Organization), "Influx:Organization is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Token), "Influx:Token is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.DefaultBucket), "Influx:DefaultBucket is required.")
            .ValidateOnStart();

        context.Builder.Services.AddSingleton<IInfluxBatchWriter, InfluxBatchWriter>();
        context.Builder.Services.AddSingleton<InfluxInternalSignalStore>();
        context.Builder.Services.AddSingleton<IInternalSignalStore>(sp => sp.GetRequiredService<InfluxInternalSignalStore>());
        context.Builder.Services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<InfluxInternalSignalStore>());
    }
}
