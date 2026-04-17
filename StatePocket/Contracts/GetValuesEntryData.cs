using System.Text.Json;
using System.Text.Json.Serialization;

namespace StatePocket.Contracts;

internal sealed record GetValuesEntryData
{
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
