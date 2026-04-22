namespace StatePocket.Storage;

internal sealed record SetValueMetadata
{
    public string? ExpiresAt { get; init; }
    public required string UpdatedAt { get; init; }
    public required long Revision { get; init; }
}
