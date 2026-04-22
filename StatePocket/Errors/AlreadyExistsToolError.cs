namespace StatePocket.Errors;

internal sealed record AlreadyExistsToolError : ConflictToolError
{
    public required long CurrentRevision { get; init; }
}
