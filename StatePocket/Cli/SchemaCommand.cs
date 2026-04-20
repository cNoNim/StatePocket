using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using StatePocket.Configuration;
using StatePocket.Contracts;
using StatePocket.Hosting;
using StatePocket.Json.Patch;
using StatePocket.Storage;

namespace StatePocket.Cli;

internal static class SchemaCommand
{
    public static Command Build()
    {
        Argument<string> toolArgument = new("tool")
        {
            Description = "Tool name to inspect."
        };
        Command schemaCommand = new("schema", "Print the static MCP schema for a tool.")
        {
            toolArgument
        };
        schemaCommand.SetAction((parseResult, cancellationToken) => WriteToolSchemaAsync(
                parseResult.GetValue(toolArgument)!,
                Console.Out,
                Console.Error,
                CreateServices,
                cancellationToken
            )
        );
        return schemaCommand;
    }

    internal static async Task<int> WriteToolSchemaAsync(
        string toolName,
        TextWriter outputWriter,
        TextWriter errorWriter,
        Func<StatePocketMcpToolRegistration, ServiceProvider> createServices,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(outputWriter);
        ArgumentNullException.ThrowIfNull(errorWriter);
        ArgumentNullException.ThrowIfNull(createServices);
        try
        {
            if (StatePocketMcpRegistration.FindTool(toolName) is not
                {} toolRegistration)
            {
                await errorWriter.WriteLineAsync(
                                      $"Unknown tool '{toolName}'. Known tools: {string.Join(", ", ToolNames.All)}."
                                  )
                                 .ConfigureAwait(false);
                return 1;
            }
            var services = createServices(toolRegistration);
            await using (services.ConfigureAwait(false))
            {
                var tool = services.GetServices<McpServerTool>()
                                   .SingleOrDefault(candidate => string.Equals(
                                            candidate.ProtocolTool.Name,
                                            toolName,
                                            StringComparison.Ordinal
                                        )
                                    );
                if (tool is null)
                {
                    await errorWriter.WriteLineAsync($"Failed to load schema for tool '{toolName}'.")
                                     .ConfigureAwait(false);
                    return 1;
                }
                var json = JsonSerializer.Serialize(tool.ProtocolTool, ToolSchemaJsonContext.Default.Tool);
                await outputWriter.WriteLineAsync(json)
                                  .ConfigureAwait(false);
                return 0;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception exception)
        {
            await errorWriter.WriteLineAsync($"Failed to load schema for tool '{toolName}'. {exception.Message}")
                             .ConfigureAwait(false);
            return 1;
        }
    }

    private static ServiceProvider CreateServices(StatePocketMcpToolRegistration toolRegistration)
    {
        ServiceCollection services = new();
        services.AddSingleton<IKvStore, SchemaOnlyKvStore>();
        toolRegistration.AddServices(services);
        var mcpServerBuilder = StatePocketMcpRegistration.AddServer(services);
        toolRegistration.AddTool(mcpServerBuilder);
        return services.BuildServiceProvider();
    }

    private sealed class SchemaOnlyKvStore : IKvStore
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
