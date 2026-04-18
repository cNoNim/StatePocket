using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using StatePocket.Tools;

namespace StatePocket.Hosting;

internal static class StatePocketMcpToolFactory
{
    public static McpServerTool CreateSetValue(IServiceProvider services)
    {
        return Create(SetValueAsync, services, static schema => OverrideAnyJsonProperty(schema, "value"));
    }

    public static McpServerTool CreateQueryValues(IServiceProvider services)
    {
        return Create(QueryValuesAsync, services, static schema => OverrideAnyJsonProperty(schema, "equals"));
    }

    public static McpServerTool CreatePatchValue(IServiceProvider services)
    {
        return Create(PatchValueAsync, services, static schema => OverrideArrayProperty(schema, "patch"));
    }

    private static McpServerTool Create(
        Delegate method,
        IServiceProvider services,
        Func<JsonElement, JsonElement>? inputSchemaOverride = null
    )
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(services);
        var tool = McpServerTool.Create(
            method,
            new McpServerToolCreateOptions
            {
                Services = services
            }
        );
        return inputSchemaOverride is null
          ? tool
          : new InputSchemaOverrideMcpServerTool(tool, inputSchemaOverride(tool.ProtocolTool.InputSchema));
    }

    private static JsonElement OverrideAnyJsonProperty(JsonElement inputSchema, string propertyName)
    {
        return OverridePropertySchema(
            inputSchema,
            propertyName,
            static existing =>
            {
                var schema = CloneObject(existing);
                schema["type"] = new JsonArray(
                    JsonValue.Create("object"),
                    JsonValue.Create("array"),
                    JsonValue.Create("string"),
                    JsonValue.Create("number"),
                    JsonValue.Create("boolean"),
                    JsonValue.Create("null")
                );
                return schema;
            }
        );
    }

    private static JsonElement OverrideArrayProperty(JsonElement inputSchema, string propertyName)
    {
        return OverridePropertySchema(
            inputSchema,
            propertyName,
            static existing =>
            {
                var schema = CloneObject(existing);
                schema["type"] = JsonValue.Create("array");
                return schema;
            }
        );
    }

    private static JsonElement OverridePropertySchema(
        JsonElement inputSchema,
        string propertyName,
        Func<JsonObject?, JsonObject> overrideProperty
    )
    {
        var schema = JsonNode.Parse(inputSchema.GetRawText())
                            ?.AsObject()
                  ?? throw new InvalidOperationException("Tool input schema must be a JSON object.");
        var properties = schema["properties"]
                           ?.AsObject()
                      ?? throw new InvalidOperationException("Tool input schema must define properties.");
        properties[propertyName] = overrideProperty(properties[propertyName] as JsonObject);
        using var document = JsonDocument.Parse(schema.ToJsonString());
        return document.RootElement.Clone();
    }

    private static JsonObject CloneObject(JsonObject? schema)
    {
        return schema is null
          ? []
          : schema.DeepClone()
                  .AsObject();
    }

    [McpServerTool(Name = SetValueTool.ToolName)]
    [Description(
        "Stores a JSON value under a key in the selected namespace, creating the key or replacing its current value."
    )]
    private static Task<CallToolResult> SetValueAsync(
        SetValueTool tool,
        [Description("Key to create or replace.")] string key,
        [Description("JSON value to store.")] JsonElement value,
        [Description("Namespace to use. Defaults to 'default'.")] string? @namespace = null,
        [Description("Optional TTL in seconds. Omit to store the value without expiration.")] long? ttlSeconds = null,
        CancellationToken cancellationToken = default
    )
    {
        return tool.SetValueAsync(
            key,
            value,
            @namespace,
            ttlSeconds,
            cancellationToken
        );
    }

    [McpServerTool(Name = QueryValuesTool.ToolName, ReadOnly = true)]
    [Description(
        "Finds values in the selected namespace by key pattern and optional JSONPath filter, with optional equality and JSON Pointer projection. Pagination uses an opaque scan cursor over keys in ascending order."
    )]
    private static Task<CallToolResult> QueryValuesAsync(
        QueryValuesTool tool,
        [Description("Namespace to use. Defaults to 'default'.")] string? @namespace = null,
        [Description("Optional wildcard key pattern, for example 'user:*'.")] string? pattern = null,
        [Description(
            "Optional query used to filter stored JSON values. Use JSONPath syntax, for example '$.status' or '$.profile.name'. Omit to match by key pattern only."
        )]
        string? query = null,
        [Description(
            "Optional JSON value that at least one query match must equal. Requires query to be set. Pass explicit null to match JSON nulls."
        )]
        JsonElement? equals = null,
        [Description(
            "Optional path to project part of each matched JSON value. Use JSON Pointer syntax starting with '/', for example '/profile/name' or '/items/0'. Omit to return whole values."
        )]
        string? path = null,
        [Description(
            "Maximum number of matching values to return. Defaults to 50 and must be less than or equal to 100."
        )]
        int? limit = null,
        [Description(
            "Optional opaque cursor for pagination. Pass the `next_cursor` value from a previous response to continue scanning after the last emitted match. Because filtering is applied while scanning, a follow-up request may return no matches even when `next_cursor` was present."
        )]
        string? cursor = null,
        RequestContext<CallToolRequestParams>? requestContext = null,
        CancellationToken cancellationToken = default
    )
    {
        return tool.QueryValuesAsync(
            @namespace,
            pattern,
            query,
            equals,
            path,
            limit,
            cursor,
            requestContext,
            cancellationToken
        );
    }

    [McpServerTool(Name = PatchValueTool.ToolName)]
    [Description("Applies an RFC 6902 JSON Patch document to an existing value in the selected namespace.")]
    private static Task<CallToolResult> PatchValueAsync(
        PatchValueTool tool,
        [Description("Key to patch.")] string key,
        [Description("JSON Patch document to apply.")] JsonElement patch,
        [Description("Namespace to use. Defaults to 'default'.")] string? @namespace = null,
        CancellationToken cancellationToken = default
    )
    {
        return tool.PatchValueAsync(
            key,
            patch,
            @namespace,
            cancellationToken
        );
    }
}
