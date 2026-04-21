using System.ComponentModel;

namespace StatePocket.Contracts;

internal sealed record QueryValuesResult
{
    [Description("Namespace containing the matched keys.")]
    public required string Namespace { get; init; }
    [Description("Entries keyed by matched key names.")]
    public required IReadOnlyDictionary<string, GetValuesEntry> Values { get; init; }
    [Description("Opaque pagination cursor for the next page, or null when there are no more results.")]
    public string? NextCursor { get; init; }
}
