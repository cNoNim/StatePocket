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
        var tool = StatePocketMcpToolFactory.CreateSetValue(_services);
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
    public void PatchValue_ExposesTypedPatchSchema()
    {
        var tool = StatePocketMcpToolFactory.CreatePatchValue(_services);
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
        var tool = StatePocketMcpToolFactory.CreateQueryValues(_services);
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
        var tool = StatePocketMcpToolFactory.CreateGetValue(_services);
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
        var tool = StatePocketMcpToolFactory.CreateGetValues(_services);
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
        var tool = StatePocketMcpToolFactory.CreateQueryValues(_services);
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
        var tool = StatePocketMcpToolFactory.CreateGetValue(_services);
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
        var tool = StatePocketMcpToolFactory.CreatePatchValue(_services);
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
        public Task SetValueAsync(
            string? @namespace,
            string key,
            JsonElement value,
            long? ttlSeconds,
            CancellationToken cancellationToken
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

        public Task<bool> PatchValueAsync(
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
