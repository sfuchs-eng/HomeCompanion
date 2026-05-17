using HomeCompanion.Abstractions;
using HomeCompanion.Core;
using HomeCompanion.Server.Components;
using Microsoft.Extensions.Hosting.Systemd;
using SRF.Network.Knx;

var cso = Console.Error;

var builder = WebApplication.CreateBuilder(args);
var isSystemdService = SystemdHelpers.IsSystemdService();

if (isSystemdService)
{
    // Use systemd lifetime/console integration only when started by systemd.
    builder.Host.UseSystemd();
}

// logging
if (isSystemdService)
{
    builder.Logging.AddSystemdConsole((cfo) =>
    {
        cfo.UseUtcTimestamp = false;
        cfo.TimestampFormat = "yyyy-MM-dd HH:MM:ss ";
    });
}

// Add services to the container.
cso.WriteLine("Registering Core services...");
builder.AddHomeCompanionCore();

cso.WriteLine("Registering Server services...");
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton<HomeCompanion.Server.Services.EventBusMonitor>();

cso.WriteLine("Building application...");
var app = builder.Build();

cso.WriteLine("Signaling initialization stage PreBuild ...");
app.Services.GetRequiredService<IHomeCompanionLifeCycleSynchronization>().SignalInitializationStageCompletedAsync(AppInitializationStage.PreBuild).GetAwaiter().GetResult();
cso.WriteLine("Signaling initialization stage PreRun ...");
app.Services.GetRequiredService<IHomeCompanionLifeCycleSynchronization>().SignalInitializationStageCompletedAsync(AppInitializationStage.PreRun).GetAwaiter().GetResult();

// Configure the HTTP request pipeline.
cso.WriteLine("Configuring HTTP request pipeline...");
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

cso.WriteLine("Starting application...");
app.Run();
cso.WriteLine("Application stopped.");