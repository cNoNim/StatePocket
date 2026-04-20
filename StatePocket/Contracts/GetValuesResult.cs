using System.Text.Json.Serialization;

namespace StatePocket.Contracts;

internal sealed record GetValuesResult
{
    [JsonPropertyName("namespace")]
    public required string Namespace { get; init; }
    [JsonPropertyName("values")]
    public required IReadOnlyDictionary<string, GetValuesEntry> Values { get; init; }
    [JsonPropertyName("nextCursor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NextCursor { get; init; }
}
