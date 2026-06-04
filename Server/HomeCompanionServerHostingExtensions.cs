using HomeCompanion.Server.Components;
using HomeCompanion.Server.Mcp;
using HomeCompanion.Server.Quartz;
using HomeCompanion.Server.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz;

namespace HomeCompanion.Server;

public static class HomeCompanionServerHostingExtensions
{
    public static IServiceCollection AddHomeCompanionServer(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<McpApiOptions>(configuration.GetSection("HomeCompanion:Mcp"));
        services.Configure<QuartzOptions>(configuration.GetSection("Quartz"));
        services.Configure<QuartzFileStoreOptions>(configuration.GetSection("HomeCompanion:QuartzFileStore"));
        services.AddSingleton<EventBusMonitor>();

        var quartzFileStoreOptions = configuration.GetSection("HomeCompanion:QuartzFileStore").Get<QuartzFileStoreOptions>() ?? new QuartzFileStoreOptions();

        services.AddQuartz(q =>
        {
            if (!string.IsNullOrWhiteSpace(quartzFileStoreOptions.SchedulerName))
                q.SchedulerName = quartzFileStoreOptions.SchedulerName;

            if (!string.IsNullOrWhiteSpace(quartzFileStoreOptions.SchedulerId))
                q.SchedulerId = quartzFileStoreOptions.SchedulerId;

            if (quartzFileStoreOptions.EnableFileStore)
            {
                var dbPath = ResolveStorePath(quartzFileStoreOptions.StoreFilePath);
                var directory = Path.GetDirectoryName(dbPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                q.UsePersistentStore(s =>
                {
                    s.UseProperties = true;
                    s.PerformSchemaValidation = quartzFileStoreOptions.PerformSchemaValidation;
                    s.UseSQLite($"Data Source={dbPath}");
                    s.UseBinarySerializer();
                });
            }
        });

        services.AddQuartzHostedService(options =>
        {
            options.WaitForJobsToComplete = quartzFileStoreOptions.WaitForJobsToComplete;
        });
        services.AddHostedService<QuartzSchedulerConfiguratorHostedService>();

        return services;
    }

    private static string ResolveStorePath(string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            configuredPath = "state/quartz/quartz-store.db";

        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredPath));
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
        app.UseStaticFiles();
        app.UseAntiforgery();

        app.MapHomeCompanionMcp();

        return app;
    }
}
