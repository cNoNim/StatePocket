using System.Text.Json;
using System.Text.Json.Serialization;

namespace StatePocket.Contracts;

internal sealed record GetValuesEntry
{
    [JsonPropertyName("found")]
    public required bool Found { get; init; }
    [JsonPropertyName("pathFound")]
    public required bool PathFound { get; init; }
    [JsonPropertyName("value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Value { get; init; }
    [JsonPropertyName("expiresAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExpiresAt { get; init; }
    [JsonPropertyName("updatedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UpdatedAt { get; init; }
    [JsonPropertyName("revision")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Revision { get; init; }
}
