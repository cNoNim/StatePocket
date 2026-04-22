using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using StatePocket.Configuration;

namespace StatePocket.Hosting;

internal static class McpRegistration
{
    internal static readonly string ServerInstructions = McpPublishedDocumentation.AppendDocumentationLink(
        McpPublishedDocumentation.AppendAboutDocumentationLink(
            "StatePocket provides durable namespaced JSON state backed by SQLite for agents and tools."
        ),
        RuntimeInfoResource.InfoUri
    );
    private static readonly Dictionary<string, McpToolRegistration> ToolRegistrations = McpTools.All.ToDictionary(
        static tool => tool.Name,
        static tool => tool,
        StringComparer.Ordinal
    );

    public static McpToolRegistration? FindTool(string toolName)
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
        assembly ??= typeof(McpHostFactory).Assembly;
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (JsonException exception)
            {
                return ToolErrorResultFactory.Create(exception);
            }
            catch (ArgumentException exception)
            {
                return ToolErrorResultFactory.Create(exception);
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

    public static void AddPublishedDocumentation(IMcpServerBuilder mcpServerBuilder, ResolvedOptions resolvedOptions)
    {
        ArgumentNullException.ThrowIfNull(mcpServerBuilder);
        ArgumentNullException.ThrowIfNull(resolvedOptions);
        var catalog = McpPublishedDocumentation.CreateCatalog(
            resolvedOptions.EnabledTools,
            [RuntimeInfoResource.CreateLinkTarget()]
        );
        mcpServerBuilder.Services.AddSingleton(catalog);
        mcpServerBuilder.WithResources(
            [.. McpPublishedDocumentation.CreateResources(catalog), RuntimeInfoResource.CreateResource(resolvedOptions)]
        );
    }

    private static IEnumerable<McpToolRegistration> GetEnabledTools(IReadOnlyCollection<string> enabledTools)
    {
        foreach (var toolName in enabledTools.Order(StringComparer.Ordinal))
        {
            if (FindTool(toolName) is
                {} tool)
            {
                yield return tool;
            }
        }
    }
}
