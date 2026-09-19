using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SRF.Knx.Config;
using SRF.Knx.Config.Domain;
using SRF.Knx.Config.OpenHab;
using SRF.Knx.Config.OpenHab.BaseConfig;
using SRF.Knx.Core;
using SRF.Knx.Core.DPT;

namespace HomeCompanion.Support.Knx;

public class HomeCompanionKnxConfigFactory(
    IOptions<KnxSystemConfigOptions> options,
    IKnxConfigFactory knxConfigFactory,
    IOpenHabKnxConfigFactory openHabKnxConfigFactory,
    IUnitSystemsMapper unitSystemsMapper,
    ILogger<HomeCompanionKnxConfigFactory> logger,
    ILoggerFactory loggerFactory,
    ILabelToNameConverter labelToNameConverter,
    IDptFactory dptFactory,
    IKnxMasterDataProvider knxMasterDataProvider,
    KnxValuesCodeGenerator knxValuesCodeGenerator
) : IHomeCompanionKnxConfigFactory
{
    private readonly KnxSystemConfigOptions config = options.Value;
    private readonly IKnxConfigFactory knxConfigFactory = knxConfigFactory;
    private readonly IOpenHabKnxConfigFactory openHabKnxConfigFactory = openHabKnxConfigFactory;
    private readonly IUnitSystemsMapper unitSystemsMapper = unitSystemsMapper;
    private readonly ILogger<HomeCompanionKnxConfigFactory> logger = logger;
    private readonly ILoggerFactory loggerFactory = loggerFactory;
    private readonly ILabelToNameConverter labelToNameConverter = labelToNameConverter;
    private readonly IDptFactory dptFactory = dptFactory;
    private readonly IKnxMasterDataProvider knxMasterDataProvider = knxMasterDataProvider;

    /// <summary>
    /// Writes the generated <c>KnxValues.generated.cs</c> and <c>HomeCompanionKnxAutoGen.json</c> files to the paths configured in <see cref="KnxSystemConfigOptions"/>.<br/>
    /// The generated files are based on the provided <see cref="DomainConfiguration"/>.<br/>
    /// If <paramref name="postProcessEntries"/> is provided, it will be invoked with the generated <see cref="KnxValueConfiguration"/> entries before writing the files.<br/>
    /// </summary>
    /// <param name="postProcessEntries"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task UpdateHomeCompanionCodeFilesAsync(Action<Dictionary<string, KnxValueConfiguration>>? postProcessEntries = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(config.HomeCompanion.KnxValuesCodeGenFilePath))
        {
            logger.LogError("KnxValuesCodeGenFilePath is not configured. Set it in your local SRF.Network.json to the path of HomeCompanion.Knx/KnxValues.generated.cs.");
            return;
        }
        //        var dc = knxConfigFactory.GetDomainConfig();
        //        var ohc = openHabKnxConfigFactory.Get(dc);
        var dc = knxConfigFactory.GetDomainConfig();
        var ohc = openHabKnxConfigFactory.Get();
        var code = config.LinkKnxValuesToOpenHabForInitialization
            ? GenerateHomeCompanionCode(dc, entries => AddOpenHabItemNamesFromOhConfig(entries, ohc))
            : GenerateHomeCompanionCode(dc);
        File.WriteAllText(config.HomeCompanion.KnxValuesCodeGenFilePath, code, System.Text.Encoding.UTF8);
        logger.LogInformation("Generated KnxValues source with {count} properties and wrote to '{file}'",
            dc.GroupAddresses.Count,
            config.HomeCompanion.KnxValuesCodeGenFilePath);
    }

    /// <summary>
    /// Generates the <see cref="KnxValueConfiguration"/> entries from the provided <see cref="DomainConfiguration"/>.<br/>
    /// The generated entries are used to create the <c>KnxValues.generated.cs</c> source file and the <c>HomeCompanionKnxAutoGen.json</c> mapping file.<br/>
    /// If <paramref name="postProcessEntries"/> is provided, it will be invoked with the generated entries before returning them.<br/>
    /// </summary>
    /// <param name="config"></param>
    /// <returns></returns>
    public Dictionary<string, KnxValueConfiguration> GenerateIValueConfigurations(DomainConfiguration config)
    {
        var result = new Dictionary<string, KnxValueConfiguration>();
        foreach (var kvp in config.GroupAddresses)
        {
            var extra = config.Extra.TryGetGAExtraConfig(kvp.Value.Address, out var extraConfig) ? extraConfig : null;
            var address3L = kvp.Key.To3LGroupAddress();
            var gac = kvp.Value;
            var name = extra != null && !string.IsNullOrEmpty(extra.Name)
                ? extra.Name
                : labelToNameConverter.GetName(gac);
            var comms = KnxObjectBusCommunication.Write | KnxObjectBusCommunication.Transmit | KnxObjectBusCommunication.Update;
            if (extra?.HomeCompanion?.AnswerReadRequests ?? false)
                comms |= KnxObjectBusCommunication.Read;
            if (extra?.HomeCompanion?.InitializeFromKnxBus ?? false)
                comms |= KnxObjectBusCommunication.Initialize;

            var kv = new KnxValueConfiguration
            {
                PropertyName = name,
                Label = string.IsNullOrWhiteSpace(gac.Label) ? null : gac.Label,
                Description = string.IsNullOrWhiteSpace(gac.Description) ? null : gac.Description,
                Dpt = string.IsNullOrEmpty(gac.DPTs) ? null : gac.DPTs,
                Communication = comms,
                WantsOpenHabInitialization = extra?.HomeCompanion?.InitializeFromOpenHab ?? false,
            };
            result[address3L] = kv;

            // does it need to be unit aware? Consult KNX DPT master data for the DPT and check if it has a unit. If so, add the unit to the description.
            if (gac.DPT is not null)
            {
                var dpt = dptFactory.Get(gac.DPT);
                if (dpt is DptSimple dptSimple && dptSimple.NumericInfo?.Unit is not null)
                {
                    var unit = dptSimple.NumericInfo.Unit;
                    var unitMapping = unitSystemsMapper.GetDpstUnitMapping(gac.DPT, $"{gac.Address}, {gac.Label}");
                    kv.Dimension = unitMapping?.DimensionName ?? unit?.ToString();
                    kv.Unit = unitMapping?.UnitName;
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Generates the full content of <c>KnxValues.generated.cs</c> from the domain configuration.
    /// Write the result to <see cref="KnxSystemConfigOptions.HomeCompanion.KnxValuesCodeGenFilePath"/>.
    /// </summary>
    /// <param name="config"></param>
    /// <param name="postProcessEntries"></param>
    /// <returns></returns>
    protected string GenerateHomeCompanionCode(DomainConfiguration config, Action<Dictionary<string, KnxValueConfiguration>>? postProcessEntries = null)
    {
        var entries = GenerateIValueConfigurations(config);
        postProcessEntries?.Invoke(entries);
        return knxValuesCodeGenerator.Generate(entries);
    }

    private static void AddOpenHabItemNamesFromOhConfig(Dictionary<string, KnxValueConfiguration> entries, KnxOpenHabConfig ohc)
    {
        var gaToOhItemName = ohc.Things
            .SelectMany(t => t.GroupAddresses.Select(ga => new { ga.Address, OhItemName = ga.Item?.Name }))
            .Where(x => !string.IsNullOrEmpty(x.OhItemName))
            .ToDictionary(x => x.Address, x => x.OhItemName);

        var entryGADix = entries.ToDictionary(e => new GroupAddress(e.Key), e => e.Value);

        foreach (var entry in entryGADix)
        {
            if (gaToOhItemName.TryGetValue(entry.Key, out var ohItemName))
            {
                entry.Value.OpenHabItemName = ohItemName;
            }
        }
    }
}