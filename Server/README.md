# HomeCompanion.Server

Reusable ASP.NET Core server library for HomeCompanion.

This project contains:

- Blazor components and pages
- MCP endpoint mapping
- Server-specific service registration helpers

Host applications should call:

- `services.AddHomeCompanionServer(configuration)`
- `app.MapHomeCompanionServer()`

## Static asset hosting contract

`HomeCompanion.Server` is a reusable library and contains the UI static assets (including `_content/HomeCompanion.Server/*`).

To correctly serve all UI assets, including Blazor framework files like `/_framework/blazor.web.js`, the executable host must also register and map Razor components itself:

- `services.AddRazorComponents().AddInteractiveServerComponents()`
- `app.MapStaticAssets()`
- `app.MapRazorComponents<App>().AddInteractiveServerRenderMode()`

`app.MapHomeCompanionServer()` configures middleware and MCP endpoints, but it intentionally does not map Razor component endpoints or static web assets.
