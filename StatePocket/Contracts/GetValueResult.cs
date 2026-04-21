using System.ComponentModel;
using System.Text.Json;

namespace StatePocket.Contracts;

internal sealed record GetValueResult
{
    [Description("Namespace containing the requested key.")]
    public required string Namespace { get; init; }
    [Description("Requested key.")]
    public required string Key { get; init; }
    [Description("True when the key exists.")]
    public required bool Found { get; init; }
    [Description("True when the requested JSON Pointer path exists within the stored value.")]
    public required bool PathFound { get; init; }
    [Description("Stored JSON value, or the projected value when a JSON Pointer path is provided.")]
    public JsonElement? Value { get; init; }
    [Description("Expiration timestamp in UTC ISO 8601 format, or null when the value does not expire.")]
    public string? ExpiresAt { get; init; }
    [Description("Last update timestamp in UTC ISO 8601 format, or null when the key was not found.")]
    public string? UpdatedAt { get; init; }
    [Description("Monotonic revision scoped to the namespace, or null when the key was not found.")]
    public long? Revision { get; init; }
}
