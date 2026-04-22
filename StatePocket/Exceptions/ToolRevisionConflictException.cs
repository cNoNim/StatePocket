using StatePocket.Errors;

namespace StatePocket.Exceptions;

internal sealed class ToolRevisionConflictException(
    string @namespace,
    string key,
    long expectedRevision,
    long? currentRevision = null
) : ToolConflictException(
    currentRevision is null
      ? $"Revision conflict for key '{key}' in namespace '{@namespace}'. Expected revision {expectedRevision}, but the key does not exist."
      : $"Revision conflict for key '{key}' in namespace '{@namespace}'. Expected revision {expectedRevision}, found {currentRevision}.",
    @namespace,
    key
)
{
    public override ToolError ToPayload()
    {
        return new RevisionConflictToolError
        {
            Message = Message,
            Retryable = true,
            Namespace = Namespace,
            Key = Key,
            ExpectedRevision = expectedRevision,
            CurrentRevision = currentRevision
        };
    }
}
