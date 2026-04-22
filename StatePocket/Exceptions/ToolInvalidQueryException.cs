using StatePocket.Errors;

namespace StatePocket.Exceptions;

internal sealed class ToolInvalidQueryException(
    string message,
    string? argument = "query",
    Exception? innerException = null
) : ToolErrorException(message, innerException)
{
    public override ToolError ToPayload()
    {
        return new InvalidQueryToolError
        {
            Message = Message,
            Retryable = false,
            Argument = argument
        };
    }
}
