using System.ComponentModel;

namespace StatePocket.Contracts;

internal sealed record ListNamespacesResult
{
    [Description("Page of namespace names in ascending order.")]
    public required IReadOnlyList<string> Namespaces { get; init; }
    [Description("Opaque pagination cursor for the next page, or null when there are no more results.")]
    public string? NextCursor { get; init; }
}
