using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using StatePocket.Configuration;
using StatePocket.Storage;
using StatePocket.Tools;

namespace StatePocket.Hosting;

internal static class StatePocketMcpHostFactory
{
    public static IHost Create(ResolvedOptions resolvedOptions)
    {
        ArgumentNullException.ThrowIfNull(resolvedOptions);
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(resolvedOptions);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<SqliteDatabaseInitializer>();
        builder.Services.AddSingleton<IKvStore, SqliteKvStore>();
        AddToolServices(builder.Services);
        var mcpServerBuilder = builder.Services.AddMcpServer(static options =>
                                           {
                                               options.ServerInfo = new Implementation
                                               {
                                                   Name = "statepocket",
                                                   Version = typeof(StatePocketMcpHostFactory).Assembly.GetName()
                                                                                              .Version?.ToString()
                                                          ?? "0.0.0"
                                               };
                                           }
                                       )
                                      .WithStdioServerTransport();
        ConfigureEnabledTools(resolvedOptions, mcpServerBuilder);
        return builder.Build();
    }

    private static void AddToolServices(IServiceCollection services)
    {
        services.AddSingleton<SetValueTool>();
        services.AddSingleton<GetValueTool>();
        services.AddSingleton<GetValuesTool>();
        services.AddSingleton<QueryValuesTool>();
        services.AddSingleton<ListNamespacesTool>();
        services.AddSingleton<ListKeysTool>();
        services.AddSingleton<DeleteValueTool>();
        services.AddSingleton<PatchValueTool>();
    }

    private static void ConfigureEnabledTools(ResolvedOptions resolvedOptions, IMcpServerBuilder mcpServerBuilder)
    {
        if (resolvedOptions.IsToolEnabled(ToolNames.SetValue))
        {
            mcpServerBuilder.WithTools<SetValueTool>();
        }
        if (resolvedOptions.IsToolEnabled(ToolNames.GetValue))
        {
            mcpServerBuilder.WithTools<GetValueTool>();
        }
        if (resolvedOptions.IsToolEnabled(ToolNames.GetValues))
        {
            mcpServerBuilder.WithTools<GetValuesTool>();
        }
        if (resolvedOptions.IsToolEnabled(ToolNames.QueryValues))
        {
            mcpServerBuilder.WithTools<QueryValuesTool>();
        }
        if (resolvedOptions.IsToolEnabled(ToolNames.ListNamespaces))
        {
            mcpServerBuilder.WithTools<ListNamespacesTool>();
        }
        if (resolvedOptions.IsToolEnabled(ToolNames.ListKeys))
        {
            mcpServerBuilder.WithTools<ListKeysTool>();
        }
        if (resolvedOptions.IsToolEnabled(ToolNames.DeleteValue))
        {
            mcpServerBuilder.WithTools<DeleteValueTool>();
        }
        if (resolvedOptions.IsToolEnabled(ToolNames.PatchValue))
        {
            mcpServerBuilder.WithTools<PatchValueTool>();
        }
    }
}
