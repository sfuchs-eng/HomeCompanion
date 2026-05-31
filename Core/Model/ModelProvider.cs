using HomeCompanion.Abstractions;
using HomeCompanion.Base.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HomeCompanion.Core.Models;

internal sealed class ModelProvider(
    IConfiguration configuration,
    IModelFactory modelFactory,
    ModelValueBinder modelValueBinder,
    IHomeCompanionLifeCycleSynchronization lifeCycleSynchronization,
    IOptions<CoreOptions> coreOptions,
    ILogger<ModelProvider> logger)
    : IModelProvider, IHostedService
{
    private readonly IConfiguration _configuration = configuration;
    private readonly IModelFactory _modelFactory = modelFactory;
    private readonly ModelValueBinder _modelValueBinder = modelValueBinder;
    private readonly IHomeCompanionLifeCycleSynchronization _lifeCycleSynchronization = lifeCycleSynchronization;
    private readonly CoreOptions _coreOptions = coreOptions.Value;
    private readonly ILogger<ModelProvider> _logger = logger;

    private Model? _model;

    public bool IsInitialized => _model is not null;

    public Model GetModel()
        => _model ?? throw new InvalidOperationException("Model is not initialized yet.");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _lifeCycleSynchronization.WaitForInitializationStageCompletedAsync(
            AppInitializationStage.InitValuesRegistered,
            _coreOptions.BusInitializationTimeout,
            cancellationToken);

        var config = LoadConfig();
        _model = _modelFactory.CreateModel(config);
        _modelValueBinder.Bind(_model);

        await _lifeCycleSynchronization.SignalInitializationStageCompletedAsync(AppInitializationStage.InitModelReady);
        _logger.LogInformation(
            "Model initialized with {BuildingCount} building(s) and signaled stage {Stage}.",
            _model.Buildings.Count,
            AppInitializationStage.InitModelReady);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return Task.CompletedTask;
    }

    private CfgModel LoadConfig()
    {
        var modelSection = _configuration.GetSection(CfgModel.ConfigurationKey);
        var cfg = _modelFactory.CreateModelConfig();
        modelSection.Bind(cfg);

        cfg.Buildings = MaterializeDictionary(
            modelSection.GetSection("Buildings"),
            (kind, path) => _modelFactory.CreateBuildingConfig(kind, path),
            (buildingCfg, buildingSection) =>
            {
                buildingCfg.Facades = MaterializeDictionary(
                    buildingSection.GetSection("Facades"),
                    (kind, path) => _modelFactory.CreateFacadeConfig(kind, path));

                buildingCfg.Floors = MaterializeDictionary(
                    buildingSection.GetSection("Floors"),
                    (kind, path) => _modelFactory.CreateFloorConfig(kind, path),
                    (floorCfg, floorSection) =>
                    {
                        floorCfg.Rooms = MaterializeDictionary(
                            floorSection.GetSection("Rooms"),
                            (kind, path) => _modelFactory.CreateRoomConfig(kind, path),
                            (roomCfg, roomSection) =>
                            {
                                roomCfg.Shutters = MaterializeDictionary(
                                    roomSection.GetSection("Shutters"),
                                    (kind, path) => _modelFactory.CreateShutterConfig(kind, path));
                            });
                    });

                        buildingCfg.Specials = MaterializeDictionary(
                            buildingSection.GetSection("Specials"),
                            (kind, path) => _modelFactory.CreateSpecialConfig(kind, path));
            });

        return cfg;
    }

    private Dictionary<string, TCfgEntity> MaterializeDictionary<TCfgEntity>(
        IConfigurationSection section,
        Func<string?, string, TCfgEntity> createByKind,
        Action<TCfgEntity, IConfigurationSection>? bindChildren = null)
        where TCfgEntity : class
    {
        var values = new Dictionary<string, TCfgEntity>(StringComparer.OrdinalIgnoreCase);
        foreach (var childSection in section.GetChildren())
        {
            var kind = childSection[CfgModel.KindConfigurationKey];
            var cfg = createByKind(kind, childSection.Path);
            childSection.Bind(cfg);
            bindChildren?.Invoke(cfg, childSection);
            values[childSection.Key] = cfg;
        }

        return values;
    }
}
