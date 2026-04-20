using System.Text.Json.Serialization;

namespace StatePocket.Contracts;

internal sealed record SetValueResult
{
    [JsonPropertyName("namespace")]
    public required string Namespace { get; init; }
    [JsonPropertyName("key")]
    public required string Key { get; init; }
    [JsonPropertyName("expiresAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExpiresAt { get; init; }
    [JsonPropertyName("updatedAt")]
    public required string UpdatedAt { get; init; }
    [JsonPropertyName("revision")]
    public required long Revision { get; init; }
}
