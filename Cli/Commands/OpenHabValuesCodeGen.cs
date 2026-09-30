using DotMake.CommandLine;
using HomeCompanion.Support.OpenHab;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SRF.Knx.Config;
using SRF.Knx.Config.OpenHab;
using SRF.Network.OpenHab;

namespace HomeCompanion.Cli.Commands;

[CliCommand(Alias = "ohvcg", Description = "Generates C# code for HomeCompanion values, pulling the items from the live OpenHAB instance", Parent = typeof(Root))]
public class OpenHabValuesCodeGen : HostLauncher<OpenHabValuesCodeGen.Worker>
{
    protected override void AddServices(IServiceCollection services, CliContext cliContext)
    {
        base.AddServices(services, cliContext);
        services.AddOpenHabConfigSupport();
    }

    public class Worker(
        OpenHabValuesCodeGen cmd,
        IHomeCompanionOpenHabConfigFactory openHabConfigFactory,
        IHostApplicationLifetime appLifetime,
        ILogger<OpenHabValuesCodeGen> logger
        ) : BackgroundService
    {
        private readonly OpenHabValuesCodeGen cmd = cmd;
        private readonly IHomeCompanionOpenHabConfigFactory openHabConfigFactory = openHabConfigFactory;
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
