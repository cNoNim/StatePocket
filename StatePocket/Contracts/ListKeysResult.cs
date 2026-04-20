namespace StatePocket.Contracts;

internal sealed record ListKeysResult
{
    public required string Namespace { get; init; }
    public required IReadOnlyList<string> Keys { get; init; }
    public string? NextCursor { get; init; }
}
