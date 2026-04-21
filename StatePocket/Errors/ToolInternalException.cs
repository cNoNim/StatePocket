using StatePocket.Contracts;

namespace StatePocket.Errors;

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
