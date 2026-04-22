using StatePocket.Contracts;

namespace StatePocket.Errors;

internal sealed class ToolInvalidJsonException(string message, string? path = null, Exception? innerException = null)
    : ToolErrorException(message, innerException)
{
    public override ToolError ToPayload()
    {
        return new InvalidJsonToolError
        {
            Message = Message,
            Retryable = false,
            Path = path
        };
    }
}
