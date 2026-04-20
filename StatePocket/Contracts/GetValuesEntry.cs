using System.Text.Json;

namespace StatePocket.Contracts;

internal sealed record GetValuesEntry
{
    public required bool Found { get; init; }
    public required bool PathFound { get; init; }
    public JsonElement? Value { get; init; }
    public string? ExpiresAt { get; init; }
    public string? UpdatedAt { get; init; }
    public long? Revision { get; init; }
}
