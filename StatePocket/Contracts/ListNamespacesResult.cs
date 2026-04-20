using System.Text.Json.Serialization;

namespace StatePocket.Contracts;

internal sealed record ListNamespacesResult
{
    [JsonPropertyName("namespaces")]
    public required IReadOnlyList<string> Namespaces { get; init; }
    [JsonPropertyName("nextCursor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NextCursor { get; init; }
}
