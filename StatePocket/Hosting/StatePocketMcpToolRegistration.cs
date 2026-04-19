using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace StatePocket.Hosting;

internal readonly record struct StatePocketMcpToolRegistration(
    string Name,
    Action<IServiceCollection> RegisterServices,
    Func<IServiceProvider, McpServerTool> CreateTool
)
{
    public McpServerTool Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return CreateTool(services);
    }

    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        RegisterServices(services);
    }

    public void AddTool(IMcpServerBuilder mcpServerBuilder)
    {
        ArgumentNullException.ThrowIfNull(mcpServerBuilder);
        mcpServerBuilder.Services.AddSingleton(Create);
    }
}
