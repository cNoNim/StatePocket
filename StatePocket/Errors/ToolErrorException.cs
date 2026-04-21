using System.Text.Json;
using ModelContextProtocol;
using StatePocket.Contracts;

namespace StatePocket.Errors;

internal abstract class ToolErrorException(string message, Exception? innerException = null)
    : McpException(message, innerException)
{
    public JsonElement ToStructuredContent()
    {
        return JsonSerializer.SerializeToElement(ToPayload(), ToolErrorJsonContext.Default.ToolError);
    }

    public abstract ToolError ToPayload();
}
