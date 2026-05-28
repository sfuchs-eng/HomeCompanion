using HomeCompanion.Core.Mcp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HomeCompanion.Server.Mcp;

public static class McpEndpointExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    /// <summary>
    /// Maps HomeCompanion MCP JSON-RPC endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapHomeCompanionMcp(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/mcp");
        group.DisableAntiforgery();

        group.MapGet("", () => TypedResults.Ok(new
        {
            name = "HomeCompanion MCP",
            transport = "json-rpc",
            endpoint = "/api/mcp",
        }));

        group.MapPost("", HandleJsonRpcAsync);

        return endpoints;
    }

    private static async Task<Results<Ok<McpJsonRpcResponse>, UnauthorizedHttpResult, StatusCodeHttpResult>> HandleJsonRpcAsync(
        HttpContext context,
        IMcpIntrospectionService introspection,
        IOptions<McpApiOptions> options,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorized(context, options.Value))
            return TypedResults.Unauthorized();

        McpJsonRpcRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<McpJsonRpcRequest>(JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return TypedResults.Ok(new McpJsonRpcResponse
            {
                Error = new McpJsonRpcError { Code = -32700, Message = "Parse error" },
            });
        }

        if (request is null)
        {
            return TypedResults.Ok(new McpJsonRpcResponse
            {
                Error = new McpJsonRpcError { Code = -32600, Message = "Invalid Request" },
            });
        }

        var response = DispatchRequest(request, introspection);
        return TypedResults.Ok(response);
    }

    private static bool IsAuthorized(HttpContext context, McpApiOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BearerToken))
            return false;

        if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader))
            return false;

        const string prefix = "Bearer ";
        var headerValue = authHeader.ToString();
        if (!headerValue.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var incomingToken = headerValue[prefix.Length..].Trim();
        return FixedTimeEquals(incomingToken, options.BearerToken);
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static McpJsonRpcResponse DispatchRequest(McpJsonRpcRequest request, IMcpIntrospectionService introspection)
    {
        if (!string.Equals(request.Jsonrpc, "2.0", StringComparison.Ordinal))
        {
            return Error(request.Id, -32600, "Invalid Request");
        }

        if (string.IsNullOrWhiteSpace(request.Method))
        {
            return Error(request.Id, -32600, "Invalid Request");
        }

        return request.Method switch
        {
            "initialize" => Success(request.Id, new
            {
                protocolVersion = "2024-11-05",
                capabilities = new
                {
                    tools = new { },
                },
                serverInfo = new
                {
                    name = "HomeCompanion",
                    version = "0.1.0",
                },
            }),
            "notifications/initialized" => Success(request.Id, new { }),
            "tools/list" => Success(request.Id, new { tools = GetTools() }),
            "tools/call" => HandleToolsCall(request.Id, request.Params, introspection),
            _ => Error(request.Id, -32601, "Method not found"),
        };
    }

    private static McpJsonRpcResponse HandleToolsCall(JsonElement? id, JsonElement? @params, IMcpIntrospectionService introspection)
    {
        if (@params is null
            || !@params.Value.TryGetProperty("name", out var nameElement)
            || nameElement.ValueKind != JsonValueKind.String)
        {
            return Error(id, -32602, "Invalid params");
        }

        var toolName = nameElement.GetString() ?? string.Empty;

        JsonElement argumentsElement = default;
        var hasArguments = @params.Value.TryGetProperty("arguments", out argumentsElement) && argumentsElement.ValueKind == JsonValueKind.Object;

        return toolName switch
        {
            "list_values_containers" => ToolSuccess(id, introspection.ListValuesContainers()),
            "list_logic_instances" => ToolSuccess(id, introspection.ListLogicInstances()),
            "list_container_value_properties" => HandleListContainerValueProperties(id, introspection, hasArguments ? argumentsElement : default),
            "get_value_info" => HandleGetValueInfo(id, introspection, hasArguments ? argumentsElement : default),
            _ => Error(id, -32601, $"Unknown tool: {toolName}"),
        };
    }

    private static McpJsonRpcResponse HandleListContainerValueProperties(JsonElement? id, IMcpIntrospectionService introspection, JsonElement arguments)
    {
        var containerType = ReadStringArgument(arguments, "containerType");
        if (string.IsNullOrWhiteSpace(containerType))
            return Error(id, -32602, "Missing argument: containerType");

        return ToolSuccess(id, introspection.ListContainerValueProperties(containerType));
    }

    private static McpJsonRpcResponse HandleGetValueInfo(JsonElement? id, IMcpIntrospectionService introspection, JsonElement arguments)
    {
        var containerType = ReadStringArgument(arguments, "containerType");
        var propertyName = ReadStringArgument(arguments, "propertyName");
        if (string.IsNullOrWhiteSpace(containerType) || string.IsNullOrWhiteSpace(propertyName))
            return Error(id, -32602, "Missing arguments: containerType and propertyName are required");

        var info = introspection.GetValueInfo(containerType, propertyName);
        if (info is null)
            return Error(id, -32602, "Value not found");

        return ToolSuccess(id, info);
    }

    private static string? ReadStringArgument(JsonElement arguments, string key)
    {
        return arguments.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static McpJsonRpcResponse ToolSuccess(JsonElement? id, object payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        return Success(id, new
        {
            content = new[]
            {
                new
                {
                    type = "text",
                    text = json,
                },
            },
            structuredContent = payload,
        });
    }

    private static McpJsonRpcResponse Success(JsonElement? id, object result)
    {
        return new McpJsonRpcResponse
        {
            Id = id,
            Result = result,
        };
    }

    private static McpJsonRpcResponse Error(JsonElement? id, int code, string message)
    {
        return new McpJsonRpcResponse
        {
            Id = id,
            Error = new McpJsonRpcError
            {
                Code = code,
                Message = message,
            },
        };
    }

    private static object[] GetTools()
    {
        return
        [
            new
            {
                name = "list_values_containers",
                description = "List all registered IValuesContainer instances.",
                inputSchema = new
                {
                    type = "object",
                    properties = new { },
                    additionalProperties = false,
                },
            },
            new
            {
                name = "list_container_value_properties",
                description = "List public IValue properties declared by a container type.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        containerType = new { type = "string", description = "Container CLR type full name." },
                    },
                    required = new[] { "containerType" },
                    additionalProperties = false,
                },
            },
            new
            {
                name = "get_value_info",
                description = "Get details for one IValue property in a container.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        containerType = new { type = "string", description = "Container CLR type full name." },
                        propertyName = new { type = "string", description = "Public property name implementing IValue." },
                    },
                    required = new[] { "containerType", "propertyName" },
                    additionalProperties = false,
                },
            },
            new
            {
                name = "list_logic_instances",
                description = "List all discovered ILogic instances.",
                inputSchema = new
                {
                    type = "object",
                    properties = new { },
                    additionalProperties = false,
                },
            },
        ];
    }
}
