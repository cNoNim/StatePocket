using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace StatePocket.Hosting;

internal readonly record struct McpToolRegistration(
    string Name,
    Action<IServiceCollection> RegisterServices,
    Func<IServiceProvider, McpServerTool> CreateTool
)
{
    public McpServerTool Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var tool = CreateTool(services);
        if (services.GetService<McpPublishedDocumentation.PublishedDocumentationCatalog>() is
            {} catalog)
        {
            McpPublishedDocumentation.AppendToolDocumentationLink(catalog, Name, tool);
        }
        return tool;
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
