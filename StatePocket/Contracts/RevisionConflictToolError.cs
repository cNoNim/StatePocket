namespace StatePocket.Contracts;

internal sealed record RevisionConflictToolError : ConflictToolError
{
    public required long ExpectedRevision { get; init; }
    public long? CurrentRevision { get; init; }
}
