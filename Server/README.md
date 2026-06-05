# HomeCompanion.Server

Reusable ASP.NET Core server library for HomeCompanion.

This project contains:

- Blazor components and pages
- MCP endpoint mapping
- Server-specific service registration helpers

Host applications should call:

- `services.AddHomeCompanionServer(configuration)`
- `app.MapHomeCompanionServer()`

## Quartz integration

`HomeCompanion.Server` registers Quartz and Quartz hosted-service lifecycle when
`services.AddHomeCompanionServer(configuration)` is used.

This enables:

- `ILogic` modules to inject and use `ISchedulerFactory` / `IScheduler`
- extensions to register Quartz jobs/triggers via DI and configuration
- JSON-driven Quartz setup via standard `IConfiguration` section `Quartz`
- extension-level scheduler bootstrap via `HomeCompanion.Base.Quartz.IQuartzSchedulerConfigurator`

### Configuration sections

- `Quartz`:
	- standard Quartz properties and options (IConfiguration integration)
	- can be supplied through any loaded JSON source (for example `appsettings*.json`, `/etc/HomeCompanion.json`, user config files)
- `HomeCompanion:QuartzFileStore`:
	- HomeCompanion-specific toggle/options for SQLite file-store persistence

Example:

```json
{
	"Quartz": {
		"quartz.scheduler.instanceName": "HomeCompanionScheduler",
		"quartz.scheduler.instanceId": "AUTO"
	},
	"HomeCompanion": {
		"QuartzFileStore": {
			"EnableFileStore": true,
			"StoreFilePath": "state/quartz/quartz-store.db",
			"PerformSchemaValidation": true,
			"WaitForJobsToComplete": true
		}
	}
}
```

### Extension pattern

Extensions can use their `IExtensionRegistration` to add Quartz jobs/triggers, for example by:

- registering job types in DI,
- configuring `QuartzOptions` and/or adding jobs/triggers through Quartz DI APIs,
- relying on `HomeCompanion.Server` to host scheduler lifecycle.

For extension authors that depend on `HomeCompanion.Base`, implement
`IQuartzSchedulerConfigurator` and register it in DI. `HomeCompanion.Server`
executes all configurators on startup after Quartz is available.

Example:

```csharp
using HomeCompanion.Base.Quartz;
using Quartz;

public sealed class MyQuartzConfigurator : IQuartzSchedulerConfigurator
{
	public async ValueTask ConfigureAsync(IScheduler scheduler, CancellationToken cancellationToken = default)
	{
		var jobKey = new JobKey("example-job", "extensions");

		await scheduler.AddJob(
			JobBuilder.Create<ExampleJob>().WithIdentity(jobKey).Build(),
			replace: true,
			cancellationToken: cancellationToken);

		await scheduler.ScheduleJob(
			TriggerBuilder.Create()
				.WithIdentity("example-trigger", "extensions")
				.ForJob(jobKey)
				.WithCronSchedule("0 0/5 * * * ?")
				.Build(),
			cancellationToken);
	}
}
```

## Static asset hosting contract

`HomeCompanion.Server` is a reusable library and contains the UI static assets (including `_content/HomeCompanion.Server/*`).

To correctly serve all UI assets, including Blazor framework files like `/_framework/blazor.web.js`, the executable host must also register and map Razor components itself:

- `services.AddRazorComponents().AddInteractiveServerComponents()`
- `app.MapStaticAssets()`
- `app.MapRazorComponents<App>().AddInteractiveServerRenderMode()`

`app.MapHomeCompanionServer()` configures middleware and MCP endpoints, but it intentionally does not map Razor component endpoints or static web assets.
