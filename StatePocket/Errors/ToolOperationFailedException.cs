using StatePocket.Contracts;

namespace StatePocket.Errors;

internal sealed class ToolOperationFailedException(string message, Exception? innerException = null)
    : ToolErrorException(message, innerException)
{
    public override ToolError ToPayload()
    {
        return new OperationFailedToolError
        {
            Message = Message,
            Retryable = false
        };
    }
}
