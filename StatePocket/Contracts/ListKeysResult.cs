using System.ComponentModel;

namespace StatePocket.Contracts;

internal sealed record ListKeysResult
{
    [Description("Namespace whose keys are listed.")]
    public required string Namespace { get; init; }
    [Description("Page of keys in ascending order.")]
    public required IReadOnlyList<string> Keys { get; init; }
    [Description("Last key in this page to use as the next cursor, or null when there are no more results.")]
    public string? NextCursor { get; init; }
}
