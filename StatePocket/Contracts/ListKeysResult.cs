using System.ComponentModel;

namespace StatePocket.Contracts;

internal sealed record ListKeysResult
{
    [Description("Namespace whose keys are listed.")]
    public required string Namespace { get; init; }
    [Description("Page of keys in ascending order.")]
    public required IReadOnlyList<string> Keys { get; init; }
    [Description("Opaque pagination cursor for the next page, or null when there are no more results.")]
    public string? NextCursor { get; init; }
}
