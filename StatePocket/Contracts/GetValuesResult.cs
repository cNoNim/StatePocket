namespace StatePocket.Contracts;

internal sealed record GetValuesResult
{
    public required string Namespace { get; init; }
    public required IReadOnlyDictionary<string, GetValuesEntry> Values { get; init; }
    public string? NextCursor { get; init; }
}
