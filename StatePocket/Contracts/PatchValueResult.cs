using System.ComponentModel;
using System.Text.Json;

namespace StatePocket.Contracts;

internal sealed record PatchValueResult
{
    [Description("Namespace containing the patched key.")]
    public required string Namespace { get; init; }
    [Description("Patched key.")]
    public required string Key { get; init; }
    [Description("Updated JSON value after applying the patch.")]
    public required JsonElement Value { get; init; }
    [Description("Expiration timestamp in UTC ISO 8601 format, or null when the value does not expire.")]
    public string? ExpiresAt { get; init; }
    [Description("Last update timestamp in UTC ISO 8601 format.")]
    public required string UpdatedAt { get; init; }
    [Description("Monotonic revision scoped to the namespace. A new namespace starts at revision 1.")]
    public required long Revision { get; init; }
}
