using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using StatePocket.Hosting;
using StatePocket.Json.Patch;
using StatePocket.Json.Pointer;
using StatePocket.Storage;
using StatePocket.Tools;

namespace StatePocket.Tests.Hosting;

public sealed class StatePocketMcpToolFactoryTests
{
    private readonly IServiceProvider _services = new ServiceCollection().AddSingleton<IKvStore, InMemoryKvStore>()
                                                                         .AddSingleton<GetValueTool>()
                                                                         .AddSingleton<GetValuesTool>()
                                                                         .AddSingleton<SetValueTool>()
                                                                         .AddSingleton<QueryValuesTool>()
                                                                         .AddSingleton<PatchValueTool>()
                                                                         .BuildServiceProvider();

    [Fact]
    public void SetValue_OverridesValueSchemaToExplicitAnyJson()
    {
        var tool = CreateTool(SetValueTool.ToolName);
        var propertySchema = GetPropertySchema(tool, "value");
        Assert.Equal(
            "JSON value to store.",
            propertySchema.GetProperty("description")
                          .GetString()
        );
        Assert.Equal(
            [
                "object",
                "array",
                "string",
                "number",
                "boolean",
                "null"
            ],
            [
                .. propertySchema.GetProperty("type")
                                 .EnumerateArray()
                                 .Select(static value => value.GetString()!)
            ]
        );
    }

    [Fact]
    public void SetValue_ExposesTitleAndClosedWorldHint()
    {
        var tool = CreateTool(SetValueTool.ToolName);
        Assert.Equal("Set Value", tool.ProtocolTool.Title);
        var annotations = Assert.IsType<ToolAnnotations>(tool.ProtocolTool.Annotations);
        Assert.Equal("Set Value", annotations.Title);
        Assert.False(annotations.OpenWorldHint);
        Assert.Null(annotations.ReadOnlyHint);
    }

    [Fact]
    public void SetValue_ExposesCamelCaseTtlParameter()
    {
        var tool = CreateTool(SetValueTool.ToolName);
        var properties = tool.ProtocolTool.InputSchema.GetProperty("properties");
        var ttlPropertySchema = properties.GetProperty("ttlSeconds");
        Assert.Equal(
            "Optional TTL in seconds. Omit to store the value without expiration.",
            ttlPropertySchema.GetProperty("description")
                             .GetString()
        );
        Assert.False(properties.TryGetProperty("ttl_seconds", out _));
    }

    [Fact]
    public void SetValue_ExposesConditionalWriteParameters()
    {
        var tool = CreateTool(SetValueTool.ToolName);
        var properties = tool.ProtocolTool.InputSchema.GetProperty("properties");
        var expectedRevisionPropertySchema = properties.GetProperty("expectedRevision");
        var ifAbsentPropertySchema = properties.GetProperty("ifAbsent");
        Assert.Equal(
            "Optional expected revision for compare-and-set writes. When provided, the write succeeds only if the current live value has this exact revision.",
            expectedRevisionPropertySchema.GetProperty("description")
                                          .GetString()
        );
        Assert.Equal(
            ["integer", "null"],
            [
                .. expectedRevisionPropertySchema.GetProperty("type")
                                                 .EnumerateArray()
                                                 .Select(static value => value.GetString()!)
            ]
        );
        Assert.Equal(
            "When true, create the key only if no live value currently exists.",
            ifAbsentPropertySchema.GetProperty("description")
                                  .GetString()
        );
        Assert.Equal(
            "boolean",
            ifAbsentPropertySchema.GetProperty("type")
                                  .GetString()
        );
        Assert.False(
            ifAbsentPropertySchema.GetProperty("default")
                                  .GetBoolean()
        );
    }

    [Fact]
    public void SetValue_ExposesTypedOutputSchema()
    {
        var tool = CreateTool(SetValueTool.ToolName);
        var outputSchema = tool.ProtocolTool.OutputSchema
                        ?? throw new InvalidOperationException("Expected output schema.");
        var properties = outputSchema.GetProperty("properties");
        Assert.Equal(
            "string",
            properties.GetProperty("namespace")
                      .GetProperty("type")
                      .GetString()
        );
        Assert.Equal(
            "string",
            properties.GetProperty("key")
                      .GetProperty("type")
                      .GetString()
        );
        Assert.Equal(
            ["string", "null"],
            [
                .. properties.GetProperty("expiresAt")
                             .GetProperty("type")
                             .EnumerateArray()
                             .Select(static value => value.GetString()!)
            ]
        );
        Assert.Equal(
            "string",
            properties.GetProperty("updatedAt")
                      .GetProperty("type")
                      .GetString()
        );
        Assert.Equal(
            "integer",
            properties.GetProperty("revision")
                      .GetProperty("type")
                      .GetString()
        );
        Assert.False(outputSchema.TryGetProperty("allOf", out _));
    }

    [Fact]
    public void SetValue_SchemaRejectsExpectedRevisionWhenIfAbsentIsTrue()
    {
        var tool = CreateTool(SetValueTool.ToolName);
        var constraintSchema = tool.ProtocolTool.InputSchema.GetProperty("allOf")[0]
                                   .GetProperty("not");
        Assert.Equal(
            ["expectedRevision", "ifAbsent"],
            [
                .. constraintSchema.GetProperty("required")
                                   .EnumerateArray()
                                   .Select(static value => value.GetString()!)
            ]
        );
        Assert.Equal(
            "null",
            constraintSchema.GetProperty("properties")
                            .GetProperty("expectedRevision")
                            .GetProperty("not")
                            .GetProperty("type")
                            .GetString()
        );
        Assert.True(
            constraintSchema.GetProperty("properties")
                            .GetProperty("ifAbsent")
                            .TryGetProperty("const", out var constValue)
        );
        Assert.True(constValue.GetBoolean());
    }

    [Fact]
    public void PatchValue_ExposesTypedPatchSchema()
    {
        var tool = CreateTool(PatchValueTool.ToolName);
        var propertySchema = GetPropertySchema(tool, "patch");
        Assert.Equal(
            "JSON Patch document to apply.",
            propertySchema.GetProperty("description")
                          .GetString()
        );
        Assert.Equal(
            "array",
            propertySchema.GetProperty("type")
                          .GetString()
        );
        var itemSchemas = propertySchema.GetProperty("items")
                                        .GetProperty("oneOf")
                                        .EnumerateArray()
                                        .ToArray();
        Assert.Equal(6, itemSchemas.Length);
        var replaceSchema = Assert.Single(
            itemSchemas,
            static schema => schema.GetProperty("properties")
                                   .GetProperty("op")
                                   .GetProperty("const")
                                   .GetString()
                          == "replace"
        );
        var moveSchema = Assert.Single(
            itemSchemas,
            static schema => schema.GetProperty("properties")
                                   .GetProperty("op")
                                   .GetProperty("const")
                                   .GetString()
                          == "move"
        );
        var replaceProperties = replaceSchema.GetProperty("properties");
        var moveProperties = moveSchema.GetProperty("properties");
        Assert.Equal(
            "string",
            replaceProperties.GetProperty("op")
                             .GetProperty("type")
                             .GetString()
        );
        Assert.Equal(
            [
                "object",
                "array",
                "string",
                "number",
                "boolean",
                "null"
            ],
            [
                .. replaceProperties.GetProperty("value")
                                    .GetProperty("type")
                                    .EnumerateArray()
                                    .Select(static value => value.GetString()!)
            ]
        );
        Assert.Equal(
            "string",
            moveProperties.GetProperty("from")
                          .GetProperty("type")
                          .GetString()
        );
        Assert.False(replaceSchema.TryGetProperty("additionalProperties", out _));
        Assert.False(moveSchema.TryGetProperty("additionalProperties", out _));
    }

    [Fact]
    public void QueryValues_OverridesEqualsSchemaToExplicitAnyJsonAndPreservesDefault()
    {
        var tool = CreateTool(QueryValuesTool.ToolName);
        var propertySchema = GetPropertySchema(tool, "equals");
        Assert.Equal(
            "Optional JSON value that at least one query match must equal. Requires query to be set. Pass explicit null to match JSON nulls.",
            propertySchema.GetProperty("description")
                          .GetString()
        );
        Assert.Equal(
            JsonValueKind.Null,
            propertySchema.GetProperty("default")
                          .ValueKind
        );
        Assert.Equal(
            [
                "object",
                "array",
                "string",
                "number",
                "boolean",
                "null"
            ],
            [
                .. propertySchema.GetProperty("type")
                                 .EnumerateArray()
                                 .Select(static value => value.GetString()!)
            ]
        );
    }

    [Fact]
    public void QueryValues_SchemaRequiresQueryWhenEqualsIsPresent()
    {
        var tool = CreateTool(QueryValuesTool.ToolName);
        var constraintSchema = tool.ProtocolTool.InputSchema.GetProperty("allOf")[0];
        Assert.Equal(
            ["equals"],
            [
                .. constraintSchema.GetProperty("if")
                                   .GetProperty("required")
                                   .EnumerateArray()
                                   .Select(static value => value.GetString()!)
            ]
        );
        Assert.Equal(
            ["query"],
            [
                .. constraintSchema.GetProperty("then")
                                   .GetProperty("required")
                                   .EnumerateArray()
                                   .Select(static value => value.GetString()!)
            ]
        );
        Assert.Equal(
            "null",
            constraintSchema.GetProperty("then")
                            .GetProperty("properties")
                            .GetProperty("query")
                            .GetProperty("not")
                            .GetProperty("type")
                            .GetString()
        );
    }

    [Fact]
    public void GetValue_ExposesTitleAndClosedWorldReadOnlyHints()
    {
        var tool = CreateTool(GetValueTool.ToolName);
        Assert.Equal("Get Value", tool.ProtocolTool.Title);
        var annotations = Assert.IsType<ToolAnnotations>(tool.ProtocolTool.Annotations);
        Assert.Equal("Get Value", annotations.Title);
        Assert.False(annotations.OpenWorldHint);
        Assert.True(annotations.ReadOnlyHint);
    }

    [Fact]
    public void GetValue_ExposesTypedOutputSchema()
    {
        var tool = CreateTool(GetValueTool.ToolName);
        var outputSchema = tool.ProtocolTool.OutputSchema
                        ?? throw new InvalidOperationException("Expected output schema.");
        var properties = outputSchema.GetProperty("properties");
        Assert.Equal(
            "boolean",
            properties.GetProperty("found")
                      .GetProperty("type")
                      .GetString()
        );
        Assert.Equal(
            "boolean",
            properties.GetProperty("pathFound")
                      .GetProperty("type")
                      .GetString()
        );
        Assert.True(properties.TryGetProperty("value", out var valueSchema));
        Assert.Equal(JsonValueKind.True, valueSchema.ValueKind);
        Assert.Equal(
            ["integer", "null"],
            [
                .. properties.GetProperty("revision")
                             .GetProperty("type")
                             .EnumerateArray()
                             .Select(static value => value.GetString()!)
            ]
        );
    }

    [Fact]
    public void McpSchema_TreatsJsonPointerParameterAsString()
    {
        var tool = StatePocketMcpToolFactory.Create((Func<JsonPointer, string>)DescribePointer, _services);
        var propertySchema = GetPropertySchema(tool, "pointer");
        Assert.Equal(
            "string",
            propertySchema.GetProperty("type")
                          .GetString()
        );
    }

    [Fact]
    public void GetValue_ExposesPathSchemaAsString()
    {
        var tool = CreateTool(GetValueTool.ToolName);
        var propertySchema = GetPropertySchema(tool, "path");
        Assert.Equal(
            ["string", "null"],
            [
                .. propertySchema.GetProperty("type")
                                 .EnumerateArray()
                                 .Select(static value => value.GetString()!)
            ]
        );
    }

    [Fact]
    public void GetValues_ExposesPathSchemaAsString()
    {
        var tool = CreateTool(GetValuesTool.ToolName);
        var propertySchema = GetPropertySchema(tool, "path");
        Assert.Equal(
            ["string", "null"],
            [
                .. propertySchema.GetProperty("type")
                                 .EnumerateArray()
                                 .Select(static value => value.GetString()!)
            ]
        );
    }

    [Fact]
    public void QueryValues_ExposesPathSchemaAsString()
    {
        var tool = CreateTool(QueryValuesTool.ToolName);
        var propertySchema = GetPropertySchema(tool, "path");
        Assert.Equal(
            ["string", "null"],
            [
                .. propertySchema.GetProperty("type")
                                 .EnumerateArray()
                                 .Select(static value => value.GetString()!)
            ]
        );
    }

    [Fact]
    public async Task JsonExceptionCallToolFilter_PreservesMalformedJsonPointerMessageForGetValue()
    {
        var tool = CreateTool(GetValueTool.ToolName);
        await using var serverServices = CreateMcpServerServices();
        var request = CreateCallToolRequestContext(
            serverServices.GetRequiredService<McpServer>(),
            GetValueTool.ToolName,
            new Dictionary<string, JsonElement>
            {
                ["key"] = JsonSerializer.SerializeToElement("alpha"),
                ["path"] = JsonSerializer.SerializeToElement("nested/value")
            }
        );
        var handler = StatePocketMcpRegistration.CreateJsonExceptionCallToolFilter(tool.InvokeAsync);
        var result = await handler(request, CancellationToken.None);
        var error = Assert.Single(result.Content);
        var text = Assert.IsType<TextContentBlock>(error);
        Assert.True(result.IsError);
        Assert.Contains("Invalid JSON Pointer path 'nested/value'.", text.Text);
    }

    [Fact]
    public async Task JsonExceptionCallToolFilter_PreservesMalformedJsonPointerMessageForPatchValue()
    {
        var tool = CreateTool(PatchValueTool.ToolName);
        await using var serverServices = CreateMcpServerServices();
        var request = CreateCallToolRequestContext(
            serverServices.GetRequiredService<McpServer>(),
            PatchValueTool.ToolName,
            new Dictionary<string, JsonElement>
            {
                ["key"] = JsonSerializer.SerializeToElement("alpha"),
                ["patch"] = JsonDocument.Parse("""[{"op":"remove","path":"nested/value"}]""")
                                        .RootElement.Clone()
            }
        );
        var handler = StatePocketMcpRegistration.CreateJsonExceptionCallToolFilter(tool.InvokeAsync);
        var result = await handler(request, CancellationToken.None);
        var error = Assert.Single(result.Content);
        var text = Assert.IsType<TextContentBlock>(error);
        Assert.True(result.IsError);
        Assert.Contains("Invalid JSON Pointer path 'nested/value'.", text.Text);
    }

    private static JsonElement GetPropertySchema(McpServerTool tool, string propertyName)
    {
        return tool.ProtocolTool.InputSchema.GetProperty("properties")
                   .GetProperty(propertyName);
    }

    private McpServerTool CreateTool(string toolName)
    {
        return StatePocketMcpRegistration.FindTool(toolName)
                                        ?.Create(_services)
            ?? throw new InvalidOperationException($"Tool '{toolName}' is not registered.");
    }

    private static ServiceProvider CreateMcpServerServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IKvStore, InMemoryKvStore>();
        services.AddSingleton<GetValueTool>();
        services.AddSingleton<GetValuesTool>();
        services.AddSingleton<SetValueTool>();
        services.AddSingleton<QueryValuesTool>();
        services.AddSingleton<PatchValueTool>();
        StatePocketMcpRegistration.AddServer(services)
                                  .WithStreamServerTransport(Stream.Null, Stream.Null);
        return services.BuildServiceProvider();
    }

    private static RequestContext<CallToolRequestParams> CreateCallToolRequestContext(
        McpServer server,
        string toolName,
        IDictionary<string, JsonElement>? arguments
    )
    {
        var request = new JsonRpcRequest
        {
            JsonRpc = "2.0",
            Id = new RequestId(1),
            Method = RequestMethods.ToolsCall
        };
        return new RequestContext<CallToolRequestParams>(
            server,
            request,
            new CallToolRequestParams
            {
                Name = toolName,
                Arguments = arguments
            }
        );
    }

    private static string DescribePointer(JsonPointer pointer)
    {
        return pointer.ToString();
    }

    private sealed class InMemoryKvStore : IKvStore
    {
        public Task<SetValueMetadata> SetValueAsync(
            string? @namespace,
            string key,
            JsonElement value,
            long? ttlSeconds,
            long? expectedRevision = null,
            bool ifAbsent = false,
            CancellationToken cancellationToken = default
        )
        {
            throw new NotSupportedException();
        }

        public Task<KvValue?> GetValueAsync(string? @namespace, string key, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyDictionary<string, KvValue>> GetValuesAsync(
            string? @namespace,
            IReadOnlyList<string> keys,
            CancellationToken cancellationToken
        )
        {
            throw new NotSupportedException();
        }

        public Task<PageResult<KeyValuePair<string, KvValue>>> ListValuesPageAsync(
            string? @namespace,
            string? pattern,
            string? cursor,
            int limit,
            CancellationToken cancellationToken
        )
        {
            throw new NotSupportedException();
        }

        public Task<PageResult<string>> ListNamespacesPageAsync(
            string? pattern,
            string? cursor,
            int limit,
            CancellationToken cancellationToken
        )
        {
            throw new NotSupportedException();
        }

        public Task<PageResult<string>> ListKeysPageAsync(
            string? @namespace,
            string? pattern,
            string? cursor,
            int limit,
            CancellationToken cancellationToken
        )
        {
            throw new NotSupportedException();
        }

        public Task<KvValue?> PatchValueAsync(
            string? @namespace,
            string key,
            JsonPatch patch,
            CancellationToken cancellationToken
        )
        {
            throw new NotSupportedException();
        }

        public Task<bool> DeleteValueAsync(string? @namespace, string key, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task PurgeExpiredAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
