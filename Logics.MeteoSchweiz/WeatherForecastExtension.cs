using HomeCompanion.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HomeCompanion.Logics.MeteoSchweiz;

public class WeatherForecastExtension : IExtensionRegistration
{
    public void RegisterServices(IExtensionRegistrationContext context)
    {
        context.Builder.Services.AddOptions<MeteoSchweizOptions>()
            .BindConfiguration(MeteoSchweizOptions.SectionName);

        context.Builder.Services.AddMeteoSwissApi((o) =>
        {
            o.SwissMetNet.CacheExpiration = context.Builder.Configuration
                .GetValue<TimeSpan>(MeteoSchweizOptions.SectionName + ":CacheExpiration", TimeSpan.FromHours(2));
            o.Language = "en";
        });
    }
}
