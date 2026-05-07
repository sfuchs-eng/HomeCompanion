using System;
using HomeCompanion.Abstractions;
using HomeCompanion.Extensions;
using HomeCompanion.Persistence;
using HomeCompanion.Values;
using Microsoft.Extensions.Logging;
using SRF.Network.OpenHab;

namespace HomeCompanion.Integrations.OpenHab;

public class OpenHabExtensionRegistration(
    IHomeCompanionLifeCycleSynchronization lifeCycleSynchronization,
    IStateInitializationManager stateInitializationManager,
    IEnumerable<IValuesContainer> valueContainers,
    ILogger<OpenHabExtensionRegistration> logger
) : IExtensionRegistration
{
    private readonly IHomeCompanionLifeCycleSynchronization lifeCycleSynchronization = lifeCycleSynchronization;
    private readonly IStateInitializationManager stateInitializationManager = stateInitializationManager;
    private readonly IEnumerable<IValuesContainer> valueContainers = valueContainers;
    private readonly ILogger<OpenHabExtensionRegistration> logger = logger;

    public void RegisterServices(IExtensionRegistrationContext context)
    {
        context.Builder.Services.AddOpenHabConnector();
        stateInitializationManager.RegisterInitialization(StateInitializationStage.InitRetrieveFromEnvironment, InitializeValuesFromOpenHabAsync);
        logger.LogInformation("Registered OpenHAB connectivity extension");
    }

    /// <summary>
    /// Retrieve all current item values from OpenHAB and see which ones can be mapped to registered values in the application.
    /// Mapping is done via <see cref="IValue.BusMappings"/> or if the property name matches the OpenHAB item name.
    /// </summary>
    /// <param name="token"></param>
    /// <returns></returns>
    private async Task InitializeValuesFromOpenHabAsync(CancellationToken token)
    {
        throw new NotImplementedException();
    }
}
