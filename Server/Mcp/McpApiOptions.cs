namespace HomeCompanion.Server.Mcp;

/// <summary>
/// Configuration options for the MCP HTTP API.
/// </summary>
public sealed class McpApiOptions
{
    /// <summary>
    /// Bearer token required for requests to the MCP API route.
    /// </summary>
    public string? BearerToken { get; set; }
}
