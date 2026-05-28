using HomeCompanion.Server.Components;
using HomeCompanion.Server.Mcp;
using HomeCompanion.Server.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HomeCompanion.Server;

public static class HomeCompanionServerHostingExtensions
{
    public static IServiceCollection AddHomeCompanionServer(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRazorComponents()
            .AddInteractiveServerComponents();

        services.Configure<McpApiOptions>(configuration.GetSection("HomeCompanion:Mcp"));
        services.AddSingleton<EventBusMonitor>();

        return services;
    }

    public static WebApplication MapHomeCompanionServer(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
            app.UseHsts();
        }

        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseHttpsRedirection();
        app.UseAntiforgery();

        app.MapHomeCompanionMcp();

        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        return app;
    }
}
