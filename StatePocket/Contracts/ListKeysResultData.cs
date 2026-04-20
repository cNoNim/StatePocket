using System.Text.Json.Serialization;

namespace StatePocket.Contracts;

internal sealed record ListKeysResultData
{
    [JsonPropertyName("namespace")]
    public required string Namespace { get; init; }
    [JsonPropertyName("keys")]
    public required IReadOnlyList<string> Keys { get; init; }
    [JsonPropertyName("nextCursor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NextCursor { get; init; }
}
