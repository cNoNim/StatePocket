using System.Text.Json;

namespace StatePocket.Contracts;

internal sealed record PatchValueResult
{
    public required string Namespace { get; init; }
    public required string Key { get; init; }
    public required JsonElement Value { get; init; }
    public string? ExpiresAt { get; init; }
    public required string UpdatedAt { get; init; }
    public required long Revision { get; init; }
}
