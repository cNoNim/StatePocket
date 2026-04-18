using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StatePocket.Configuration;
using StatePocket.Storage;

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
        StatePocketMcpRegistration.AddToolServices(builder.Services, resolvedOptions.EnabledTools);
        var mcpServerBuilder = StatePocketMcpRegistration.AddServer(builder.Services)
                                                         .WithStdioServerTransport();
        StatePocketMcpRegistration.AddEnabledTools(mcpServerBuilder, resolvedOptions.EnabledTools);
        return builder.Build();
    }
}
