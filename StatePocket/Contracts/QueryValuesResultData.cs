using System.Text.Json.Serialization;

namespace StatePocket.Contracts;

internal sealed record QueryValuesResultData
{
    [JsonPropertyName("namespace")]
    public required string Namespace { get; init; }
    [JsonPropertyName("values")]
    public required IReadOnlyDictionary<string, GetValuesEntryData> Values { get; init; }
    [JsonPropertyName("nextCursor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NextCursor { get; init; }
}
