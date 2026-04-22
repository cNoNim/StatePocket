using StatePocket.Errors;

namespace StatePocket.Exceptions;

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
