using StatePocket.Contracts;

namespace StatePocket.Errors;

internal sealed class ToolAlreadyExistsException(string @namespace, string key, long currentRevision)
    : ToolConflictException($"Key '{key}' already exists in namespace '{@namespace}'.", @namespace, key)
{
    public override ToolError ToPayload()
    {
        return new AlreadyExistsToolError
        {
            Message = Message,
            Retryable = false,
            Namespace = Namespace,
            Key = Key,
            CurrentRevision = currentRevision
        };
    }
}
