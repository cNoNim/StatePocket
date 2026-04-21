using StatePocket.Contracts;

namespace StatePocket.Errors;

internal sealed class ToolValidationException(string message, string? argument = null, Exception? innerException = null)
    : ToolErrorException(message, innerException)
{
    public override ToolError ToPayload()
    {
        return new InvalidInputToolError
        {
            Message = Message,
            Retryable = false,
            Argument = argument
        };
    }
}
