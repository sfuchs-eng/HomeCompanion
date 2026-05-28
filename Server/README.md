# HomeCompanion.Server

Reusable ASP.NET Core server library for HomeCompanion.

This project contains:

- Blazor components and pages
- MCP endpoint mapping
- Server-specific service registration helpers

Host applications should call:

- `services.AddHomeCompanionServer(configuration)`
- `app.MapHomeCompanionServer()`
