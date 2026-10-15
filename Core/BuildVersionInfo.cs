using System.Reflection;
using HomeCompanion.Values;

namespace HomeCompanion.Core;

public static class BuildVersionInfo
{
    public static string GetBuildVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(ValueBase).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrEmpty(informationalVersion))
        {
            return $"{assembly.GetName().Name}, {informationalVersion}";
        }

        return "dev";
    }
}
