using DotMake.CommandLine;
using HomeCompanion.Support.Knx;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SRF.Knx.Config;
using SRF.Knx.Config.OpenHab;
using SRF.Knx.Core;

namespace HomeCompanion.Cli.Commands;

[CliCommand(Description = "Updates the HomeCompanion and OpenHAB KNX configurations based on a provided ETS GroupAddress export file.", Parent = typeof(Root))]
public class KnxConfiguration : HostLauncher<KnxConfiguration.Worker>
{
    [CliOption(Alias = "su", Name = "save-update", Description = "Save the updated HomeCompanion and OpenHAB KNX configurations based on a provided ETS GroupAddress export file. Default true, set to false to only update in memory without saving.")]
    public bool SaveUpdate { get; set; } = true;

    [CliOption(Alias = "oh", Name = "openhab-config", Description = "Generate OpenHAB KNX configuration files based on the provided ETS GroupAddress export file. Default true, set to false to skip generation.")]
    public bool GenerateOpenHabConfig { get; set; } = true;

    [CliOption(Alias = "hc", Name = "homecompanion-code", Description = "Generate HomeCompanion KNX code based on the provided ETS GroupAddress export file. Default true, set to false to skip generation.")]
    public bool GenerateHomeCompanionCode { get; set; } = true;

    protected override void AddServices(IServiceCollection services, CliContext cliContext)
    {
        base.AddServices(services, cliContext);
        services.AddKnxCore();
        services.AddKnxConfig();
        services.AddKnxOpenHabConfig();
        services.AddSingleton<KnxValuesCodeGenerator>();
        services.AddSingleton<IHomeCompanionKnxConfigFactory, HomeCompanionKnxConfigFactory>();
    }

    public class Worker(
        KnxConfiguration cmd,
        //        IKnxMasterDataProvider knxMasterDataProvider,
        //        IDptFactory dptFactory,
        //        IDptResolver dptResolver,
        //        IOptions<KnxSystemConfigOptions> knxSystemConfigOptions,
        //        IDomainConfigurationFactory domainConfigurationFactory,
        IKnxConfigFactory knxConfigFactory,
        IOpenHabKnxConfigFactory openHabKnxConfigFactory,
        IHomeCompanionKnxConfigFactory homeCompanionKnxConfigFactory,
        IHostApplicationLifetime appLifetime,
        ILogger<KnxConfiguration> logger
        ) : BackgroundService
    {
        private readonly KnxConfiguration cmd = cmd;
//        private readonly IKnxMasterDataProvider knxMasterDataProvider = knxMasterDataProvider;
//        private readonly IDptFactory dptFactory = dptFactory;
//        private readonly IDptResolver dptResolver = dptResolver;
//        private readonly IOptions<KnxSystemConfigOptions> knxSystemConfigOptions = knxSystemConfigOptions;
//        private readonly IDomainConfigurationFactory domainConfigurationFactory = domainConfigurationFactory;
        private readonly IKnxConfigFactory knxConfigFactory = knxConfigFactory;
        private readonly IOpenHabKnxConfigFactory openHabKnxConfigFactory = openHabKnxConfigFactory;
        private readonly IHomeCompanionKnxConfigFactory homeCompanionKnxConfigFactory = homeCompanionKnxConfigFactory;
        private readonly IHostApplicationLifetime appLifetime = appLifetime;
        private readonly ILogger<KnxConfiguration> logger = logger;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (cmd.SaveUpdate)
            {
                await UpdateKnxConfigurationAsync(stoppingToken);
            }
            else
                logger.LogInformation("KNX configuration updated but not saved. Use --save-update to save changes.");

            if (cmd.GenerateOpenHabConfig)
            {
                var ohc = openHabKnxConfigFactory.Get();
                await openHabKnxConfigFactory.WriteOpenHabConfigFilesAsync(ohc);
            }
            else
                logger.LogInformation("OpenHAB KNX configuration files not generated. Use --openhab-config to generate them.");
            
            if (cmd.GenerateHomeCompanionCode)
            {
                await homeCompanionKnxConfigFactory.UpdateHomeCompanionCodeFilesAsync(cancellationToken: stoppingToken);
            }
            else
                logger.LogInformation("HomeCompanion KNX code not generated. Use --homecompanion-code to generate it.");

            if (!stoppingToken.IsCancellationRequested)
                appLifetime.StopApplication();
        }

        protected async Task UpdateKnxConfigurationAsync(CancellationToken stoppingToken)
        {
            // Retrieve the current domain configuration, which is loaded from the configured files or created fresh if not present.
            var dc = knxConfigFactory.GetDomainConfig();
            knxConfigFactory.SaveDomainConfig(dc);

            // HomeCompanion KNX configuration update
            var ohc = openHabKnxConfigFactory.Get(dc);
            await openHabKnxConfigFactory.SaveAsync(ohc);

            // HomeCompanion KNX related configuration is not persisted to disk (so far), but the generated code files are written to the configured paths.
        }
    }
}