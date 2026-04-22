using StatePocket.Errors;

namespace StatePocket.Exceptions;

internal sealed class ToolNotFoundException(string @namespace, string key)
    : ToolErrorException($"Key '{key}' was not found in namespace '{@namespace}'.")
{
    public override ToolError ToPayload()
    {
        return new NotFoundToolError
        {
            Message = Message,
            Retryable = false,
            Namespace = @namespace,
            Key = key
        };
    }
}
