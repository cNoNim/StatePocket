using StatePocket.Contracts;

namespace StatePocket.Errors;

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
