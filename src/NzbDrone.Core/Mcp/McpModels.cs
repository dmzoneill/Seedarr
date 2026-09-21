using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NzbDrone.Core.Mcp;

public static class McpJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}

public class JsonRpcRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public object Id { get; set; }

    [JsonPropertyName("method")]
    public string Method { get; set; }

    [JsonPropertyName("params")]
    public JsonElement? Params { get; set; }
}

public class JsonRpcResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public object Id { get; set; }

    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object Result { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonRpcError Error { get; set; }

    public static JsonRpcResponse Success(object id, object result) => new()
    {
        Id = id,
        Result = result
    };

    public static JsonRpcResponse CreateError(object id, int code, string message, object data = null) => new()
    {
        Id = id,
        Error = new JsonRpcError { Code = code, Message = message, Data = data }
    };
}

public class JsonRpcError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; }

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object Data { get; set; }
}

public class McpInitializeResult
{
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; set; } = "2024-11-05";

    [JsonPropertyName("capabilities")]
    public McpServerCapabilities Capabilities { get; set; } = new();

    [JsonPropertyName("serverInfo")]
    public McpImplementation ServerInfo { get; set; } = new()
    {
        Name = "Seedarr MCP Server",
        Version = "1.0.0"
    };
}

public class McpServerCapabilities
{
    [JsonPropertyName("tools")]
    public McpToolsCapability Tools { get; set; } = new();

    [JsonPropertyName("resources")]
    public McpResourcesCapability Resources { get; set; } = new();

    [JsonPropertyName("prompts")]
    public McpPromptsCapability Prompts { get; set; } = new();
}

public class McpToolsCapability
{
    [JsonPropertyName("listChanged")]
    public bool ListChanged { get; set; }
}

public class McpResourcesCapability
{
    [JsonPropertyName("subscribe")]
    public bool Subscribe { get; set; }

    [JsonPropertyName("listChanged")]
    public bool ListChanged { get; set; }
}

public class McpPromptsCapability
{
    [JsonPropertyName("listChanged")]
    public bool ListChanged { get; set; }
}

public class McpImplementation
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; }
}

public class McpTool
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; }

    [JsonPropertyName("inputSchema")]
    public object InputSchema { get; set; }
}

public class McpToolListResult
{
    [JsonPropertyName("tools")]
    public List<McpTool> Tools { get; set; } = new();
}

public class McpToolCallResult
{
    [JsonPropertyName("content")]
    public List<McpContent> Content { get; set; } = new();

    [JsonPropertyName("isError")]
    public bool IsError { get; set; }

    public static McpToolCallResult Text(string text, bool isError = false) => new()
    {
        IsError = isError,
        Content = new List<McpContent> { new() { Type = "text", Text = text } }
    };
}

public class McpContent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    public string Text { get; set; }
}

public class McpResource
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; }

    [JsonPropertyName("mimeType")]
    public string MimeType { get; set; } = "application/json";
}

public class McpResourceListResult
{
    [JsonPropertyName("resources")]
    public List<McpResource> Resources { get; set; } = new();
}

public class McpResourceReadResult
{
    [JsonPropertyName("contents")]
    public List<McpResourceContent> Contents { get; set; } = new();
}

public class McpResourceContent
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; }

    [JsonPropertyName("mimeType")]
    public string MimeType { get; set; } = "application/json";

    [JsonPropertyName("text")]
    public string Text { get; set; }
}

public class McpPrompt
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; }

    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<McpPromptArgument> Arguments { get; set; }
}

public class McpPromptArgument
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; }

    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

public class McpPromptListResult
{
    [JsonPropertyName("prompts")]
    public List<McpPrompt> Prompts { get; set; } = new();
}

public class McpPromptGetResult
{
    [JsonPropertyName("description")]
    public string Description { get; set; }

    [JsonPropertyName("messages")]
    public List<McpPromptMessage> Messages { get; set; } = new();
}

public class McpPromptMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "user";

    [JsonPropertyName("content")]
    public McpContent Content { get; set; }
}
