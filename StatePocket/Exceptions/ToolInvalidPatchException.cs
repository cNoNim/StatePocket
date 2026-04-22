using StatePocket.Errors;

namespace StatePocket.Exceptions;

internal sealed class ToolInvalidPatchException(
    string message,
    string? argument = "patch",
    string? path = null,
    int? operationIndex = null,
    string? operation = null,
    string? targetPath = null,
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
            Path = path,
            OperationIndex = operationIndex,
            Operation = operation,
            TargetPath = targetPath
        };
    }
}
