using StatePocket.Tools;

namespace StatePocket.Configuration;

internal static class ToolNames
{
    public static IReadOnlyCollection<string> All { get; } =
    [
        SetValueTool.ToolName,
        GetValueTool.ToolName,
        GetValuesTool.ToolName,
        QueryValuesTool.ToolName,
        ListNamespacesTool.ToolName,
        ListKeysTool.ToolName,
        DeleteValueTool.ToolName,
        PatchValueTool.ToolName
    ];
}
