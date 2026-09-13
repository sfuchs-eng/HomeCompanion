using System.Text;
using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HomeCompanion.Cli;

public class HostLauncher<TCommand>() where TCommand : BackgroundService
{
    [CliOption(Alias = "j", Required = false, Arity = CliArgumentArity.ExactlyOne, Description = "Write JSON output to filename instead of console.")]
    public string? JsonOutputFileName { get; set; }

    public TextWriter Output { get; set; } = Console.Out;

    public JsonSerializerOptions JsonOptions { get; set; } = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
        IncludeFields = false
    };

    public void JsonOutput(object? output)
    {
        if (!string.IsNullOrEmpty(JsonOutputFileName))
        {
            using var fs = new FileStream(JsonOutputFileName, FileMode.Create);
            var ow = new StreamWriter(fs, Encoding.UTF8);
            ow.WriteLine(JsonSerializer.Serialize(output, JsonOptions));
            ow.Close();
        }
        else
            Output.WriteLine(JsonSerializer.Serialize(output, JsonOptions));
    }

    protected virtual void AddConfiguration(IConfigurationBuilder configurationBuilder, CliContext cliContext)
    {
        var userConfigPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var userConfigFiles = new[] { "HomeCompanion.json", "HomeCompanion.Cli.json" };

        configurationBuilder.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

        foreach (var userConfigFile in userConfigFiles)
        {
            if (File.Exists(Path.Combine(userConfigPath, userConfigFile)))
            {
                Console.WriteLine($"Adding config from '{userConfigPath}/{userConfigFile}'");
                configurationBuilder.AddJsonFile(new PhysicalFileProvider(userConfigPath), userConfigFile, optional: true, reloadOnChange: true);
            }
        }

        configurationBuilder.AddCommandLine([.. cliContext.Result.ParseResult.UnmatchedTokens]);
    }

    protected virtual void AddLogging(ILoggingBuilder loggingBuilder, CliContext cliContext)
    {
        loggingBuilder.AddConsole();
    }

    protected virtual void AddServices(IServiceCollection services, CliContext cliContext)
    {
        services.AddSingleton(this.GetType(), this);
        services.AddSingleton<CliContext>(cliContext);
        services.AddHostedService<TCommand>();
    }

    public virtual async Task<int> RunAsync(CliContext cliContext)
    {
        var hostBuilder = Host.CreateApplicationBuilder([.. cliContext.Result.ParseResult.UnmatchedTokens]);

        AddConfiguration(hostBuilder.Configuration, cliContext);
        AddLogging(hostBuilder.Logging, cliContext);
        AddServices(hostBuilder.Services, cliContext);

        /*
        foreach (var cs in hostBuilder.Configuration.AsEnumerable().Select(c => $"{c.Key} = '{c.Value}'"))
            Console.WriteLine($"- {cs}");
            */
            
        using var host = hostBuilder.Build();

        await host.RunAsync();

        return 0;
    }
}
