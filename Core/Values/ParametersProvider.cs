using HomeCompanion.Logics;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Values;

/// <summary>
/// A provider of <see cref="IParametersContainer"/>s, allowing the Web UI and other user interface components to access and configure parameters exposed by logics.¨
/// It searches for <see cref="IParametersContainer"/>s that are registered with the dependency injection container and returns them as a collection of <see cref="IParametersContainer"/>s.
/// This catches also any <see cref="ILogic"/>s that implement <see cref="IParametersContainer"/> and returns them as a collection of <see cref="IParametersContainer"/>s.
/// </summary>
public class ParametersProvider(
    IEnumerable<IParametersContainer> parameterContainers,
    IEnumerable<ILogic> logics,
    ILogger<ParametersProvider> logger
) : IParametersProvider
{
    public IEnumerable<IParametersContainer> ParameterContainers
    {
        get
        {
            // Combine the parameter containers from the constructor and the logics that implement IParametersContainer and dedupliicate them by object reference.
            var combinedParameterContainers = parameterContainers.Concat(logics.OfType<IParametersContainer>()).Distinct();
            logger.LogTrace("Found {Count} parameter containers from constructor and {LogicCount} logics that implement IParametersContainer, resulting in {CombinedCount} unique parameter containers.", parameterContainers.Count(), logics.OfType<IParametersContainer>().Count(), combinedParameterContainers.Count());
            return combinedParameterContainers;
        }
    }
}