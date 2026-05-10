using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HomeCompanion.Extensions;

public interface IExtensionRegistrationContext
{
    /// <summary>
    /// Gets the service collection to which services can be registered.
    /// Do not use it to build a temporary service provider or similar;
    /// it is only intended for registering services, and the final service provider will be built by the host after all extensions have registered their services.
    /// </summary>
    IHostApplicationBuilder Builder { get; init; }
}