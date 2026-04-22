using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
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
                return WrapWithValidation(
                    McpToolFactory.CreateRaw(method.Method, target, services),
                    static arguments => ToolArgumentValidator.ValidateSetValueArguments(arguments)
                );
            }
        ),
        new(
            GetValueTool.ToolName,
            static services => services.AddSingleton<GetValueTool>(),
            static services =>
            {
                var target = services.GetRequiredService<GetValueTool>();
                var method = target.GetValueAsync;
                return WrapWithValidation(
                    McpToolFactory.CreateRaw(method.Method, target, services),
                    static arguments => ToolArgumentValidator.ValidateGetValueArguments(arguments)
                );
            }
        ),
        new(
            GetValuesTool.ToolName,
            static services => services.AddSingleton<GetValuesTool>(),
            static services =>
            {
                var target = services.GetRequiredService<GetValuesTool>();
                var method = target.GetValuesAsync;
                return WrapWithValidation(
                    McpToolFactory.CreateRaw(method.Method, target, services),
                    static arguments => ToolArgumentValidator.ValidateGetValuesArguments(arguments)
                );
            }
        ),
        new(
            QueryValuesTool.ToolName,
            static services => services.AddSingleton<QueryValuesTool>(),
            static services =>
            {
                var target = services.GetRequiredService<QueryValuesTool>();
                var method = target.QueryValuesAsync;
                return WrapWithValidation(
                    McpToolFactory.CreateRaw(method.Method, target, services),
                    static arguments => ToolArgumentValidator.ValidateQueryValuesArguments(arguments)
                );
            }
        ),
        new(
            ListNamespacesTool.ToolName,
            static services => services.AddSingleton<ListNamespacesTool>(),
            static services =>
            {
                var target = services.GetRequiredService<ListNamespacesTool>();
                var method = target.ListNamespacesAsync;
                return WrapWithValidation(
                    McpToolFactory.CreateRaw(method.Method, target, services),
                    static arguments => ToolArgumentValidator.ValidateListNamespacesArguments(arguments)
                );
            }
        ),
        new(
            ListKeysTool.ToolName,
            static services => services.AddSingleton<ListKeysTool>(),
            static services =>
            {
                var target = services.GetRequiredService<ListKeysTool>();
                var method = target.ListKeysAsync;
                return WrapWithValidation(
                    McpToolFactory.CreateRaw(method.Method, target, services),
                    static arguments => ToolArgumentValidator.ValidateListKeysArguments(arguments)
                );
            }
        ),
        new(
            DeleteValueTool.ToolName,
            static services => services.AddSingleton<DeleteValueTool>(),
            static services =>
            {
                var target = services.GetRequiredService<DeleteValueTool>();
                var method = target.DeleteValueAsync;
                return WrapWithValidation(
                    McpToolFactory.CreateRaw(method.Method, target, services),
                    static arguments => ToolArgumentValidator.ValidateDeleteValueArguments(arguments)
                );
            }
        ),
        new(
            PatchValueTool.ToolName,
            static services => services.AddSingleton<PatchValueTool>(),
            static services =>
            {
                var target = services.GetRequiredService<PatchValueTool>();
                var method = target.PatchValueAsync;
                return WrapWithValidation(
                    McpToolFactory.CreateRaw(method.Method, target, services),
                    static arguments => ToolArgumentValidator.ValidatePatchValueArguments(arguments)
                );
            }
        )
    ];

    private static ToolErrorHandlingMcpServerTool WrapWithValidation(
        McpServerTool tool,
        Action<IDictionary<string, JsonElement>?> validateArguments
    )
    {
        return new ToolErrorHandlingMcpServerTool(
            new ValidatedMcpTool(tool, request => validateArguments(request.Params.Arguments))
        );
    }
}
