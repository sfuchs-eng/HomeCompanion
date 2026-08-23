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
    IEnumerable<IValuesContainer> valuesContainers,
    ILogger<ParametersProvider> logger
) : IParametersProvider
{
    public IEnumerable<IParametersContainer> ParameterContainers
    {
        get
        {
            // Combine the parameter containers from the constructor and the logics that implement IParametersContainer and dedupliicate them by object reference.
            var combinedParameterContainers = parameterContainers
                .Concat(logics.OfType<IParametersContainer>())
                .Concat(valuesContainers.Select(vc => vc is IParametersContainer pc ? pc : new ValuesContainerParametersContainerAdapter(vc)))
                .Distinct();
            logger.LogTrace("Found {Count} parameter containers from constructor and {LogicCount} logics that implement IParametersContainer, resulting in {CombinedCount} unique parameter containers.", parameterContainers.Count(), logics.OfType<IParametersContainer>().Count(), combinedParameterContainers.Count());
            return combinedParameterContainers;
        }
    }
}

internal class ValuesContainerParametersContainerAdapter : IParametersContainer
{
    private readonly IValuesContainer _valuesContainer;

    public ValuesContainerParametersContainerAdapter(IValuesContainer valuesContainer)
    {
        _valuesContainer = valuesContainer;
    }

    public string Name => _valuesContainer.GetType().Name;

    public IEnumerable<IParameter> Parameters => _valuesContainer
        .GetValues()
        // If an IValue instance also implements IParameter, we can treat it as a parameter and expose it via the IParametersContainer interface.
        // otherwise we wrap it in a ParameterAdapter that implements IParameter and delegates to the IValue instance.
        .Select(v => v is IParameter p ? p : v.ToParameter())
        .Where(p => p is not null)
        .Cast<IParameter>();
}