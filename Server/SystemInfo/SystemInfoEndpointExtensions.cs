using HomeCompanion.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace HomeCompanion.Server.SystemInfo;

internal static class SystemInfoEndpointExtensions
{
    /// <summary>
    /// Maps system info endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapHomeCompanionSystemInfo(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/system/version", () => Results.Text(BuildVersionInfo.GetBuildVersion(), "text/plain"));

        return endpoints;
    }
}
