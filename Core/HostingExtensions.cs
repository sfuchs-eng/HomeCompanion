using HomeCompanion.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SRF.Network.Knx;

namespace HomeCompanion.Core;

public static class HostingExtensions
{
    /// <summary>
    /// Adds HomeCompanion core services to the dependency injection container.
    /// </summary>
    /// <param name="builder">The IHostApplicationBuilder to add services to.</param>
    /// <returns>The modified IHostApplicationBuilder for chaining.</returns>
    public static IHostApplicationBuilder AddHomeCompanionCore(this IHostApplicationBuilder builder)
    {
        builder.Services.AddEventBus();
        builder.AddKnxIpRouting("default");

        return builder;
    }

    /// <summary>
    /// Registers the <see cref="EventBus"/> singleton as <see cref="IEventPublisher"/>,
    /// <see cref="IEventSubscriber"/>, and <see cref="IHostedService"/>.
    /// </summary>
    public static IServiceCollection AddEventBus(this IServiceCollection services)
    {
        services.AddSingleton<EventBus>();
        services.AddSingleton<IEventPublisher>(sp => sp.GetRequiredService<EventBus>());
        services.AddSingleton<IEventSubscriber>(sp => sp.GetRequiredService<EventBus>());
        services.AddHostedService(sp => sp.GetRequiredService<EventBus>());

        return services;
    }
}
