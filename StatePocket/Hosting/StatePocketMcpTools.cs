using Microsoft.Extensions.DependencyInjection;
using StatePocket.Tools;

namespace StatePocket.Hosting;

internal static class StatePocketMcpTools
{
    public static IReadOnlyCollection<StatePocketMcpToolRegistration> All { get; } =
    [
        CreateSetValue(),
        CreateGetValue(),
        CreateGetValues(),
        CreateQueryValues(),
        CreateListNamespaces(),
        CreateListKeys(),
        CreateDeleteValue(),
        CreatePatchValue()
    ];

    private static StatePocketMcpToolRegistration CreateSetValue()
    {
        return new StatePocketMcpToolRegistration(
            SetValueTool.ToolName,
            static services => services.AddSingleton<SetValueTool>(),
            static mcpServerBuilder => mcpServerBuilder.Services.AddSingleton(StatePocketMcpToolFactory.CreateSetValue)
        );
    }

    private static StatePocketMcpToolRegistration CreateGetValue()
    {
        return new StatePocketMcpToolRegistration(
            GetValueTool.ToolName,
            static services => services.AddSingleton<GetValueTool>(),
            static mcpServerBuilder => mcpServerBuilder.Services.AddSingleton(StatePocketMcpToolFactory.CreateGetValue)
        );
    }

    private static StatePocketMcpToolRegistration CreateGetValues()
    {
        return new StatePocketMcpToolRegistration(
            GetValuesTool.ToolName,
            static services => services.AddSingleton<GetValuesTool>(),
            static mcpServerBuilder => mcpServerBuilder.Services.AddSingleton(StatePocketMcpToolFactory.CreateGetValues)
        );
    }

    private static StatePocketMcpToolRegistration CreateQueryValues()
    {
        return new StatePocketMcpToolRegistration(
            QueryValuesTool.ToolName,
            static services => services.AddSingleton<QueryValuesTool>(),
            static mcpServerBuilder =>
                mcpServerBuilder.Services.AddSingleton(StatePocketMcpToolFactory.CreateQueryValues)
        );
    }

    private static StatePocketMcpToolRegistration CreateListNamespaces()
    {
        return new StatePocketMcpToolRegistration(
            ListNamespacesTool.ToolName,
            static services => services.AddSingleton<ListNamespacesTool>(),
            static mcpServerBuilder => mcpServerBuilder.WithTools<ListNamespacesTool>()
        );
    }

    private static StatePocketMcpToolRegistration CreateListKeys()
    {
        return new StatePocketMcpToolRegistration(
            ListKeysTool.ToolName,
            static services => services.AddSingleton<ListKeysTool>(),
            static mcpServerBuilder => mcpServerBuilder.WithTools<ListKeysTool>()
        );
    }

    private static StatePocketMcpToolRegistration CreateDeleteValue()
    {
        return new StatePocketMcpToolRegistration(
            DeleteValueTool.ToolName,
            static services => services.AddSingleton<DeleteValueTool>(),
            static mcpServerBuilder => mcpServerBuilder.WithTools<DeleteValueTool>()
        );
    }

    private static StatePocketMcpToolRegistration CreatePatchValue()
    {
        return new StatePocketMcpToolRegistration(
            PatchValueTool.ToolName,
            static services => services.AddSingleton<PatchValueTool>(),
            static mcpServerBuilder =>
                mcpServerBuilder.Services.AddSingleton(StatePocketMcpToolFactory.CreatePatchValue)
        );
    }
}
