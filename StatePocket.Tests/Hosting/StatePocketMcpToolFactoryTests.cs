using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
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
                                                                         .AddSingleton<ListNamespacesTool>()
                                                                         .AddSingleton<PatchValueTool>()
                                                                         .BuildServiceProvider();

    [Fact]
    public void CreateServerInfo_UsesReadableServerMetadata()
    {
        var serverInfo = StatePocketMcpRegistration.CreateServerInfo();
        var informationalVersion = typeof(StatePocketMcpHostFactory)
                                  .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                                 ?.InformationalVersion;
        Assert.Equal("statepocket", serverInfo.Name);
        Assert.Equal("State Pocket", serverInfo.Title);
        Assert.Equal(informationalVersion?.Split('+', 2)[0], serverInfo.Version);
        Assert.False(string.IsNullOrWhiteSpace(serverInfo.Description));
        Assert.False(string.IsNullOrWhiteSpace(serverInfo.WebsiteUrl));
        Assert.DoesNotContain('+', serverInfo.Version);
    }

    [Fact]
    public void CreateServerInstructions_DescribePersistentJsonState()
    {
        const string instructions = StatePocketMcpRegistration.ServerInstructions;
        Assert.Contains("persistent local JSON key-value state", instructions);
        Assert.Contains("durable namespaced JSON state", instructions);
        Assert.Contains("SQLite", instructions);
    }

    [Fact]
    public void SetValue_ExposesStringValueAndFormatSchemas()
    {
        var tool = CreateTool(SetValueTool.ToolName);
        Assert.Equal(
            "Stores a JSON value under a key in the selected namespace, creating the key or replacing its current value.",
            tool.ProtocolTool.Description
        );
        var valueSchema = GetPropertySchema(tool, "value");
        var formatSchema = GetPropertySchema(tool, "format");
        Assert.Equal(
            "Value to store. Use format 'json' to parse this string as JSON text, or 'text' to store it as a JSON string.",
            valueSchema.GetProperty("description")
                       .GetString()
        );
        Assert.Equal(
            "string",
            valueSchema.GetProperty("type")
                       .GetString()
        );
        Assert.Equal(
            "string",
            formatSchema.GetProperty("type")
                        .GetString()
        );
        Assert.Equal(
            ["text", "json"],
            [
                .. formatSchema.GetProperty("enum")
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
            "Optional TTL in whole seconds. Omit to store the value without expiration.",
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
            "Optional expected revision for compare-and-set writes. When provided, the write succeeds only if the current live value has this exact revision. Cannot be combined with ifAbsent.",
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
            "When true, create the key only if no live value currently exists. Cannot be combined with expectedRevision.",
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
            "Namespace containing the stored key.",
            properties.GetProperty("namespace")
                      .GetProperty("description")
                      .GetString()
        );
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
        Assert.Equal(
            "Monotonic revision scoped to the namespace. A new namespace starts at revision 1.",
            properties.GetProperty("revision")
                      .GetProperty("description")
                      .GetString()
        );
        Assert.False(outputSchema.TryGetProperty("allOf", out _));
    }

    [Fact]
    public void SetValue_DoesNotPublishTopLevelAllOfInInputSchema()
    {
        var tool = CreateTool(SetValueTool.ToolName);
        Assert.False(tool.ProtocolTool.InputSchema.TryGetProperty("allOf", out _));
    }

    [Fact]
    public void PatchValue_ExposesStringPatchSchema()
    {
        var tool = CreateTool(PatchValueTool.ToolName);
        var propertySchema = GetPropertySchema(tool, "patch");
        Assert.Equal(
            "JSON Patch document to apply, encoded as JSON text.",
            propertySchema.GetProperty("description")
                          .GetString()
        );
        Assert.Equal(
            "string",
            propertySchema.GetProperty("type")
                          .GetString()
        );
    }

    [Fact]
    public void QueryValues_ExposesStringEqualsAndFormatSchemas()
    {
        var tool = CreateTool(QueryValuesTool.ToolName);
        var equalsSchema = GetPropertySchema(tool, "equals");
        var formatSchema = GetPropertySchema(tool, "format");
        Assert.Equal(
            "Optional value that at least one query match must equal. Requires query. When format is 'json', provide JSON text. When format is 'text', the raw string is matched as a JSON string. Pass explicit null to match JSON nulls.",
            equalsSchema.GetProperty("description")
                        .GetString()
        );
        Assert.Equal(
            ["string", "null"],
            [
                .. equalsSchema.GetProperty("type")
                               .EnumerateArray()
                               .Select(static value => value.GetString()!)
            ]
        );
        Assert.Equal(
            JsonValueKind.Null,
            equalsSchema.GetProperty("default")
                        .ValueKind
        );
        Assert.Equal(
            "string",
            formatSchema.GetProperty("type")
                        .GetString()
        );
        Assert.Equal(
            ["text", "json"],
            [
                .. formatSchema.GetProperty("enum")
                               .EnumerateArray()
                               .Select(static value => value.GetString()!)
            ]
        );
    }

    [Fact]
    public void QueryValues_DoesNotPublishTopLevelAllOfInInputSchema()
    {
        var tool = CreateTool(QueryValuesTool.ToolName);
        Assert.False(tool.ProtocolTool.InputSchema.TryGetProperty("allOf", out _));
    }

    [Fact]
    public void ListNamespaces_ExposesLiveNamespaceDescription()
    {
        var tool = CreateTool(ListNamespacesTool.ToolName);
        Assert.Equal(
            "Lists namespaces that currently contain at least one live, unexpired key, optionally filtered by a wildcard pattern.",
            tool.ProtocolTool.Description
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
    public void GetValue_ExposesUntypedValueOutputSchema()
    {
        var tool = CreateTool(GetValueTool.ToolName);
        var outputSchema = tool.ProtocolTool.OutputSchema
                        ?? throw new InvalidOperationException("Expected output schema.");
        var properties = outputSchema.GetProperty("properties");
        Assert.Equal(
            "True when the key exists.",
            properties.GetProperty("found")
                      .GetProperty("description")
                      .GetString()
        );
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
        Assert.False(valueSchema.TryGetProperty("type", out _));
        Assert.Equal(
            "Stored JSON value, or the projected value when a JSON Pointer path is provided.",
            valueSchema.GetProperty("description")
                       .GetString()
        );
        Assert.Equal(
            ["integer", "null"],
            [
                .. properties.GetProperty("revision")
                             .GetProperty("type")
                             .EnumerateArray()
                             .Select(static value => value.GetString()!)
            ]
        );
        Assert.Equal(
            "Monotonic revision scoped to the namespace, or null when the key was not found.",
            properties.GetProperty("revision")
                      .GetProperty("description")
                      .GetString()
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
                ["patch"] = JsonSerializer.SerializeToElement("""[{"op":"remove","path":"nested/value"}]""")
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
    public async Task JsonExceptionCallToolFilter_RejectsUnsupportedNumericFormatForSetValue()
    {
        var tool = CreateTool(SetValueTool.ToolName);
        await using var serverServices = CreateMcpServerServices();
        var request = CreateCallToolRequestContext(
            serverServices.GetRequiredService<McpServer>(),
            SetValueTool.ToolName,
            new Dictionary<string, JsonElement>
            {
                ["key"] = JsonSerializer.SerializeToElement("alpha"),
                ["value"] = JsonSerializer.SerializeToElement("""{"ok":true}"""),
                ["format"] = JsonSerializer.SerializeToElement((JsonNode?)5)
            }
        );
        var handler = StatePocketMcpRegistration.CreateJsonExceptionCallToolFilter(tool.InvokeAsync);
        var result = await handler(request, CancellationToken.None);
        var error = Assert.Single(result.Content);
        var text = Assert.IsType<TextContentBlock>(error);
        Assert.True(result.IsError);
        Assert.Contains("format", text.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task JsonExceptionCallToolFilter_RejectsZeroFormatForSetValue()
    {
        var tool = CreateTool(SetValueTool.ToolName);
        await using var serverServices = CreateMcpServerServices();
        var request = CreateCallToolRequestContext(
            serverServices.GetRequiredService<McpServer>(),
            SetValueTool.ToolName,
            new Dictionary<string, JsonElement>
            {
                ["key"] = JsonSerializer.SerializeToElement("alpha"),
                ["value"] = JsonSerializer.SerializeToElement("""{"ok":true}"""),
                ["format"] = JsonSerializer.SerializeToElement((JsonNode?)0)
            }
        );
        var handler = StatePocketMcpRegistration.CreateJsonExceptionCallToolFilter(tool.InvokeAsync);
        var result = await handler(request, CancellationToken.None);
        var error = Assert.Single(result.Content);
        var text = Assert.IsType<TextContentBlock>(error);
        Assert.True(result.IsError);
        Assert.Contains("format", text.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task JsonExceptionCallToolFilter_RejectsNullValueForSetValue()
    {
        var tool = CreateTool(SetValueTool.ToolName);
        await using var serverServices = CreateMcpServerServices();
        var request = CreateCallToolRequestContext(
            serverServices.GetRequiredService<McpServer>(),
            SetValueTool.ToolName,
            new Dictionary<string, JsonElement>
            {
                ["key"] = JsonSerializer.SerializeToElement("alpha"),
                ["value"] = JsonSerializer.SerializeToElement((JsonNode?)null)
            }
        );
        var handler = StatePocketMcpRegistration.CreateJsonExceptionCallToolFilter(tool.InvokeAsync);
        var result = await handler(request, CancellationToken.None);
        var error = Assert.Single(result.Content);
        var text = Assert.IsType<TextContentBlock>(error);
        Assert.True(result.IsError);
        Assert.Equal("value is required and must not be null.", text.Text);
    }

    [Fact]
    public async Task JsonExceptionCallToolFilter_RejectsMissingValueForSetValue()
    {
        var tool = CreateTool(SetValueTool.ToolName);
        await using var serverServices = CreateMcpServerServices();
        var request = CreateCallToolRequestContext(
            serverServices.GetRequiredService<McpServer>(),
            SetValueTool.ToolName,
            new Dictionary<string, JsonElement>
            {
                ["key"] = JsonSerializer.SerializeToElement("alpha")
            }
        );
        var handler = StatePocketMcpRegistration.CreateJsonExceptionCallToolFilter(tool.InvokeAsync);
        var result = await handler(request, CancellationToken.None);
        var error = Assert.Single(result.Content);
        var text = Assert.IsType<TextContentBlock>(error);
        Assert.True(result.IsError);
        Assert.Contains("required parameter 'value'", text.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JsonExceptionCallToolFilter_RejectsUnsupportedNumericFormatForQueryValues()
    {
        var tool = CreateTool(QueryValuesTool.ToolName);
        await using var serverServices = CreateMcpServerServices();
        var request = CreateCallToolRequestContext(
            serverServices.GetRequiredService<McpServer>(),
            QueryValuesTool.ToolName,
            new Dictionary<string, JsonElement>
            {
                ["query"] = JsonSerializer.SerializeToElement("$.status"),
                ["equals"] = JsonSerializer.SerializeToElement("active"),
                ["format"] = JsonSerializer.SerializeToElement((JsonNode?)5)
            }
        );
        var handler = StatePocketMcpRegistration.CreateJsonExceptionCallToolFilter(tool.InvokeAsync);
        var result = await handler(request, CancellationToken.None);
        var error = Assert.Single(result.Content);
        var text = Assert.IsType<TextContentBlock>(error);
        Assert.True(result.IsError);
        Assert.Contains("format", text.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task JsonExceptionCallToolFilter_RejectsOneFormatForQueryValues()
    {
        var tool = CreateTool(QueryValuesTool.ToolName);
        await using var serverServices = CreateMcpServerServices();
        var request = CreateCallToolRequestContext(
            serverServices.GetRequiredService<McpServer>(),
            QueryValuesTool.ToolName,
            new Dictionary<string, JsonElement>
            {
                ["query"] = JsonSerializer.SerializeToElement("$.status"),
                ["equals"] = JsonSerializer.SerializeToElement("active"),
                ["format"] = JsonSerializer.SerializeToElement((JsonNode?)1)
            }
        );
        var handler = StatePocketMcpRegistration.CreateJsonExceptionCallToolFilter(tool.InvokeAsync);
        var result = await handler(request, CancellationToken.None);
        var error = Assert.Single(result.Content);
        var text = Assert.IsType<TextContentBlock>(error);
        Assert.True(result.IsError);
        Assert.Contains("format", text.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SequentialCallToolFilter_AllowsConcurrentReadOnlyRequests()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var serverServices = CreateMcpServerServices();
        var server = serverServices.GetRequiredService<McpServer>();
        TaskCompletionSource firstEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource secondEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler =
            StatePocketMcpRegistration.CreateSequentialCallToolFilter(async (request, requestCancellationToken) =>
                {
                    if (request.Params is
                        {
                            Name: GetValueTool.ToolName
                        })
                    {
                        firstEntered.SetResult();
                        await releaseFirst.Task.WaitAsync(requestCancellationToken);
                        return new CallToolResult();
                    }
                    secondEntered.SetResult();
                    return new CallToolResult();
                }
            );
        var firstTask = handler(
                CreateCallToolRequestContext(
                    server,
                    GetValueTool.ToolName,
                    new Dictionary<string, JsonElement>
                    {
                        ["key"] = JsonSerializer.SerializeToElement("alpha")
                    }
                ),
                cancellationToken
            )
           .AsTask();
        await firstEntered.Task.WaitAsync(cancellationToken);
        var secondTask = handler(
                CreateCallToolRequestContext(
                    server,
                    ListKeysTool.ToolName,
                    new Dictionary<string, JsonElement>
                    {
                        ["namespace"] = JsonSerializer.SerializeToElement("alpha")
                    }
                ),
                cancellationToken
            )
           .AsTask();
        await secondEntered.Task.WaitAsync(cancellationToken);
        Assert.True(secondTask.IsCompleted);
        releaseFirst.SetResult();
        await firstTask;
    }

    [Fact]
    public async Task SequentialCallToolFilter_WriterBlocksLaterReadersUntilItRuns()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var serverServices = CreateMcpServerServices();
        var server = serverServices.GetRequiredService<McpServer>();
        TaskCompletionSource firstReaderEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseFirstReader = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource writerEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseWriter = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource secondReaderEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler =
            StatePocketMcpRegistration.CreateSequentialCallToolFilter(async (request, requestCancellationToken) =>
                {
                    switch (request.Params.Name)
                    {
                        case GetValueTool.ToolName:
                            if (!firstReaderEntered.Task.IsCompleted)
                            {
                                firstReaderEntered.SetResult();
                                await releaseFirstReader.Task.WaitAsync(requestCancellationToken);
                                return new CallToolResult();
                            }
                            secondReaderEntered.SetResult();
                            return new CallToolResult();
                        case SetValueTool.ToolName:
                            writerEntered.SetResult();
                            await releaseWriter.Task.WaitAsync(requestCancellationToken);
                            return new CallToolResult();
                        default:
                            return new CallToolResult();
                    }
                }
            );
        var firstReaderTask = handler(
                CreateCallToolRequestContext(
                    server,
                    GetValueTool.ToolName,
                    new Dictionary<string, JsonElement>
                    {
                        ["key"] = JsonSerializer.SerializeToElement("alpha")
                    }
                ),
                cancellationToken
            )
           .AsTask();
        await firstReaderEntered.Task.WaitAsync(cancellationToken);
        var writerTask = handler(
                CreateCallToolRequestContext(
                    server,
                    SetValueTool.ToolName,
                    new Dictionary<string, JsonElement>
                    {
                        ["key"] = JsonSerializer.SerializeToElement("alpha"),
                        ["value"] = JsonSerializer.SerializeToElement(1)
                    }
                ),
                cancellationToken
            )
           .AsTask();
        Assert.False(writerTask.IsCompleted);
        var secondReaderTask = handler(
                CreateCallToolRequestContext(
                    server,
                    GetValueTool.ToolName,
                    new Dictionary<string, JsonElement>
                    {
                        ["key"] = JsonSerializer.SerializeToElement("beta")
                    }
                ),
                cancellationToken
            )
           .AsTask();
        Assert.False(secondReaderTask.IsCompleted);
        Assert.False(secondReaderEntered.Task.IsCompleted);
        releaseFirstReader.SetResult();
        await writerEntered.Task.WaitAsync(cancellationToken);
        Assert.False(secondReaderTask.IsCompleted);
        Assert.False(secondReaderEntered.Task.IsCompleted);
        releaseWriter.SetResult();
        await Task.WhenAll(firstReaderTask, writerTask, secondReaderTask);
        await secondReaderEntered.Task.WaitAsync(cancellationToken);
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
        services.AddSingleton<ListKeysTool>();
        services.AddSingleton<ListNamespacesTool>();
        services.AddSingleton<DeleteValueTool>();
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
        )
        {
            MatchedPrimitive = StatePocketMcpRegistration.FindTool(toolName)
                                                        ?.Create(
                                                              server.Services
                                                           ?? throw new InvalidOperationException(
                                                                  "Server services are unavailable."
                                                              )
                                                          )
        };
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
