using System;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Extensions;

/// <summary>
/// Supports discovery and service registration of extensions. An extension is a loosely defined concept that can be used for various purposes.
/// For example, Integrations are extensions that add support for specific device ecosystems, but also logics could be implemented as extensions in case they are complex enough to need it.
/// Note that <see cref="ILogic"/> and <see cref="IConnectivityProvider"/> only need to be complemented by an <see cref="IExtensionRegistration"/>
/// if they require additional services to be registered in the DI container;
/// otherwise, they can be registered directly as implementations of their respective interfaces and are discovered and registered automatically.
/// </summary>
public interface IExtensionRegistration
{
    /// <summary>
    /// Registers services for this extension in the DI container.
    /// </summary>
    /// <param name="context">Context object providing access to the DI service collection and other relevant information.</param>
    void RegisterServices(IExtensionRegistrationContext context);
}