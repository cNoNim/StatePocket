namespace StatePocket.Storage;

internal sealed class SetValueMetadata
{
    public string? ExpiresAt { get; init; }
    public required string UpdatedAt { get; init; }
    public required long Revision { get; init; }
}
