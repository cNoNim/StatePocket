namespace StatePocket.Storage;

internal sealed record PageResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required string? NextCursor { get; init; }
}
