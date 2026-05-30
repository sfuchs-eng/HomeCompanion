using HomeCompanion.Extensions;
using HomeCompanion.Persistence;
using Microsoft.Extensions.Configuration;
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
        var configuration = context.Builder.Configuration;
        var url = configuration[$"{InfluxIntegrationOptions.SectionName}:Url"];
        var organization = configuration[$"{InfluxIntegrationOptions.SectionName}:Organization"];
        var token = configuration[$"{InfluxIntegrationOptions.SectionName}:Token"];
        var defaultBucket = configuration[$"{InfluxIntegrationOptions.SectionName}:DefaultBucket"];

        if (string.IsNullOrWhiteSpace(url)
            || string.IsNullOrWhiteSpace(organization)
            || string.IsNullOrWhiteSpace(token)
            || string.IsNullOrWhiteSpace(defaultBucket))
        {
            Console.Error.WriteLine("Influx integration disabled: required configuration values are missing.");

            context.Builder.Services.AddSingleton<DisabledInfluxSignalStore>();
            context.Builder.Services.AddSingleton<ISignalStore>(sp => sp.GetRequiredService<DisabledInfluxSignalStore>());
            context.Builder.Services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<DisabledInfluxSignalStore>());
            return;
        }

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
        context.Builder.Services.AddSingleton<InfluxSignalStore>();
        context.Builder.Services.AddSingleton<ISignalStore>(sp => sp.GetRequiredService<InfluxSignalStore>());
        context.Builder.Services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<InfluxSignalStore>());
    }
}
