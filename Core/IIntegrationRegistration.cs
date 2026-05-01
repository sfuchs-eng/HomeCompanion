using Microsoft.Extensions.DependencyInjection;

namespace HomeCompanion.Core;

public interface IIntegrationRegistration
{
    public static IIntegrationRegistration Register(Type integrationType, IServiceCollection services)
    {
        if (integrationType == null)
            throw new ArgumentNullException(nameof(integrationType));
        if (!typeof(IIntegrationRegistration).IsAssignableFrom(integrationType))
            throw new ArgumentException($"Type {integrationType.FullName} does not implement IIntegrationRegistration", nameof(integrationType));

        var registration = (IIntegrationRegistration)Activator.CreateInstance(integrationType)!;
        registration.Register(services);
        return registration;
    }

    void Register(IServiceCollection services);
}
