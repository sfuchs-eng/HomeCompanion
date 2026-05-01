using HomeCompanion.Extensions;
using Microsoft.Extensions.Hosting;

namespace HomeCompanion.Core;

internal class ExtensionRegistrationContext(IHostApplicationBuilder builder) : IExtensionRegistrationContext
{
    private IHostApplicationBuilder builder = builder;

    public IHostApplicationBuilder Builder { get => builder; init => builder = value; }
}