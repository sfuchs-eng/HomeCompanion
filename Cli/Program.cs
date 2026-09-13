using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using CL = DotMake.CommandLine;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Cli;

public class Program
{
    public static async Task Main(string[] args)
    {
        // https://devblogs.microsoft.com/dotnet/introducing-c-source-generators/
        /*
        CL.Cli.RunWithServices<Commands.Root>(args, services =>
        {
            // 1. Build configuration
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false)
                .Build();

            // 2. Register configuration with the DI container
            // This allows us to inject IOptions<AppSettings> into our services
            services.Configure<AppSettings>(configuration.GetSection("AppSettings"));

            // 3. Register our custom services
            // Here we map the interface to its concrete implementation.
            // AddTransient, AddScoped, or AddSingleton can be used depending on the desired lifetime.
            services.AddTransient<IGreetingService, GreetingService>();
        });
        */
        await CL.Cli.RunAsync<Commands.Root>(args);
    }
}