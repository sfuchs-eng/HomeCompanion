using DotMake.CommandLine;
using HomeCompanion.Support.OpenHab;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Cli.Commands;

[CliCommand(Description = "Generates C# code for HomeCompanion values based on a live OpenHAB instance", Parent = typeof(Root))]
public class OpenHabValuesCodeGen : HostLauncher<OpenHabValuesCodeGen.Worker>
{
    protected override void AddServices(IServiceCollection services, CliContext cliContext)
    {
        base.AddServices(services, cliContext);
        services.AddOpenHabConfigSupport();
    }

    public class Worker(
        OpenHabValuesCodeGen cmd,
        HomeCompanionOpenHabConfigFactory openHabConfigFactory,
        IHostApplicationLifetime appLifetime,
        ILogger<OpenHabValuesCodeGen> logger
        ) : BackgroundService
    {
        private readonly OpenHabValuesCodeGen cmd = cmd;
        private readonly HomeCompanionOpenHabConfigFactory openHabConfigFactory = openHabConfigFactory;
        private readonly IHostApplicationLifetime appLifetime = appLifetime;
        private readonly ILogger<OpenHabValuesCodeGen> logger = logger;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await openHabConfigFactory.GenerateOpenHabItemsValueContainerAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred during C# code generation for OpenHAB values: {Message}", ex.Message);
            }
            finally
            {
                appLifetime.StopApplication();
            }
        }
    }
}
