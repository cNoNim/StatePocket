using Microsoft.Extensions.DependencyInjection;

namespace StatePocket.Hosting;

internal sealed class StatePocketMcpToolRegistration(
    string name,
    Action<IServiceCollection> addServices,
    Action<IMcpServerBuilder> addTool
)
{
    public string Name { get; } = name;

    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        addServices(services);
    }

    public void AddTool(IMcpServerBuilder mcpServerBuilder)
    {
        ArgumentNullException.ThrowIfNull(mcpServerBuilder);
        addTool(mcpServerBuilder);
    }
}
