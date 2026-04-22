using System.Text.Json;
using ModelContextProtocol.Protocol;
using StatePocket.Errors;

namespace StatePocket.Hosting;

internal static class ToolErrorResultFactory
{
    public static CallToolResult Create(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return Create(ToolErrorFactory.Create(exception));
    }

    private static CallToolResult Create(ToolError payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var structuredContent = JsonSerializer.SerializeToElement(payload, ToolErrorJsonContext.Default.ToolError);
        var serializedPayload = JsonSerializer.Serialize(payload, ToolErrorJsonContext.Default.ToolError);
        return new CallToolResult
        {
            IsError = true,
            Content =
            [
                new TextContentBlock
                {
                    Text = serializedPayload
                }
            ],
            StructuredContent = structuredContent
        };
    }
}
