using System.Text.Json;

namespace StatePocket.Storage;

internal sealed class KvValue
{
    public required JsonElement Value { get; init; }
    public string? ExpiresAt { get; init; }
}
