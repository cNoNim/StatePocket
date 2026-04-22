using StatePocket.Contracts;

namespace StatePocket.Errors;

internal sealed class ToolInvalidPatchException(
    string message,
    string? argument = "patch",
    string? path = null,
    Exception? innerException = null
) : ToolErrorException(message, innerException)
{
    public override ToolError ToPayload()
    {
        return new InvalidPatchToolError
        {
            Message = Message,
            Retryable = false,
            Argument = argument,
            Path = path
        };
    }
}
