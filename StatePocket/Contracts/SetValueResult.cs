namespace StatePocket.Contracts;

internal sealed record SetValueResult
{
    public required string Namespace { get; init; }
    public required string Key { get; init; }
    public string? ExpiresAt { get; init; }
    public required string UpdatedAt { get; init; }
    public required long Revision { get; init; }
}
