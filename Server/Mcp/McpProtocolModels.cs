using System.Text.Json;

namespace HomeCompanion.Server.Mcp;

internal sealed class McpJsonRpcRequest
{
    public string? Jsonrpc { get; set; }
    public JsonElement? Id { get; set; }
    public string? Method { get; set; }
    public JsonElement? Params { get; set; }
}

internal sealed class McpJsonRpcError
{
    public int Code { get; set; }
    public string Message { get; set; } = string.Empty;
    public object? Data { get; set; }
}

internal sealed class McpJsonRpcResponse
{
    public string Jsonrpc { get; set; } = "2.0";
    public JsonElement? Id { get; set; }
    public object? Result { get; set; }
    public McpJsonRpcError? Error { get; set; }
}
