using StatePocket.Errors;

namespace StatePocket.Exceptions;

internal class ToolBusyException(string message, Exception? innerException = null)
    : ToolErrorException(message, innerException)
{
    public override ToolError ToPayload()
    {
        return new BusyToolError
        {
            Message = Message,
            Retryable = true
        };
    }
}
