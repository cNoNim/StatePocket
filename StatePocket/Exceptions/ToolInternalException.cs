using StatePocket.Errors;

namespace StatePocket.Exceptions;

internal sealed class ToolInternalException(string message, Exception? innerException = null)
    : ToolErrorException(message, innerException)
{
    public override ToolError ToPayload()
    {
        return new InternalToolError
        {
            Message = Message,
            Retryable = false
        };
    }
}
