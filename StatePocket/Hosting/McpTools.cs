using Microsoft.Extensions.DependencyInjection;
using StatePocket.Tools;

namespace StatePocket.Hosting;

internal static class McpTools
{
    public static IReadOnlyCollection<McpToolRegistration> All { get; } =
    [
        new(
            SetValueTool.ToolName,
            static services => services.AddSingleton<SetValueTool>(),
            static services =>
            {
                var target = services.GetRequiredService<SetValueTool>();
                var method = target.SetValueAsync;
                return McpToolFactory.Create(method.Method, target, services);
            }
        ),
        new(
            GetValueTool.ToolName,
            static services => services.AddSingleton<GetValueTool>(),
            static services =>
            {
                var target = services.GetRequiredService<GetValueTool>();
                var method = target.GetValueAsync;
                return McpToolFactory.Create(method.Method, target, services);
            }
        ),
        new(
            GetValuesTool.ToolName,
            static services => services.AddSingleton<GetValuesTool>(),
            static services =>
            {
                var target = services.GetRequiredService<GetValuesTool>();
                var method = target.GetValuesAsync;
                return McpToolFactory.Create(method.Method, target, services);
            }
        ),
        new(
            QueryValuesTool.ToolName,
            static services => services.AddSingleton<QueryValuesTool>(),
            static services =>
            {
                var target = services.GetRequiredService<QueryValuesTool>();
                var method = target.QueryValuesAsync;
                return McpToolFactory.Create(method.Method, target, services);
            }
        ),
        new(
            ListNamespacesTool.ToolName,
            static services => services.AddSingleton<ListNamespacesTool>(),
            static services =>
            {
                var target = services.GetRequiredService<ListNamespacesTool>();
                var method = target.ListNamespacesAsync;
                return McpToolFactory.Create(method.Method, target, services);
            }
        ),
        new(
            ListKeysTool.ToolName,
            static services => services.AddSingleton<ListKeysTool>(),
            static services =>
            {
                var target = services.GetRequiredService<ListKeysTool>();
                var method = target.ListKeysAsync;
                return McpToolFactory.Create(method.Method, target, services);
            }
        ),
        new(
            DeleteValueTool.ToolName,
            static services => services.AddSingleton<DeleteValueTool>(),
            static services =>
            {
                var target = services.GetRequiredService<DeleteValueTool>();
                var method = target.DeleteValueAsync;
                return McpToolFactory.Create(method.Method, target, services);
            }
        ),
        new(
            PatchValueTool.ToolName,
            static services => services.AddSingleton<PatchValueTool>(),
            static services =>
            {
                var target = services.GetRequiredService<PatchValueTool>();
                var method = target.PatchValueAsync;
                return McpToolFactory.Create(method.Method, target, services);
            }
        )
    ];
}
