namespace StatePocket.Contracts;

internal sealed record ListNamespacesResult
{
    public required IReadOnlyList<string> Namespaces { get; init; }
    public string? NextCursor { get; init; }
}
