using System.Text.Json;
using System.Text.Json.Serialization;

namespace StatePocket.Contracts;

internal sealed record GetValueResultData
{
    [JsonPropertyName("namespace")]
    public required string Namespace { get; init; }
    [JsonPropertyName("key")]
    public required string Key { get; init; }
    [JsonPropertyName("found")]
    public required bool Found { get; init; }
    [JsonPropertyName("path_found")]
    public required bool PathFound { get; init; }
    [JsonPropertyName("value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Value { get; init; }
    [JsonPropertyName("expires_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExpiresAt { get; init; }
}
