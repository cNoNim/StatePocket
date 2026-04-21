using System.ComponentModel;

namespace StatePocket.Contracts;

internal sealed record GetValuesResult
{
    [Description("Namespace containing the requested keys.")]
    public required string Namespace { get; init; }
    [Description("Entries keyed by the requested key names.")]
    public required IReadOnlyDictionary<string, GetValuesEntry> Values { get; init; }
    [Description("Always null. Included for response shape consistency.")]
    public string? NextCursor { get; init; }
}
