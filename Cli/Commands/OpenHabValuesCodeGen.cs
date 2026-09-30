using DotMake.CommandLine;
using HomeCompanion.Cli.OpenHab;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SRF.Network.OpenHab;
using SRF.Network.OpenHab.Client;

namespace HomeCompanion.Cli.Commands;

[CliCommand(Description = "Generates C# code for HomeCompanion values based on a live OpenHAB instance", Parent = typeof(Root))]
public class OpenHabValuesCodeGen : HostLauncher<OpenHabValuesCodeGen.Worker>
{
    [CliOption(Alias = "o", Name = "output-dir", Description = "The output directory for the generated C# code files.")]
    public string OutputDirectory { get; set; } = Path.Combine(Directory.GetCurrentDirectory(), "Generated");

    protected override void AddServices(IServiceCollection services, CliContext cliContext)
    {
        base.AddServices(services, cliContext);

        // override config such that the Connector is not started, as we only need the Rest API client to fetch the list of items from OpenHAB
        services.Configure<EventBusClientOptions>(options =>
        {
            options.EnableWebSocket = false;
        });

        services.AddOpenHabConnector();
        services.AddSingleton<OpenHabValuesCodeGenerator>();
    }

    public class Worker(
        OpenHabValuesCodeGen cmd,
        OpenHabValuesCodeGenerator generator,
        IHostApplicationLifetime appLifetime,
        ILogger<OpenHabValuesCodeGen> logger
        ) : BackgroundService
    {
        private readonly OpenHabValuesCodeGen cmd = cmd;
        private readonly OpenHabValuesCodeGenerator generator = generator;
        private readonly IHostApplicationLifetime appLifetime = appLifetime;
        private readonly ILogger<OpenHabValuesCodeGen> logger = logger;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await generator.GenerateAsync(cmd.OutputDirectory, stoppingToken);
                logger.LogInformation("C# code generation completed successfully.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred during C# code generation.");
            }
            finally
            {
                appLifetime.StopApplication();
            }
        }
    }
}