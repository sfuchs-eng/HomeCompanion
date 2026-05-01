using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HomeCompanion.Extensions;

public interface IExtensionRegistrationContext
{
    /// <summary>
    /// Gets the service collection to which services can be registered.
    /// </summary>
    IHostApplicationBuilder Builder { get; init; }
}