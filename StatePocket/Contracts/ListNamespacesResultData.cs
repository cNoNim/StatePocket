using System.Text.Json.Serialization;

namespace StatePocket.Contracts;

internal sealed record ListNamespacesResultData
{
    [JsonPropertyName("namespaces")]
    public required IReadOnlyList<string> Namespaces { get; init; }
    [JsonPropertyName("next_cursor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NextCursor { get; init; }
}
