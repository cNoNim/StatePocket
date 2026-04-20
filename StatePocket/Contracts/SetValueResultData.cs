using System.Text.Json.Serialization;

namespace StatePocket.Contracts;

internal sealed record SetValueResultData
{
    [JsonPropertyName("namespace")]
    public required string Namespace { get; init; }
    [JsonPropertyName("key")]
    public required string Key { get; init; }
    [JsonPropertyName("expires_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExpiresAt { get; init; }
}
