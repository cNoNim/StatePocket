using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace StatePocket.Hosting;

internal static class StatePocketMcpRegistration
{
    internal const string ServerInstructions = """
                                               This server provides persistent local JSON key-value state for agents and tools.

                                               Use it for durable namespaced JSON state backed by SQLite.

                                               It fits best for small durable state such as checkpoints, caches, preferences, task state, and structured memory that should survive across turns.
                                               """;
    private static readonly Dictionary<string, StatePocketMcpToolRegistration> ToolRegistrations =
        StatePocketMcpTools.All.ToDictionary(static tool => tool.Name, static tool => tool, StringComparer.Ordinal);

    public static StatePocketMcpToolRegistration? FindTool(string toolName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        return ToolRegistrations.TryGetValue(toolName, out var tool) ? tool : null;
    }

    public static IMcpServerBuilder AddServer(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<CallToolExecutionGate>();
        return services.AddMcpServer(static options =>
                            {
                                options.ServerInfo = CreateServerInfo();
                                options.ServerInstructions = ServerInstructions;
                            }
                        )
                       .WithRequestFilters(static filters => filters.AddCallToolFilter(CreateSequentialCallToolFilter)
                                                                    .AddCallToolFilter(
                                                                         CreateJsonExceptionCallToolFilter
                                                                     )
                        );
    }

    internal static Implementation CreateServerInfo(Assembly? assembly = null)
    {
        assembly ??= typeof(StatePocketMcpHostFactory).Assembly;
        var assemblyName = assembly.GetName();
        var title = assembly.GetCustomAttribute<AssemblyTitleAttribute>()
                           ?.Title;
        var description = assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()
                                 ?.Description;
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                               .ToDictionary(
                                    static attribute => attribute.Key,
                                    static attribute => attribute.Value,
                                    StringComparer.Ordinal
                                );
        metadata.TryGetValue("ToolCommandName", out var toolCommandName);
        metadata.TryGetValue("PackageId", out var packageId);
        metadata.TryGetValue("PackageProjectUrl", out var packageProjectUrl);
        metadata.TryGetValue("RepositoryUrl", out var repositoryUrl);
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                                          ?.InformationalVersion;
        var version = informationalVersion?.Split('+', 2)[0];
        return new Implementation
        {
            Name = toolCommandName ?? packageId ?? assemblyName.Name ?? "statepocket",
            Title = title,
            Version = version ?? assemblyName.Version?.ToString() ?? "0.0.0",
            Description = description,
            WebsiteUrl = packageProjectUrl ?? repositoryUrl
        };
    }

    internal static McpRequestHandler<CallToolRequestParams, CallToolResult> CreateSequentialCallToolFilter(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next
    )
    {
        return (request, cancellationToken) =>
        {
            var services = request.Services
                        ?? request.Server.Services
                        ?? throw new InvalidOperationException("Request services are unavailable.");
            var gate = services.GetRequiredService<CallToolExecutionGate>();
            return IsReadOnlyRequest(request)
              ? gate.ExecuteReadAsync(
                    (next, request),
                    static (state, ct) => state.next(state.request, ct),
                    cancellationToken
                )
              : gate.ExecuteWriteAsync(
                    (next, request),
                    static (state, ct) => state.next(state.request, ct),
                    cancellationToken
                );
        };
    }

    private static bool IsReadOnlyRequest(RequestContext<CallToolRequestParams> request)
    {
        return request.MatchedPrimitive is McpServerTool
        {
            ProtocolTool.Annotations.ReadOnlyHint: true
        };
    }

    internal static McpRequestHandler<CallToolRequestParams, CallToolResult> CreateJsonExceptionCallToolFilter(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next
    )
    {
        return async (request, cancellationToken) =>
        {
            try
            {
                return await next(request, cancellationToken)
                   .ConfigureAwait(false);
            }
            catch (JsonException exception)
            {
                return new CallToolResult
                {
                    IsError = true,
                    Content =
                    [
                        new TextContentBlock
                        {
                            Text = exception.Message
                        }
                    ]
                };
            }
            catch (ArgumentException exception)
            {
                return new CallToolResult
                {
                    IsError = true,
                    Content =
                    [
                        new TextContentBlock
                        {
                            Text = exception.Message
                        }
                    ]
                };
            }
        };
    }

    public static void AddToolServices(IServiceCollection services, IReadOnlyCollection<string> enabledTools)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(enabledTools);
        foreach (var tool in GetEnabledTools(enabledTools))
        {
            tool.AddServices(services);
        }
    }

    public static void AddEnabledTools(IMcpServerBuilder mcpServerBuilder, IReadOnlyCollection<string> enabledTools)
    {
        ArgumentNullException.ThrowIfNull(mcpServerBuilder);
        ArgumentNullException.ThrowIfNull(enabledTools);
        foreach (var tool in GetEnabledTools(enabledTools))
        {
            tool.AddTool(mcpServerBuilder);
        }
    }

    private static IEnumerable<StatePocketMcpToolRegistration> GetEnabledTools(IReadOnlyCollection<string> enabledTools)
    {
        foreach (var toolName in enabledTools.OrderBy(static tool => tool, StringComparer.Ordinal))
        {
            if (FindTool(toolName) is
                {} tool)
            {
                yield return tool;
            }
        }
    }
}
